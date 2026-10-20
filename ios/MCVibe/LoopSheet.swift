import SwiftUI

/// Sheet for auditioning the phone-only looping haptic textures.
struct LoopSheet: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 14) {
                    ForEach(HapticLoop.allCases) { loop in
                        Button {
                            model.toggleLoop(loop)
                        } label: {
                            row(loop)
                        }
                        .buttonStyle(.plain)
                    }

                    intensityCard

                    Text("Loops run entirely on this phone — no PC or game needed. Keep MCVibe in the foreground while they play.")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                        .multilineTextAlignment(.center)
                        .padding(.top, 2)
                }
                .padding(20)
            }
            .background(Color.black.ignoresSafeArea())
            .navigationTitle("Haptic loops")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Stop") { model.stopLoop() }
                        .disabled(model.activeLoop == nil)
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Done") { dismiss() }
                }
            }
        }
        .preferredColorScheme(.dark)
    }

    private func row(_ loop: HapticLoop) -> some View {
        let active = model.activeLoop == loop
        return HStack(spacing: 14) {
            ZStack {
                Circle()
                    .fill(active ? Color.purple.opacity(0.35) : Color.white.opacity(0.08))
                    .frame(width: 44, height: 44)
                Image(systemName: loop.systemImage)
                    .font(.system(size: 19, weight: .semibold))
                    .foregroundStyle(active ? Color.purple : Color.white.opacity(0.85))
            }

            VStack(alignment: .leading, spacing: 3) {
                Text(loop.name)
                    .font(.headline)
                Text(loop.subtitle)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.leading)
            }

            Spacer(minLength: 6)

            Image(systemName: active ? "stop.circle.fill" : "play.circle.fill")
                .font(.system(size: 26))
                .foregroundStyle(active ? Color.red : Color.cyan)
        }
        .padding(14)
        .background(active ? Color.purple.opacity(0.18) : Color.white.opacity(0.06),
                    in: RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(active ? Color.purple.opacity(0.5) : Color.clear, lineWidth: 1)
        )
        .contentShape(RoundedRectangle(cornerRadius: 16))
    }

    private var intensityCard: some View {
        VStack(spacing: 10) {
            HStack {
                Text("Intensity")
                    .font(.subheadline.weight(.semibold))
                Spacer()
                Text("\(Int(model.loopLevel))%")
                    .font(.subheadline.monospacedDigit())
                    .foregroundStyle(.secondary)
            }

            Slider(value: $model.loopLevel, in: 10...100, step: 5, onEditingChanged: { editing in
                if !editing { model.applyLoopLevel() }
            })
            .tint(.purple)
        }
        .padding(14)
        .background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 16))
    }
}
