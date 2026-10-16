import Foundation
import Network

/// TCP client that connects to the MCVibe mod's server on the PC.
/// Receives newline-delimited JSON messages. Auto-reconnects while enabled.
final class LinkClient {
    enum Phase: Equatable {
        case idle
        case connecting
        case connected
    }

    var onPhase: ((Phase, String) -> Void)?
    var onMessage: ((VibeMessage) -> Void)?

    private let queue = DispatchQueue(label: "mcvibe.link")
    private var connection: NWConnection?
    private var buffer = Data()
    private var host = ""
    private var port: UInt16 = 0
    private var wantsConnection = false
    private var retryWork: DispatchWorkItem?

    func connect(host: String, port: UInt16) {
        queue.async {
            self.host = host
            self.port = port
            self.wantsConnection = true
            self.startConnection()
        }
    }

    func disconnect() {
        queue.async {
            self.wantsConnection = false
            self.retryWork?.cancel()
            self.retryWork = nil
            self.connection?.cancel()
            self.connection = nil
            self.emit(.idle, "Not connected")
        }
    }

    private func startConnection() {
        guard wantsConnection, !host.isEmpty, port != 0 else { return }
        retryWork?.cancel()
        retryWork = nil
        connection?.stateUpdateHandler = nil
        connection?.cancel()

        buffer.removeAll()
        let params = NWParameters.tcp
        params.allowLocalEndpointReuse = true

        let conn = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: params)
        connection = conn
        emit(.connecting, "Connecting to \(host)…")

        conn.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                self.emit(.connected, "Connected")
                self.sendRaw(#"{"type":"hello","proto":1,"client":"MCVibe iOS"}"# + "\n")
                self.receiveLoop(on: conn)
            case .waiting(let err):
                self.emit(.connecting, "Waiting… \(err.localizedDescription)")
            case .failed(let err):
                self.emit(.idle, "Connection failed: \(err.localizedDescription)")
                self.scheduleRetry()
            case .cancelled:
                break
            default:
                break
            }
        }
        conn.start(queue: queue)
    }

    private func scheduleRetry() {
        guard wantsConnection else { return }
        retryWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.startConnection() }
        retryWork = work
        queue.asyncAfter(deadline: .now() + 2.0, execute: work)
    }

    private func receiveLoop(on conn: NWConnection) {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { return }
            if let data, !data.isEmpty {
                self.buffer.append(data)
                self.drainLines()
            }
            if let error {
                self.emit(.idle, "Disconnected: \(error.localizedDescription)")
                self.scheduleRetry()
                return
            }
            if isComplete {
                self.emit(.idle, "Disconnected")
                self.scheduleRetry()
                return
            }
            self.receiveLoop(on: conn)
        }
    }

    private func drainLines() {
        while let idx = buffer.firstIndex(of: 0x0A) {
            let lineData = buffer.prefix(upTo: idx)
            buffer.removeFirst(idx + 1)
            guard !lineData.isEmpty else { continue }
            if let msg = try? JSONDecoder().decode(VibeMessage.self, from: Data(lineData)) {
                onMessage?(msg)
            }
        }
    }

    func sendRaw(_ s: String) {
        connection?.send(content: Data(s.utf8), completion: .contentProcessed { _ in })
    }

    private func emit(_ p: Phase, _ detail: String) {
        onPhase?(p, detail)
    }
}
