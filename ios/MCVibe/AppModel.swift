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

    private let link = LinkClient()
    private let discovery = Discovery()
    private let haptics = HapticsEngine.shared
    private var constants: [String: Double] = [:]
    private var connectedName = ""

    init() {
        manualInput = UserDefaults.standard.string(forKey: "lastHostInput") ?? ""

        link.onPhase = { [weak self] phase, detail in
            Task { @MainActor in self?.applyPhase(phase, detail) }
        }
        link.onMessage = { [weak self] msg in
            Task { @MainActor in self?.handle(msg) }
        }
        discovery.onHosts = { [weak self] hosts in
            Task { @MainActor in self?.hosts = hosts }
        }
    }

    func start() {
        discovery.start()
        if let savedHost = UserDefaults.standard.string(forKey: "lastHost"),
           let savedPort = UserDefaults.standard.object(forKey: "lastPort") as? Int,
           let port = UInt16(exactly: savedPort) {
            connect(host: savedHost, port: port)
        }
    }

    func connect(host: String, port: UInt16) {
        UserDefaults.standard.set(host, forKey: "lastHost")
        UserDefaults.standard.set(Int(port), forKey: "lastPort")
        link.connect(host: host, port: port)
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
    }

    func toggleLock() {
        locked.toggle()
        UIApplication.shared.isIdleTimerDisabled = locked || phase == .connected
    }

    private func applyPhase(_ phase: LinkClient.Phase, _ detail: String) {
        self.phase = phase
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
            let mc = msg.mc.map { " • MC \($0)" } ?? ""
            statusText = "Connected to \(connectedName)\(mc)"
            lastEvent = ""

        case "burst":
            // A burst is raised to the level of the strongest active constant,
            // so you never feel a weaker tap than the ongoing vibration.
            let floor = constants.values.max() ?? 0
            let level = max(msg.intensity ?? 50, floor)
            haptics.burst(intensity: level, durationMs: msg.durationMs ?? 200, pulses: msg.pulses ?? 1)
            lastEvent = "⚡ \(msg.id ?? "burst") • \(Int(level.rounded()))%"

        case "constant":
            if let id = msg.id {
                constants[id] = msg.intensity ?? 0
                applyConstants()
                lastEvent = "≈ \(id) • \(Int(constants.values.max() ?? 0))%"
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
        haptics.setConstant(constants.values.max() ?? 0)
    }
}
