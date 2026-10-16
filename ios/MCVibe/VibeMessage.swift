import Foundation

/// One newline-delimited JSON message from the Plant API mod.
struct VibeMessage: Decodable {
    let type: String
    let id: String?
    let intensity: Double?   // 0...100
    let durationMs: Int?
    let pulses: Int?
    let name: String?        // hello: PC name
    let mc: String?          // hello: Minecraft version
    let proto: Int?
    let active: [String: Double]?   // constants: full active-constant map
}
