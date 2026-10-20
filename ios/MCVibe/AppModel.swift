import SwiftUI
import UIKit

@MainActor
final class AppModel: ObservableObject {
    @Published var phase: LinkClient.Phase = .idle
    @Published var statusText = "Not connected"
    @Published var hosts: [DiscoveredHost] = []
    @Published var locked = false
    @Published var manualInput = ""
    @Published var lastEvent = ""
    @Published var activeLoop: HapticLoop?

    /// Intensity for looping textures, persisted between launches.
    @Published var loopLevel: Double = 70 {
        didSet { UserDefaults.standard.set(loopLevel, forKey: "loopLevel") }
    }

    enum Transport: String {
        case wifi
        case bluetooth
    }

    @Published var transport: Transport = .wifi {
        didSet {
            guard transport != oldValue else { return }
            UserDefaults.standard.set(transport.rawValue, forKey: "transport")
            if phase != .idle {
                disconnect()
            }
        }
    }

    /// Test switch: when on, incoming events are still shown but nothing vibrates.
    /// Session-only on purpose: a persisted mute once made "no vibration" very
    /// hard to diagnose, so it always starts OFF after a relaunch.
    @Published var hapticsMuted: Bool = false {
        didSet {
            if hapticsMuted {
                haptics.setConstant(0)
                if activeLoop != nil { stopLoop() }
            } else {
                applyConstants()
            }
        }
    }

    private let link = LinkClient()
    private let bluetooth = BluetoothLink()
    private let discovery = Discovery()
    private let haptics = HapticsEngine.shared
    private var constants: [String: Double] = [:]
    private var connectedName = ""

    init() {
        manualInput = UserDefaults.standard.string(forKey: "lastHostInput") ?? ""
        loopLevel = (UserDefaults.standard.object(forKey: "loopLevel") as? Double) ?? 70
        transport = Transport(rawValue: UserDefaults.standard.string(forKey: "transport") ?? "") ?? .wifi
        AppLog.add("app launched (v6)")

        link.onPhase = { [weak self] phase, detail in
            Task { @MainActor in self?.applyPhase(phase, detail) }
        }
        link.onMessage = { [weak self] msg in
            Task { @MainActor in self?.handle(msg) }
        }
        bluetooth.onPhase = { [weak self] phase, detail in
            Task { @MainActor in self?.applyPhase(phase, detail) }
        }
        bluetooth.onMessage = { [weak self] msg in
            Task { @MainActor in self?.handle(msg) }
        }
        discovery.onHosts = { [weak self] hosts in
            Task { @MainActor in self?.hosts = hosts }
        }
    }

    func start() {
        discovery.start()
        // NOTE: On purpose, the app does NOT auto-connect on launch.
        // Re-connecting to the last host automatically turned any problem in
        // the connect path into a crash loop that made the whole app unusable.
        // Tap a discovered PC or type the IP to connect.
    }

    func connect(host: String, port: UInt16) {
        if transport == .bluetooth {
            connectBluetooth()
            return
        }
        stopInactiveTransports(active: .wifi)
        UserDefaults.standard.set(host, forKey: "lastHost")
        UserDefaults.standard.set(Int(port), forKey: "lastPort")
        AppLog.add("connect to \(host):\(port)")
        link.connect(host: host, port: port)
    }

    func connectBluetooth() {
        stopInactiveTransports(active: .bluetooth)
        AppLog.add("connect over bluetooth")
        bluetooth.connect(host: "", port: 0)
    }

    private func stopInactiveTransports(active: Transport) {
        if active != .wifi { link.disconnect() }
        if active != .bluetooth { bluetooth.disconnect() }
    }

    func connectManual() {
        var text = manualInput.trimmingCharacters(in: .whitespacesAndNewlines)
        var port: UInt16 = 25666
        if let colon = text.lastIndex(of: ":"), let parsed = UInt16(text[text.index(after: colon)...]) {
            port = parsed
            text = String(text[..<colon])
        }
        guard !text.isEmpty else { return }
        UserDefaults.standard.set(manualInput, forKey: "lastHostInput")
        connect(host: text, port: port)
    }

    func disconnect() {
        link.disconnect()
        bluetooth.disconnect()
    }

    /// Plays a burst directly on the phone — no network involved. Use this to
    /// verify that phone-side haptics work at all.
    func testVibration() {
        AppLog.add("in-app test vibration")
        haptics.burst(intensity: 80, durationMs: 240, pulses: 2)
        lastEvent = "⚡ in-app test • 80%"
    }

    func toggleLock() {
        locked.toggle()
        if locked { stopLoop() }
        UIApplication.shared.isIdleTimerDisabled = locked || phase == .connected
    }

    // MARK: - Phone-only haptic loops

    /// Starts or stops a looping texture. Completely local: no link involved.
    func toggleLoop(_ loop: HapticLoop) {
        if activeLoop == loop {
            stopLoop()
            return
        }
        if hapticsMuted { hapticsMuted = false }
        activeLoop = loop
        let level = min(max(loopLevel, 10), 100)
        haptics.startLoop(loop, intensity: level)
        lastEvent = "◉ \(loop.name) loop · \(Int(level))%"
        AppLog.add("loop start: \(loop.rawValue) at \(Int(level))%")
    }

    func stopLoop() {
        guard activeLoop != nil else { return }
        activeLoop = nil
        haptics.stopLoop()
        if lastEvent.hasPrefix("◉") { lastEvent = "" }
        AppLog.add("loop stopped")
    }

    /// Applies the slider value once the user lets go (one rebuild, not one per tick).
    func applyLoopLevel() {
        guard let loop = activeLoop else { return }
        haptics.startLoop(loop, intensity: loopLevel)
        lastEvent = "◉ \(loop.name) loop · \(Int(loopLevel))%"
        AppLog.add("loop level \(Int(loopLevel))%")
    }

    private func applyPhase(_ phase: LinkClient.Phase, _ detail: String) {
        self.phase = phase
        AppLog.add("status: \(detail)")
        if phase == .connected {
            statusText = connectedName.isEmpty ? "Connected" : "Connected to \(connectedName)"
            UIApplication.shared.isIdleTimerDisabled = true
        } else {
            statusText = detail
            constants.removeAll()
            haptics.setConstant(0)
            connectedName = ""
            UIApplication.shared.isIdleTimerDisabled = locked
        }
    }

    private func handle(_ msg: VibeMessage) {
        switch msg.type {
        case "hello":
            connectedName = msg.name ?? "PC"
            let mc = msg.mc.flatMap { $0.isEmpty ? nil : " • MC \($0)" } ?? ""
            statusText = "Connected to \(connectedName)\(mc)"
            lastEvent = ""
            AppLog.add("hello from \(connectedName)\(mc)")

        case "burst":
            // A burst is raised to the level of the strongest active constant,
            // so you never feel a weaker tap than the ongoing vibration.
            let floor = constants.values.max() ?? 0
            let raw = max(msg.intensity ?? 50, floor)
            let level = raw.isFinite ? raw : floor
            if hapticsMuted {
                AppLog.add("burst suppressed (muted)")
                lastEvent = "⚡ \(msg.id ?? "burst") • \(Int(level.rounded()))% (muted)"
            } else {
                haptics.burst(intensity: level, durationMs: msg.durationMs ?? 200, pulses: msg.pulses ?? 1)
                lastEvent = "⚡ \(msg.id ?? "burst") • \(Int(level.rounded()))%"
            }

        case "constants":
            // Full state resync from the PC: replaces the local map so a missed
            // message can never leave a constant missing or stuck on.
            if let active = msg.active {
                constants = active
                applyConstants()
                if let top = constants.max(by: { $0.value < $1.value }) {
                    lastEvent = "≈ \(top.key) • \(Int(top.value))%"
                } else {
                    lastEvent = ""
                }
            }

        case "constant":
            if let id = msg.id {
                constants[id] = msg.intensity ?? 0
                applyConstants()
                lastEvent = "≈ \(id) • \(Int(constants.values.max() ?? 0))%"
                AppLog.add("constant \(id) = \(Int(msg.intensity ?? 0))%")
            }

        case "constant_end":
            if let id = msg.id {
                constants.removeValue(forKey: id)
                applyConstants()
            }

        case "clear":
            constants.removeAll()
            applyConstants()
            lastEvent = "cleared"

        default:
            break
        }
    }

    private func applyConstants() {
        haptics.setConstant(hapticsMuted ? 0 : (constants.values.max() ?? 0))
    }
}
