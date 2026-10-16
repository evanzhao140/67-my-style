import Foundation
import CoreBluetooth

/// Connects to the PC over Bluetooth Low Energy. The MCVibeBridge app running
/// on the PC advertises a GATT service and streams game events as
/// notifications, chunked into 20-byte frames and reassembled here.
///
/// Same interface as LinkClient: `onPhase` / `onMessage` are called on `queue`.
final class BluetoothLink: NSObject {
    typealias Phase = LinkClient.Phase

    var onPhase: ((Phase, String) -> Void)?
    var onMessage: ((VibeMessage) -> Void)?

    private static let serviceUUID = CBUUID(string: "6D637669-6265-4D43-5649-424500000001")
    private static let eventUUID = CBUUID(string: "6D637669-6265-4D43-5649-424500000002")

    private let queue = DispatchQueue(label: "mcvibe.ble")
    private var central: CBCentralManager?
    private var target: CBPeripheral?
    private var wantsConnection = false
    private var connected = false
    private var gen = 0
    private var rx: [UInt8] = []
    private var connectTimeout: DispatchWorkItem?

    // MARK: - Public API (mirrors LinkClient)

    func connect(host: String, port: UInt16) {
        queue.async {
            self.wantsConnection = true
            self.gen += 1
            AppLog.add("bluetooth connect requested")
            self.startLocked()
        }
    }

    func disconnect() {
        queue.async {
            self.wantsConnection = false
            self.gen += 1
            self.cleanupLocked()
            self.emit(.idle, "Not connected")
        }
    }

    // MARK: - Queue-confined

    private func startLocked() {
        guard wantsConnection else { return }
        if central == nil {
            // Created on first use so the Bluetooth permission prompt only
            // appears when the user actually chooses Bluetooth.
            central = CBCentralManager(
                delegate: self,
                queue: queue,
                options: [CBCentralManagerOptionShowPowerAlertKey: false]
            )
            emit(.connecting, "Starting Bluetooth…")
            return // wait for centralManagerDidUpdateState
        }
        guard let central else { return }
        switch central.state {
        case .poweredOn:
            if connected { return }
            if let p = target, p.state == .connected || p.state == .connecting { return }
            emit(.connecting, "Scanning for your PC…")
            central.scanForPeripherals(
                withServices: [Self.serviceUUID],
                options: [CBCentralManagerScanOptionAllowDuplicatesKey: false]
            )
        case .poweredOff:
            emit(.idle, "Bluetooth is off — turn it on to connect")
        case .unauthorized:
            emit(.idle, "Bluetooth permission denied (Settings → MCVibe)")
        case .unsupported:
            emit(.idle, "Bluetooth is not supported on this device")
        default:
            emit(.connecting, "Waiting for Bluetooth…")
        }
    }

    private func cleanupLocked() {
        connectTimeout?.cancel()
        connectTimeout = nil
        central?.stopScan()
        if let p = target {
            p.delegate = nil
            central?.cancelPeripheralConnection(p)
        }
        target = nil
        connected = false
        rx.removeAll()
    }

    private func scheduleRetryLocked() {
        guard wantsConnection else { return }
        let myGen = gen
        queue.asyncAfter(deadline: .now() + 2.0) {
            guard self.wantsConnection, self.gen == myGen else { return }
            AppLog.add("bluetooth retry")
            self.startLocked()
        }
    }

    private func armConnectTimeout(for p: CBPeripheral) {
        connectTimeout?.cancel()
        let myGen = gen
        let item = DispatchWorkItem { [weak self] in
            guard let self, self.wantsConnection, self.gen == myGen, !self.connected else { return }
            AppLog.add("bluetooth connect timeout")
            if let t = self.target {
                self.central?.cancelPeripheralConnection(t)
            }
            self.target = nil
            self.scheduleRetryLocked()
        }
        connectTimeout = item
        queue.asyncAfter(deadline: .now() + 10, execute: item)
    }

    private func handleChunk(_ bytes: [UInt8]) {
        guard let header = bytes.first else { return }
        let index = Int(header >> 1)
        let isLast = (header & 1) == 1
        if index == 0 {
            rx.removeAll()
        }
        rx.append(contentsOf: bytes.dropFirst())
        guard isLast else { return }
        let snapshot = rx
        rx.removeAll()
        guard !snapshot.isEmpty else { return }
        guard let msg = try? JSONDecoder().decode(VibeMessage.self, from: Data(snapshot)) else {
            AppLog.add("bluetooth bad json")
            return
        }
        onMessage?(msg)
    }

    private func emit(_ p: Phase, _ detail: String) {
        onPhase?(p, detail)
    }
}

// MARK: - CBCentralManagerDelegate

extension BluetoothLink: CBCentralManagerDelegate {
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        AppLog.add("bluetooth state \(central.state.rawValue)")
        guard wantsConnection else { return }
        startLocked()
    }

    func centralManager(
        _ central: CBCentralManager,
        didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any],
        rssi RSSI: NSNumber
    ) {
        guard wantsConnection, !connected, target == nil else { return }
        AppLog.add("bluetooth found peripheral")
        central.stopScan()
        target = peripheral
        peripheral.delegate = self
        emit(.connecting, "Found your PC — connecting…")
        peripheral.connect()
        armConnectTimeout(for: peripheral)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        connectTimeout?.cancel()
        AppLog.add("bluetooth peripheral connected")
        emit(.connecting, "Setting up…")
        peripheral.discoverServices([Self.serviceUUID])
    }

    func centralManager(
        _ central: CBCentralManager,
        didFailToConnect peripheral: CBPeripheral,
        error: Error?
    ) {
        connectTimeout?.cancel()
        AppLog.add("bluetooth connect failed")
        target = nil
        emit(.idle, "Connection failed")
        scheduleRetryLocked()
    }

    func centralManager(
        _ central: CBCentralManager,
        didDisconnectPeripheral peripheral: CBPeripheral,
        error: Error?
    ) {
        connectTimeout?.cancel()
        connected = false
        rx.removeAll()
        if target === peripheral { target = nil }
        guard wantsConnection else { return } // user asked to disconnect: silent
        AppLog.add("bluetooth disconnected")
        emit(.idle, "Disconnected")
        scheduleRetryLocked()
    }
}

// MARK: - CBPeripheralDelegate

extension BluetoothLink: CBPeripheralDelegate {
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard wantsConnection else { return }
        guard error == nil,
              let svc = peripheral.services?.first(where: { $0.uuid == Self.serviceUUID })
        else {
            AppLog.add("bluetooth service missing")
            central?.cancelPeripheralConnection(peripheral)
            return
        }
        peripheral.discoverCharacteristics([Self.eventUUID], for: svc)
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didDiscoverCharacteristicsFor service: CBService,
        error: Error?
    ) {
        guard wantsConnection else { return }
        guard error == nil,
              let ch = service.characteristics?.first(where: { $0.uuid == Self.eventUUID })
        else {
            AppLog.add("bluetooth characteristic missing")
            central?.cancelPeripheralConnection(peripheral)
            return
        }
        peripheral.setNotifyValue(true, for: ch)
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didUpdateNotificationStateFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        guard wantsConnection else { return }
        if error == nil && characteristic.isNotifying {
            connected = true
            AppLog.add("bluetooth notifications on")
            emit(.connected, "Connected over Bluetooth")
        } else {
            AppLog.add("bluetooth notify setup failed")
            central?.cancelPeripheralConnection(peripheral)
        }
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didUpdateValueFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        guard error == nil, let data = characteristic.value, !data.isEmpty else { return }
        handleChunk([UInt8](data))
    }
}
