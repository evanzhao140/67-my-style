import Foundation

/// Small persisted log, written from any thread. If the app ever crashes or
/// is killed, the last lines survive and can be viewed inside the app
/// (Log button) so the exact step that was running is known.
final class AppLog {
    static let shared = AppLog()

    private let lock = NSLock()
    private var lines: [String] = []
    private let key = "debugLog"

    private static let fmt: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f
    }()

    private init() {
        lines = UserDefaults.standard.stringArray(forKey: key) ?? []
    }

    func add(_ s: String) {
        lock.lock()
        lines.append(Self.fmt.string(from: Date()) + "  " + s)
        if lines.count > 250 {
            lines.removeFirst(lines.count - 250)
        }
        UserDefaults.standard.set(lines, forKey: key)
        lock.unlock()
    }

    func text() -> String {
        lock.lock()
        defer { lock.unlock() }
        return lines.joined(separator: "\n")
    }

    func clear() {
        lock.lock()
        lines.removeAll()
        UserDefaults.standard.set(lines, forKey: key)
        lock.unlock()
    }
}
