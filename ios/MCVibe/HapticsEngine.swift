import Foundation
import CoreHaptics
import UIKit
import AVFoundation

/// Plays burst and constant haptics with CoreHaptics.
/// Intensity is given as 0...100, where 100 = the strongest vibration the phone can produce.
///
/// Design:
/// - Bursts are TRANSIENT events (one per pulse). Transients use the full peak
///   strength of the haptic hardware, so pulses feel much stronger than a short
///   continuous rumble.
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

    private var lastBurstAt = Date.distantPast
    private var lastBurstLogAt = Date.distantPast

    private init() {
        guard supported else {
            AppLog.add("haptics: not supported on this device")
            return
        }
        configureAudioSession()
        startEngine()
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
        } catch {
            AppLog.add("haptics: engine restart failed")
        }
    }

    /// Burst = a train of transient taps. Intensity 0...100, span in ms, 1...5 pulses.
    func burst(intensity: Double, durationMs: Int, pulses: Int) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        guard level.isFinite, level > 0.01 else { return }
        let count = min(max(pulses, 1), 5)
        let span = Double(min(max(durationMs, 40), 2000)) / 1000.0
        let spacing = count > 1 ? max(span / Double(count - 1), 0.06) : 0

        // Flood guard: never hammer CoreHaptics faster than ~20 bursts per second.
        let now = Date()
        guard now.timeIntervalSince(lastBurstAt) > 0.05 else { return }
        lastBurstAt = now
        if now.timeIntervalSince(lastBurstLogAt) > 0.5 {
            lastBurstLogAt = now
            AppLog.add("haptic burst \(Int(level * 100))% x\(count)")
        }

        guard supported, let eng = ensureEngine() else {
            fallbackBurst(level: level, pulses: count, spacing: spacing)
            return
        }
        do {
            var events: [CHHapticEvent] = []
            for i in 0..<count {
                let params = [
                    CHHapticEventParameter(parameterID: .hapticIntensity, value: Float(level)),
                    CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.7),
                ]
                events.append(CHHapticEvent(
                    eventType: .hapticTransient,
                    parameters: params,
                    relativeTime: Double(i) * spacing
                ))
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
    func setConstant(_ intensity: Double) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        let safe = level.isFinite ? level : 0
        guard abs(safe - currentConstant) > 0.005 else { return }
        currentConstant = safe
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
            let params = [
                CHHapticEventParameter(parameterID: .hapticIntensity, value: Float(currentConstant)),
                CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.25),
            ]
            let event = CHHapticEvent(eventType: .hapticContinuous, parameters: params, relativeTime: 0, duration: 2.5)
            let pattern = try CHHapticPattern(events: [event], parameters: [])
            let player = try eng.makeAdvancedPlayer(with: pattern)
            player.loopEnabled = true
            try player.start(atTime: CHHapticTimeImmediate)
            constantPlayer = player
        } catch {
            constantPlayer = nil
            AppLog.add("haptics: constant failed \(error.localizedDescription)")
        }
    }

    func stopAll() {
        currentConstant = 0
        rebuildConstant()
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
