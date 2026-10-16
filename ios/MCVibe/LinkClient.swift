import Foundation
import Darwin

/// TCP client built directly on BSD sockets, in the same style as the UDP
/// discovery socket. (The previous version used Network.framework, whose
/// callbacks are where the on-device crash happened.)
///
/// Everything runs on one private serial queue. `onPhase` and `onMessage`
/// are called on that queue; observers hop to the main actor themselves.
final class LinkClient {
    enum Phase: Equatable {
        case idle
        case connecting
        case connected
    }

    var onPhase: ((Phase, String) -> Void)?
    var onMessage: ((VibeMessage) -> Void)?

    private let queue = DispatchQueue(label: "mcvibe.link")
    private var fd: Int32 = -1
    private var buffer = Data()
    private var host = ""
    private var port: UInt16 = 0
    private var wantsConnection = false

    /// Generation counter. Incremented on every connect/disconnect so that
    /// stale receive loops and retries from an older session do nothing.
    private var gen = 0

    // MARK: - Public API

    func connect(host: String, port: UInt16) {
        queue.async {
            self.host = host
            self.port = port
            self.wantsConnection = true
            self.gen += 1
            self.teardown()
            self.startLocked()
        }
    }

    func disconnect() {
        queue.async {
            self.wantsConnection = false
            self.gen += 1
            self.teardown()
            self.emit(.idle, "Not connected")
        }
    }

    func sendRaw(_ s: String) {
        queue.async { self.sendRawLocked(s) }
    }

    // MARK: - Connection lifecycle (queue-confined)

    private func startLocked() {
        guard wantsConnection, !host.isEmpty, port != 0 else { return }
        guard let addr = resolveIPv4(host) else {
            AppLog.add("resolve failed for \(host)")
            emit(.idle, "Cannot resolve \(host)")
            scheduleRetryLocked()
            return
        }

        let sock = socket(AF_INET, SOCK_STREAM, 0)
        guard sock >= 0 else {
            AppLog.add("socket() failed errno=\(errno)")
            emit(.idle, "Connection failed")
            scheduleRetryLocked()
            return
        }
        var on: Int32 = 1
        setsockopt(sock, SOL_SOCKET, SO_NOSIGPIPE, &on, socklen_t(MemoryLayout<Int32>.size))

        var sa = sockaddr_in()
        sa.sin_family = sa_family_t(AF_INET)
        sa.sin_port = port.bigEndian
        sa.sin_addr = addr

        emit(.connecting, "Connecting to \(host)…")
        AppLog.add("tcp connect \(host):\(port)")

        // Non-blocking connect with a hard 4 s timeout, so an unreachable
        // address can never hang the queue for long.
        _ = fcntl(sock, F_SETFL, O_NONBLOCK)
        let rc = withUnsafePointer(to: &sa) { ptr in
            ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { sap in
                connect(sock, sap, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        var connected = false
        if rc == 0 {
            connected = true
        } else if errno == EINPROGRESS {
            var pfd = pollfd(fd: sock, events: Int16(POLLOUT), revents: 0)
            let pr = poll(&pfd, 1, 4000)
            if pr == 1 && (pfd.revents & Int16(POLLOUT)) != 0 {
                var soerr: Int32 = 0
                var len = socklen_t(MemoryLayout<Int32>.size)
                if getsockopt(sock, SOL_SOCKET, SO_ERROR, &soerr, &len) == 0 && soerr == 0 {
                    connected = true
                } else {
                    AppLog.add("connect refused errno=\(soerr)")
                }
            } else {
                AppLog.add("connect timed out (pr=\(pr))")
            }
        } else {
            AppLog.add("connect() failed errno=\(errno)")
        }

        guard connected else {
            close(sock)
            emit(.idle, "Connection failed: \(host) unreachable")
            scheduleRetryLocked()
            return
        }

        _ = fcntl(sock, F_SETFL, 0)
        var tv = timeval(tv_sec: 1, tv_usec: 0)
        setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(sock, SOL_SOCKET, SO_KEEPALIVE, &on, socklen_t(MemoryLayout<Int32>.size))

        fd = sock
        buffer.removeAll()
        AppLog.add("tcp connected")
        emit(.connected, "Connected")
        sendRawLocked(#"{"type":"hello","proto":1,"client":"MCVibe iOS"}"# + "\n")
        recvStep(sock: sock, myGen: gen)
    }

    private func recvStep(sock: Int32, myGen: Int) {
        queue.async {
            guard self.wantsConnection, self.gen == myGen, self.fd == sock else { return }
            var buf = [UInt8](repeating: 0, count: 4096)
            let n = recv(sock, &buf, buf.count, 0)
            if n > 0 {
                self.buffer.append(contentsOf: buf.prefix(Int(n)))
                self.drainLines()
            } else if n == 0 {
                AppLog.add("peer closed the connection")
                self.emit(.idle, "Disconnected")
                self.teardown()
                self.scheduleRetryLocked()
                return
            } else {
                let e = errno
                if e != EAGAIN && e != EWOULDBLOCK && e != EINTR {
                    AppLog.add("recv failed errno=\(e)")
                    self.emit(.idle, "Disconnected")
                    self.teardown()
                    self.scheduleRetryLocked()
                    return
                }
                // Receive timeout tick: fall through, check state, keep waiting.
            }
            self.recvStep(sock: sock, myGen: myGen)
        }
    }

    private func scheduleRetryLocked() {
        guard wantsConnection else { return }
        let myGen = gen
        queue.asyncAfter(deadline: .now() + 2.0) {
            guard self.wantsConnection, self.gen == myGen, self.fd < 0 else { return }
            AppLog.add("retrying connection")
            self.startLocked()
        }
    }

    private func teardown() {
        if fd >= 0 {
            close(fd)
            fd = -1
        }
        buffer.removeAll()
    }

    private func sendRawLocked(_ s: String) {
        guard fd >= 0 else { return }
        let bytes = Array(s.utf8)
        let n = bytes.withUnsafeBytes { raw in
            send(fd, raw.baseAddress, raw.count, 0)
        }
        if n < 0 {
            AppLog.add("send failed errno=\(errno)")
        }
    }

    private func drainLines() {
        while let idx = buffer.firstIndex(of: 0x0A) {
            let lineData = buffer.prefix(upTo: idx)
            buffer.removeFirst(idx + 1)
            guard !lineData.isEmpty else { continue }
            guard let msg = try? JSONDecoder().decode(VibeMessage.self, from: Data(lineData)) else {
                AppLog.add("bad json from PC")
                continue
            }
            onMessage?(msg)
        }
    }

    private func emit(_ p: Phase, _ detail: String) {
        onPhase?(p, detail)
    }

    private func resolveIPv4(_ host: String) -> in_addr? {
        var a = in_addr()
        if inet_pton(AF_INET, host, &a) == 1 {
            return a
        }
        var hints = addrinfo()
        hints.ai_family = AF_INET
        hints.ai_socktype = SOCK_STREAM
        var res: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, nil, &hints, &res) == 0, let first = res else { return nil }
        defer { freeaddrinfo(res) }
        guard let sap = first.pointee.ai_addr, first.pointee.ai_family == AF_INET else { return nil }
        return sap.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr }
    }
}
