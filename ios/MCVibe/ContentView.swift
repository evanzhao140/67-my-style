import SwiftUI

struct ContentView: View {
    @EnvironmentObject var model: AppModel
    @State private var showLog = false
    @State private var showLoops = false
    /// 0 = locked screen; 1...3 = unlock confirmation steps.
    @State private var unlockStep = 0

    var body: some View {
        VStack(spacing: 16) {
            statusHeader

            if model.locked {
                lockedFlow
                Spacer(minLength: 0)
            } else {
                ScrollView(showsIndicators: false) {
                    VStack(spacing: 16) {
                        connectSection
                        loopsSection
                        lockButton
                    }
                    .padding(.vertical, 2)
                }
                footer
            }
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
                                Text(host.mc.isEmpty ? host.host : "\(host.host) • MC \(host.mc)")
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

    /// Phone-only looping textures: they work with or without a PC connection.
    @ViewBuilder
    private var loopsSection: some View {
        HStack(spacing: 8) {
            Button {
                showLoops = true
            } label: {
                HStack(spacing: 12) {
                    Image(systemName: model.activeLoop?.systemImage ?? "waveform.path")
                        .font(.title3)
                        .frame(width: 28)
                        .foregroundStyle(model.activeLoop == nil ? Color.secondary : Color.purple)

                    VStack(alignment: .leading, spacing: 2) {
                        Text(model.activeLoop.map { "\($0.name) loop" } ?? "Haptic loops")
                            .font(.subheadline.weight(.semibold))
                        Text(model.activeLoop == nil
                             ? "Cool patterns that loop on the phone"
                             : "Playing · tap to change")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }

                    Spacer(minLength: 6)

                    Image(systemName: "chevron.right")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
                .padding(12)
                .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 14))
                .contentShape(RoundedRectangle(cornerRadius: 14))
            }
            .buttonStyle(.plain)

            if model.activeLoop != nil {
                Button {
                    model.stopLoop()
                } label: {
                    Image(systemName: "stop.fill")
                        .font(.body.weight(.bold))
                        .frame(width: 44, height: 44)
                        .background(Color.red.opacity(0.25), in: Circle())
                }
                .buttonStyle(.plain)
                .foregroundStyle(.red)
                .accessibilityLabel("Stop haptic loop")
            }
        }
        .sheet(isPresented: $showLoops) { LoopSheet() }
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

    /// Locked mode replaces everything else on screen, so the footer buttons
    /// (test / mute / log) cannot be tapped while locked or while unlocking.
    /// Unlocking takes three separate confirmations, so it can never happen
    /// from a single pocket tap.
    @ViewBuilder
    private var lockedFlow: some View {
        if unlockStep == 0 {
            lockedPanel
        } else {
            unlockConfirm(step: unlockStep)
        }
    }

    private var lockedPanel: some View {
        Button {
            unlockStep = 1
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

    private func unlockConfirm(step: Int) -> some View {
        let titles = ["Unlock controls?", "Are you sure?", "Really unlock now?"]
        let details = [
            "The connect and lock buttons will become tappable again.",
            "Unlocking takes three confirmations to prevent accidental taps.",
            "This is the last confirmation; the lock turns off right after."
        ]
        let index = min(max(step, 1), 3) - 1
        return VStack(spacing: 18) {
            Image(systemName: "lock.open.fill")
                .font(.system(size: 40))
                .foregroundStyle(.orange)
            Text("Unlock \(step) of 3")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(titles[index])
                .font(.title2.bold())
                .multilineTextAlignment(.center)
            Text(details[index])
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            if step < 3 {
                Button("Continue") { unlockStep = step + 1 }
                    .buttonStyle(.borderedProminent)
            } else {
                Button("Unlock controls") {
                    unlockStep = 0
                    model.toggleLock()
                }
                .buttonStyle(.borderedProminent)
                .tint(.red)
            }
            Button("Cancel") { unlockStep = 0 }
        }
        .frame(maxWidth: .infinity, minHeight: 240)
        .padding(16)
        .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 16))
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
