import SwiftUI

struct ContentView: View {
    @EnvironmentObject var model: AppModel
    @State private var showLog = false

    var body: some View {
        VStack(spacing: 16) {
            statusHeader

            if model.locked {
                lockedPanel
            } else {
                connectSection
                lockButton
            }

            Spacer(minLength: 0)
            footer
        }
        .padding(20)
        .background(Color.black.ignoresSafeArea())
        .preferredColorScheme(.dark)
        .sheet(isPresented: $showLog) { LogView() }
    }

    private var statusHeader: some View {
        VStack(spacing: 8) {
            Text("MCVibe")
                .font(.largeTitle.bold())

            Text(model.statusText)
                .font(.footnote.weight(.semibold))
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(statusColor.opacity(0.25), in: Capsule())
                .foregroundStyle(statusColor)
        }
    }

    private var statusColor: Color {
        switch model.phase {
        case .connected: return .green
        case .connecting: return .orange
        case .idle: return .gray
        }
    }

    @ViewBuilder
    private var connectSection: some View {
        if model.phase == .connected {
            Button("Disconnect") { model.disconnect() }
                .buttonStyle(.bordered)
        } else {
            VStack(spacing: 12) {
                Picker("Connection", selection: $model.transport) {
                    Text("Wi‑Fi").tag(AppModel.Transport.wifi)
                    Text("Bluetooth").tag(AppModel.Transport.bluetooth)
                }
                .pickerStyle(.segmented)

                if model.transport == .bluetooth {
                    bluetoothConnect
                } else if model.hosts.isEmpty {
                    HStack(spacing: 8) {
                        ProgressView().controlSize(.small)
                        Text("Looking for your PC on Wi‑Fi…")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                    Text("Start Minecraft with the Plant API mod, and keep your phone and PC on the same Wi‑Fi.")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                        .multilineTextAlignment(.center)
                } else {
                    ForEach(model.hosts) { host in
                        Button {
                            model.connect(host: host.host, port: host.port)
                        } label: {
                            HStack {
                                Text("🖥️ \(host.name)")
                                Spacer()
                                Text("\(host.host) • MC \(host.mc)")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                        }
                        .buttonStyle(.borderedProminent)
                    }
                }

                if model.transport == .wifi {
                    HStack(spacing: 8) {
                        TextField("or type PC IP, e.g. 192.168.1.20", text: $model.manualInput)
                            .textFieldStyle(.roundedBorder)
                            .keyboardType(.numbersAndPunctuation)
                            .autocorrectionDisabled()
                            .textInputAutocapitalization(.never)
                        Button("Connect") { model.connectManual() }
                            .buttonStyle(.bordered)
                            .disabled(model.manualInput.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }
            }
        }
    }

    /// Bluetooth mode: the phone scans for the Plant Bridge app on the PC.
    @ViewBuilder
    private var bluetoothConnect: some View {
        VStack(spacing: 10) {
            Text("📶 📱")
                .font(.largeTitle)
            Text("1. Start Minecraft with the mod\n2. Run the Plant Bridge on your PC\n3. Tap the button below")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            Button("Connect over Bluetooth") { model.connectBluetooth() }
                .buttonStyle(.borderedProminent)
        }
        .padding(.vertical, 6)
    }

    private var lockButton: some View {
        Button {
            model.toggleLock()
        } label: {
            VStack(spacing: 6) {
                Image(systemName: "lock.open.fill")
                    .font(.system(size: 32))
                Text("Lock controls")
                    .font(.headline)
                Text("Prevents accidental taps while playing")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .frame(maxWidth: .infinity, minHeight: 110)
        }
        .buttonStyle(.borderedProminent)
        .tint(.blue)
    }

    private var lockedPanel: some View {
        Button {
            model.toggleLock()
        } label: {
            VStack(spacing: 10) {
                Image(systemName: "lock.fill")
                    .font(.system(size: 44))
                Text("LOCKED")
                    .font(.system(size: 30, weight: .heavy, design: .rounded))
                Text("Tap to unlock")
                    .font(.footnote)
                    .opacity(0.8)
            }
            .frame(maxWidth: .infinity, minHeight: 240)
        }
        .buttonStyle(.borderedProminent)
        .tint(.red)
    }

    @ViewBuilder
    private var footer: some View {
        VStack(spacing: 6) {
            HStack(spacing: 12) {
                Button("Test vibration") { model.testVibration() }
                    .font(.caption)
                    .buttonStyle(.bordered)
                Toggle(isOn: $model.hapticsMuted) {
                    Text("Mute")
                        .font(.caption)
                }
                .tint(.gray)
                Spacer(minLength: 8)
                Button("Log") { showLog = true }
                    .font(.caption)
            }
            .padding(.bottom, 2)

            if !model.lastEvent.isEmpty {
                Text(model.lastEvent)
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
            }
            Text("Keep this app in the foreground while playing — iOS pauses apps in the background, which stops the vibrations.")
                .font(.caption2)
                .foregroundStyle(.tertiary)
                .multilineTextAlignment(.center)
        }
    }
}

/// Shows the persisted connection log (also survives crashes/kills).
private struct LogView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var text = AppLog.shared.text()

    var body: some View {
        NavigationStack {
            ScrollView {
                Text(text)
                    .font(.caption2.monospaced())
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding()
            }
            .navigationTitle("Connection log")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Clear") {
                        AppLog.shared.clear()
                        text = AppLog.shared.text()
                    }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Done") { dismiss() }
                }
            }
        }
    }
}
