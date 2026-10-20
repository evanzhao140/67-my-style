import Foundation
import CoreHaptics

/// A haptic texture that loops natively on the phone.
///
/// Each pattern is compiled once by CoreHaptics and looped by the engine, so
/// there are no timers, no network traffic and no drift between repeats. The
/// haptic intensity is baked in when the pattern is built; changing the slider
/// rebuilds it once (on release), not on every drag tick.
enum HapticLoop: String, CaseIterable, Identifiable {
    case heartbeat
    case breathing
    case rain
    case purr
    case warp

    var id: String { rawValue }

    var name: String {
        switch self {
        case .heartbeat: return "Heartbeat"
        case .breathing: return "Breathing"
        case .rain: return "Rain"
        case .purr: return "Purr"
        case .warp: return "Warp"
        }
    }

    var subtitle: String {
        switch self {
        case .heartbeat: return "Lub-dub with a soft clock tick · 2 s loop"
        case .breathing: return "Slow swell in and out · 6 s loop"
        case .rain: return "Scattered soft drops · 4 s loop"
        case .purr: return "Warm motor purr · 1.2 s loop"
        case .warp: return "Accelerating taps into a whoosh · 3.3 s loop"
        }
    }

    var systemImage: String {
        switch self {
        case .heartbeat: return "heart.fill"
        case .breathing: return "wind"
        case .rain: return "cloud.rain.fill"
        case .purr: return "pawprint.fill"
        case .warp: return "sparkles"
        }
    }

    /// Builds the pattern with an intensity scale in 0...1.
    func makePattern(level: Double) throws -> CHHapticPattern {
        let amount = Float(min(max(level, 0), 1))
        switch self {
        case .heartbeat: return try Self.heartbeatPattern(amount)
        case .breathing: return try Self.breathingPattern(amount)
        case .rain: return try Self.rainPattern(amount)
        case .purr: return try Self.purrPattern(amount)
        case .warp: return try Self.warpPattern(amount)
        }
    }

    // MARK: - Patterns

    /// Two heartbeats per loop, each a crisp tap plus a low body, with a quiet
    /// clock tick between them.
    private static func heartbeatPattern(_ level: Float) throws -> CHHapticPattern {
        let duration = 2.0
        var events: [CHHapticEvent] = []

        func beat(at time: Double, _ strength: Float) {
            events.append(tap(at: time, intensity: strength * level, sharpness: 0.25))
            events.append(rumble(at: time + 0.005, duration: 0.15,
                                 intensity: strength * 0.85 * level, sharpness: 0.05))
        }

        beat(at: 0.00, 1.00)
        beat(at: 0.22, 0.62)
        beat(at: 1.00, 0.92)
        beat(at: 1.22, 0.55)

        events.append(tap(at: 0.60, intensity: 0.09 * level, sharpness: 1.0))
        events.append(tap(at: 1.60, intensity: 0.09 * level, sharpness: 1.0))
        events.append(silenceMarker(at: duration))
        return try CHHapticPattern(events: events, parameters: [])
    }

    /// A continuous bed shaped by an intensity curve: inhale, hold, exhale.
    private static func breathingPattern(_ level: Float) throws -> CHHapticPattern {
        let duration = 6.0
        let bed = rumble(at: 0, duration: duration - 0.05, intensity: 1.0, sharpness: 0.12)
        let points = [
            CHHapticParameterCurve.ControlPoint(relativeTime: 0.0, value: 0.05 * level),
            CHHapticParameterCurve.ControlPoint(relativeTime: 1.7, value: 0.95 * level),
            CHHapticParameterCurve.ControlPoint(relativeTime: 2.7, value: 0.55 * level),
            CHHapticParameterCurve.ControlPoint(relativeTime: 4.5, value: 0.08 * level),
            CHHapticParameterCurve.ControlPoint(relativeTime: 6.0, value: 0.05 * level),
        ]
        let curve = CHHapticParameterCurve(parameterID: .hapticIntensityControl,
                                           controlPoints: points, relativeTime: 0)
        return try CHHapticPattern(events: [bed], parameterCurves: [curve])
    }

    /// Deterministic pseudo-random drizzle so the loop is identical every time.
    private static func rainPattern(_ level: Float) throws -> CHHapticPattern {
        let duration = 4.0
        var events: [CHHapticEvent] = []
        var seed: UInt64 = 0x9E37_79B9_7F4A_7C15

        func next() -> Double {
            seed = seed &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
            return Double((seed >> 33) & 0x7FFF_FFFF) / Double(0x7FFF_FFFF)
        }

        for _ in 0..<54 {
            let time = next() * (duration - 0.12)
            let roll = next()
            let strength = Float(0.08 + roll * roll * 0.52)
            let sharpness = Float(0.72 + next() * 0.28)
            events.append(tap(at: time, intensity: strength * level, sharpness: sharpness))
        }
        for _ in 0..<6 {
            let time = next() * (duration - 0.2)
            events.append(rumble(at: time, duration: 0.06,
                                 intensity: 0.22 * level, sharpness: 0.35))
        }
        events.append(silenceMarker(at: duration))
        return try CHHapticPattern(events: events, parameters: [])
    }

    /// A soft motor with a fast intensity wobble through a parameter curve.
    private static func purrPattern(_ level: Float) throws -> CHHapticPattern {
        let duration = 1.2
        var points: [CHHapticParameterCurve.ControlPoint] = []
        let cycles = 9
        let step = duration / Double(cycles)
        for index in 0...cycles {
            let value = Float(index % 2 == 0 ? 0.55 : 0.92) * level
            points.append(CHHapticParameterCurve.ControlPoint(
                relativeTime: Double(index) * step, value: value))
        }

        let motor = rumble(at: 0, duration: duration - 0.03, intensity: 1.0, sharpness: 0.03)
        let curve = CHHapticParameterCurve(parameterID: .hapticIntensityControl,
                                           controlPoints: points, relativeTime: 0)
        let ticks = [
            tap(at: 0.30, intensity: 0.10 * level, sharpness: 0.5),
            tap(at: 0.90, intensity: 0.08 * level, sharpness: 0.5),
            silenceMarker(at: duration),
        ]
        return try CHHapticPattern(events: [motor] + ticks, parameterCurves: [curve])
    }

    /// Taps accelerate and swell into a continuous whoosh, then land on an
    /// impact right before the loop restarts.
    private static func warpPattern(_ level: Float) throws -> CHHapticPattern {
        let duration = 3.3
        var events: [CHHapticEvent] = []

        var time = 0.0
        var gap = 0.30
        var strength: Float = 0.10
        for _ in 0..<20 {
            events.append(tap(at: time, intensity: strength * level,
                              sharpness: 0.25 + strength * 0.6))
            time += gap
            gap = max(0.055, gap * 0.86)
            strength = min(0.95, strength + 0.045)
        }

        for index in 0..<10 {
            let start = 0.45 + Double(index) * 0.22
            let intensity = Float(0.10 + Double(index) * 0.042) * level
            events.append(rumble(at: start, duration: 0.24, intensity: intensity, sharpness: 0.30))
        }

        events.append(tap(at: 3.02, intensity: 0.95 * level, sharpness: 0.55))
        events.append(rumble(at: 3.02, duration: 0.20, intensity: 0.45 * level, sharpness: 0.10))
        events.append(silenceMarker(at: duration))
        return try CHHapticPattern(events: events, parameters: [])
    }

    // MARK: - Event helpers

    private static func tap(at time: Double, intensity: Float, sharpness: Float) -> CHHapticEvent {
        CHHapticEvent(eventType: .hapticTransient, parameters: [
            CHHapticEventParameter(parameterID: .hapticIntensity, value: clamp(intensity)),
            CHHapticEventParameter(parameterID: .hapticSharpness, value: clamp(sharpness)),
        ], relativeTime: time)
    }

    private static func rumble(at time: Double, duration: Double,
                               intensity: Float, sharpness: Float) -> CHHapticEvent {
        CHHapticEvent(eventType: .hapticContinuous, parameters: [
            CHHapticEventParameter(parameterID: .hapticIntensity, value: clamp(intensity)),
            CHHapticEventParameter(parameterID: .hapticSharpness, value: clamp(sharpness)),
        ], relativeTime: time, duration: duration)
    }

    /// A near-silent transient that only exists to pin the loop length.
    private static func silenceMarker(at time: Double) -> CHHapticEvent {
        tap(at: time, intensity: 0.001, sharpness: 0)
    }

    private static func clamp(_ value: Float) -> Float {
        min(max(value, 0), 1)
    }
}
