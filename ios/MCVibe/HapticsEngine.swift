import Foundation
import CoreHaptics
import UIKit
import AVFoundation

/// Plays burst and constant haptics with CoreHaptics.
/// Intensity is given as 0...100, where 100 = the strongest vibration the phone can produce.
///
/// Design:
/// - Bursts are trains of TRANSIENT events: several taps span each pulse, so the
///   burst reads as longer and harder while still using the full peak strength
///   of the haptic hardware (stronger than a continuous rumble of equal level).
/// - The constant (looping) vibration is a single advanced player with a
///   continuous event.
/// - Every play path re-checks the engine and restarts it when needed, and all
///   failures go to the in-app log instead of failing silently.
@MainActor
final class HapticsEngine {
    static let shared = HapticsEngine()

    private let supported = CHHapticEngine.capabilitiesForHardware().supportsHaptics
    private var engine: CHHapticEngine?
    private var engineRunning = false

    private var constantPlayer: CHHapticAdvancedPatternPlayer?
    private var retiringConstantPlayer: CHHapticAdvancedPatternPlayer?
    private var currentConstant: Double = 0

    // Phone-only looping textures (see HapticLoop). The engine loops these
    // natively, so keeping one alive costs no timers or per-beat work.
    private var loopPlayer: CHHapticAdvancedPatternPlayer?
    private var retiringLoopPlayers: [CHHapticAdvancedPatternPlayer] = []
    private var activeLoop: HapticLoop?
    private var activeLoopLevel: Double = 0

    private var lastBurstAt = Date.distantPast
    private var lastBurstLogAt = Date.distantPast

    private init() {
        guard supported else {
            AppLog.add("haptics: not supported on this device")
            return
        }
        configureAudioSession()
        startEngine()

        // Calls and other interruptions can kill the engine; when the app comes
        // back, restart a loop that was playing.
        NotificationCenter.default.addObserver(
            forName: UIApplication.didBecomeActiveNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.recoverAfterForeground() }
        }
    }

    private func recoverAfterForeground() {
        guard activeLoop != nil else { return }
        if !engineRunning { startEngine() }
        rebuildLoopPlayer()
    }

    /// Ambient + mix so haptics never interrupt the user's music, and so the
    /// system keeps the taptic engine serviced for CoreHaptics.
    private func configureAudioSession() {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.ambient, mode: .default, options: [.mixWithOthers])
            try session.setActive(true)
        } catch {
            AppLog.add("haptics: audio session error \(error.localizedDescription)")
        }
    }

    private func startEngine() {
        guard supported else { return }
        do {
            let e = try CHHapticEngine()
            e.isAutoShutdownEnabled = false
            e.stoppedHandler = { [weak self] _ in
                Task { @MainActor in
                    self?.engineRunning = false
                    self?.restart()
                }
            }
            e.resetHandler = { [weak self] in
                Task { @MainActor in
                    self?.engineRunning = false
                    self?.restart()
                }
            }
            try e.start()
            engine = e
            engineRunning = true
            AppLog.add("haptics: engine started")
        } catch {
            engine = nil
            engineRunning = false
            AppLog.add("haptics: engine start failed \(error.localizedDescription)")
        }
    }

    private func ensureEngine() -> CHHapticEngine? {
        if engineRunning, let engine { return engine }
        startEngine()
        return engineRunning ? engine : nil
    }

    private func restart() {
        guard let engine, !engineRunning else { return }
        do {
            try engine.start()
            engineRunning = true
            AppLog.add("haptics: engine restarted")
            retiringConstantPlayer = constantPlayer
            constantPlayer = nil
            if currentConstant > 0 { rebuildConstant() }

            // The old players died with the engine; start the loop again.
            loopPlayer = nil
            retiringLoopPlayers.removeAll()
            if activeLoop != nil { rebuildLoopPlayer() }
        } catch {
            AppLog.add("haptics: engine restart failed")
        }
    }

    /// Burst = a train of transient taps. Intensity 0...100, span in ms, 1...5 pulses.
    /// Transients are instantaneous by design; a rapid train of them at full
    /// strength reads as one longer, harder tap (and stays stronger than a
    /// continuous rumble of the same level).
    func burst(intensity: Double, durationMs: Int, pulses: Int) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        guard level.isFinite, level > 0.01 else { return }
        let count = min(max(pulses, 1), 5)
        let span = Double(min(max(durationMs, 40), 2000)) / 1000.0
        let spacing = count > 1 ? max(span / Double(count - 1), 0.06) : 0
        let pulseSpan = count > 1 ? spacing : span

        // One pulse = a short train of transients spanning the pulse duration.
        let maxGap = 0.055
        let taps = min(10, max(1, Int((pulseSpan / maxGap).rounded())))
        let gap = taps > 1 ? min(maxGap, pulseSpan / Double(taps - 1)) : 0

        // Flood guard: never hammer CoreHaptics faster than ~20 bursts per second.
        let now = Date()
        guard now.timeIntervalSince(lastBurstAt) > 0.05 else { return }
        lastBurstAt = now
        if now.timeIntervalSince(lastBurstLogAt) > 0.5 {
            lastBurstLogAt = now
            AppLog.add("haptic burst \(Int(level * 100))% x\(count) (\(taps) taps/pulse)")
        }

        guard supported, let eng = ensureEngine() else {
            fallbackBurst(level: level, pulses: count, spacing: spacing)
            return
        }
        do {
            var events: [CHHapticEvent] = []
            for i in 0..<count {
                let t0 = Double(i) * spacing
                for k in 0..<taps {
                    let params = [
                        CHHapticEventParameter(parameterID: .hapticIntensity, value: Float(level)),
                        CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.7),
                    ]
                    events.append(CHHapticEvent(
                        eventType: .hapticTransient,
                        parameters: params,
                        relativeTime: t0 + Double(k) * gap
                    ))
                }
            }
            let pattern = try CHHapticPattern(events: events, parameters: [])
            let player = try eng.makePlayer(with: pattern)
            try player.start(atTime: CHHapticTimeImmediate)
        } catch {
            AppLog.add("haptics: burst failed \(error.localizedDescription)")
            startEngine()
            fallbackBurst(level: level, pulses: count, spacing: spacing)
        }
    }

    /// Constant vibration level 0...100. 0 stops it.
    /// Updates are applied to the RUNNING player through the intensity-control
    /// parameter, so the constant tracks game state smoothly and as fast as
    /// updates arrive (no restart glitches between updates).
    func setConstant(_ intensity: Double) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        let safe = level.isFinite ? level : 0
        guard abs(safe - currentConstant) > 0.002 else { return }
        currentConstant = safe
        if safe <= 0.005 {
            AppLog.add("haptic constant off")
            rebuildConstant()
            return
        }
        if let player = constantPlayer, engineRunning {
            do {
                let param = CHHapticDynamicParameter(
                    parameterID: .hapticIntensityControl,
                    value: Float(safe),
                    relativeTime: 0
                )
                try player.sendParameters([param], atTime: CHHapticTimeImmediate)
                AppLog.add("haptic constant \(Int(safe * 100))% (live)")
                return
            } catch {
                AppLog.add("haptics: live constant update failed, rebuilding")
            }
        }
        AppLog.add("haptic constant \(Int(safe * 100))%")
        rebuildConstant()
    }

    private func rebuildConstant() {
        if let player = constantPlayer {
            player.loopEnabled = false
            try? player.stop(atTime: CHHapticTimeImmediate)
            // Keep the stopped player alive instead of releasing it right away:
            // freeing an advanced player immediately after stop() is a known
            // source of CoreHaptics crashes.
            retiringConstantPlayer = player
            constantPlayer = nil
        }
        guard supported, currentConstant > 0.005, let eng = ensureEngine() else { return }
        do {
            // Base intensity 1.0; the actual strength is driven live through the
            // intensity-control parameter so the running player can be retuned
            // without restarting.
            let params = [
                CHHapticEventParameter(parameterID: .hapticIntensity, value: 1.0),
                CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.25),
            ]
            let event = CHHapticEvent(eventType: .hapticContinuous, parameters: params, relativeTime: 0, duration: 2.5)
            let pattern = try CHHapticPattern(events: [event], parameters: [])
            let player = try eng.makeAdvancedPlayer(with: pattern)
            player.loopEnabled = true
            try player.start(atTime: CHHapticTimeImmediate)
            let param = CHHapticDynamicParameter(
                parameterID: .hapticIntensityControl,
                value: Float(currentConstant),
                relativeTime: 0
            )
            try? player.sendParameters([param], atTime: CHHapticTimeImmediate)
            constantPlayer = player
        } catch {
            constantPlayer = nil
            AppLog.add("haptics: constant failed \(error.localizedDescription)")
        }
    }

    // MARK: - Phone-only loops

    /// Starts, switches or retunes a native looping pattern. Level is 0...100.
    func startLoop(_ loop: HapticLoop, intensity: Double) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        guard level.isFinite, level > 0.01 else { stopLoop(); return }

        let unchanged = activeLoop == loop && abs(level - activeLoopLevel) < 0.001
        activeLoop = loop
        activeLoopLevel = level
        guard !unchanged else { return }

        rebuildLoopPlayer()
        AppLog.add("haptic loop \(loop.rawValue) at \(Int(level * 100))%")
    }

    func stopLoop() {
        let wasActive = activeLoop != nil
        activeLoop = nil
        activeLoopLevel = 0
        retireLoopPlayer()
        if wasActive { AppLog.add("haptic loop stopped") }
    }

    private func rebuildLoopPlayer() {
        retireLoopPlayer()
        guard supported, let loop = activeLoop, activeLoopLevel > 0.01,
              let engine = ensureEngine() else { return }
        do {
            let pattern = try loop.makePattern(level: activeLoopLevel)
            let player = try engine.makeAdvancedPlayer(with: pattern)
            player.loopEnabled = true
            try player.start(atTime: CHHapticTimeImmediate)
            loopPlayer = player
        } catch {
            loopPlayer = nil
            AppLog.add("haptics: loop failed \(error.localizedDescription)")
        }
    }

    private func retireLoopPlayer() {
        guard let player = loopPlayer else { return }
        player.loopEnabled = false
        try? player.stop(atTime: CHHapticTimeImmediate)
        // Keep a few stopped players alive: releasing an advanced player right
        // after stop() is a known CoreHaptics crash source.
        retiringLoopPlayers.append(player)
        loopPlayer = nil
        if retiringLoopPlayers.count > 4 {
            retiringLoopPlayers.removeFirst()
        }
    }

    func stopAll() {
        currentConstant = 0
        rebuildConstant()
        stopLoop()
    }

    /// Fallback when CoreHaptics is unavailable or the engine cannot start:
    /// plain UIKit impacts, one per pulse.
    private func fallbackBurst(level: Double, pulses: Int, spacing: Double) {
        let style: UIImpactFeedbackGenerator.FeedbackStyle = level > 0.66 ? .heavy : (level > 0.33 ? .medium : .light)
        for i in 0..<pulses {
            let gen = UIImpactFeedbackGenerator(style: style)
            gen.prepare()
            DispatchQueue.main.asyncAfter(deadline: .now() + Double(i) * max(spacing, 0.06)) {
                gen.impactOccurred()
            }
        }
    }
}
