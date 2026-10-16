import Foundation
import Darwin

struct DiscoveredHost: Identifiable, Hashable {
    let name: String
    let host: String
    let port: UInt16
    let mc: String

    var id: String { "\(host):\(port)" }
}

/// Finds MCVibe mods on the local network by broadcasting a UDP probe
/// and listening for JSON replies.
final class Discovery {
    var onHosts: (([DiscoveredHost]) -> Void)?

    private let queue = DispatchQueue(label: "mcvibe.discovery")
    private var fd: Int32 = -1
    private var running = false
    private var hosts: [String: (host: DiscoveredHost, seen: Date)] = [:]
    private var lastPublished: [DiscoveredHost] = []
    private let broadcastPort: UInt16 = 25667

    func start() {
        queue.async { [weak self] in
            guard let self, !self.running else { return }
            self.running = true
            self.openSocket()
            self.loop()
        }
    }

    func stop() {
        // Stop the loop synchronously; the socket cleanup runs right after it exits.
        running = false
        queue.async { [weak self] in
            guard let self else { return }
            if self.fd >= 0 {
                close(self.fd)
                self.fd = -1
            }
        }
    }

    private func openSocket() {
        fd = socket(AF_INET, SOCK_DGRAM, 0)
        guard fd >= 0 else { return }

        var one: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &one, socklen_t(MemoryLayout<Int32>.size))
        setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &one, socklen_t(MemoryLayout<Int32>.size))

        var tv = timeval(tv_sec: 0, tv_usec: 300_000)
        setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))

        var local = sockaddr_in()
        local.sin_family = sa_family_t(AF_INET)
        local.sin_port = 0
        local.sin_addr = in_addr(s_addr: 0)
        _ = withUnsafePointer(to: &local) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sa in
                bind(fd, sa, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
    }

    private func loop() {
        var tick = 0
        while running, fd >= 0 {
            if tick % 3 == 0 { sendProbe() }
            receiveOne()
            pruneAndPublish()
            tick += 1
            usleep(220_000)
        }
    }

    private func sendProbe() {
        var dest = sockaddr_in()
        dest.sin_family = sa_family_t(AF_INET)
        dest.sin_port = broadcastPort.bigEndian
        dest.sin_addr = in_addr(s_addr: inet_addr("255.255.255.255"))

        let payload = Array("PLANTAPI_DISCOVER".utf8)
        payload.withUnsafeBytes { raw in
            withUnsafePointer(to: &dest) { ptr in
                ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sa in
                    _ = sendto(fd, raw.baseAddress, raw.count, 0, sa, socklen_t(MemoryLayout<sockaddr_in>.size))
                }
            }
        }
    }

    private func receiveOne() {
        var from = sockaddr_in()
        var fromLen = socklen_t(MemoryLayout<sockaddr_in>.size)
        var buf = [UInt8](repeating: 0, count: 2048)

        let n = withUnsafeMutablePointer(to: &from) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sa in
                recvfrom(fd, &buf, buf.count, 0, sa, &fromLen)
            }
        }
        guard n > 0 else { return }

        let data = Data(buf[0..<Int(n)])
        guard
            let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
            obj["magic"] as? String == "PLANTAPI",
            let portNumber = obj["port"] as? Int,
            let port = UInt16(exactly: portNumber),
            let cstr = inet_ntoa(from.sin_addr)
        else { return }

        let hostIP = String(cString: cstr)
        let host = DiscoveredHost(
            name: (obj["name"] as? String) ?? hostIP,
            host: hostIP,
            port: port,
            mc: (obj["mc"] as? String) ?? "?"
        )
        hosts[host.id] = (host, Date())
    }

    private func pruneAndPublish() {
        let cutoff = Date().addingTimeInterval(-6)
        hosts = hosts.filter { $0.value.seen > cutoff }
        let list = hosts.values.map { $0.host }.sorted { $0.name < $1.name }
        guard list != lastPublished else { return }
        lastPublished = list
        onHosts?(list)
    }
}
