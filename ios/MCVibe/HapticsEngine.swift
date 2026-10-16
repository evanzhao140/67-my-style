import Foundation
import CoreHaptics
import UIKit

/// Plays burst and constant haptics with CoreHaptics.
/// Intensity is given as 0...100, where 100 = the strongest vibration the phone can produce.
///
/// Design notes (crash safety):
/// - Bursts use plain one-shot players. The engine owns a one-shot player until
///   its pattern finishes, so there is nothing to track, stop or release early.
/// - The constant (looping) player is the only advanced player. When it is
///   stopped, the reference is kept in `retiringConstantPlayer` so CoreHaptics
///   never sees a player deallocated while it is still shutting down.
@MainActor
final class HapticsEngine {
    static let shared = HapticsEngine()

    private let supported = CHHapticEngine.capabilitiesForHardware().supportsHaptics
    private var engine: CHHapticEngine?

    private var constantPlayer: CHHapticAdvancedPatternPlayer?
    private var retiringConstantPlayer: CHHapticAdvancedPatternPlayer?
    private var currentConstant: Double = 0

    private var lastBurstAt = Date.distantPast
    private var lastBurstLogAt = Date.distantPast

    private init() {
        guard supported else { return }
        do {
            let e = try CHHapticEngine()
            e.isAutoShutdownEnabled = false
            e.playsHapticsOnly = true
            e.stoppedHandler = { [weak self] _ in
                Task { @MainActor in self?.restart() }
            }
            e.resetHandler = { [weak self] in
                Task { @MainActor in self?.restart() }
            }
            try e.start()
            engine = e
        } catch {
            engine = nil
        }
    }

    private func restart() {
        guard let engine else { return }
        do {
            try engine.start()
            retiringConstantPlayer = constantPlayer
            constantPlayer = nil
            if currentConstant > 0 { rebuildConstant() }
        } catch {
            // The engine retries on the next event.
        }
    }

    /// Burst vibration. Intensity 0...100, duration in ms, 1...5 pulses.
    func burst(intensity: Double, durationMs: Int, pulses: Int) {
        let level = min(max(intensity / 100.0, 0.0), 1.0)
        guard level.isFinite, level > 0.01 else { return }

        // Flood guard: never hammer CoreHaptics faster than ~20 bursts per second.
        let now = Date()
        guard now.timeIntervalSince(lastBurstAt) > 0.05 else { return }
        lastBurstAt = now
        if now.timeIntervalSince(lastBurstLogAt) > 0.5 {
            lastBurstLogAt = now
            AppLog.add("haptic burst \(Int(level * 100))%")
        }

        let dur = Double(min(max(durationMs, 40), 2000)) / 1000.0
        let count = min(max(pulses, 1), 5)

        guard supported, let engine else {
            fallbackBurst(level: level)
            return
        }

        do {
            var events: [CHHapticEvent] = []
            let gap = max(dur, 0.06)
            for i in 0..<count {
                let params = [
                    CHHapticEventParameter(parameterID: .hapticIntensity, value: Float(level)),
                    CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.6),
                ]
                events.append(CHHapticEvent(
                    eventType: .hapticContinuous,
                    parameters: params,
                    relativeTime: Double(i) * (dur + gap),
                    duration: dur
                ))
            }
            let pattern = try CHHapticPattern(events: events, parameters: [])
            let player = try engine.makePlayer(with: pattern)
            try player.start(atTime: CHHapticTimeImmediate)
        } catch {
            fallbackBurst(level: level)
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
        guard supported, let engine, currentConstant > 0.005 else { return }
        do {
            let params = [
                CHHapticEventParameter(parameterID: .hapticIntensity, value: Float(currentConstant)),
                CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.25),
            ]
            let event = CHHapticEvent(eventType: .hapticContinuous, parameters: params, relativeTime: 0, duration: 2.5)
            let pattern = try CHHapticPattern(events: [event], parameters: [])
            let player = try engine.makeAdvancedPlayer(with: pattern)
            player.loopEnabled = true
            try player.start(atTime: CHHapticTimeImmediate)
            constantPlayer = player
        } catch {
            constantPlayer = nil
        }
    }

    func stopAll() {
        currentConstant = 0
        rebuildConstant()
    }

    /// Fallback for devices without haptics support (simulator / iPad): a single impact.
    private func fallbackBurst(level: Double) {
        let style: UIImpactFeedbackGenerator.FeedbackStyle = level > 0.66 ? .heavy : (level > 0.33 ? .medium : .light)
        UIImpactFeedbackGenerator(style: style).impactOccurred()
    }
}
