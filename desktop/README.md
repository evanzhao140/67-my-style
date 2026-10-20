# Plant Pulse — haptic pattern transmitter

A small Windows app that streams cool, looping haptic textures to the phone.
It is completely separate from the game: no mod required. The phone app stays
exactly as it is — no new IPA, no re-sideload.

## Quick start (Bluetooth)

1. Run `run-plantpulse.bat` (or the **Plant Pulse** desktop shortcut).
2. Click **Start Bluetooth bridge** (this launches Plant Bridge).
3. On the phone: open MCVibe → **Bluetooth** → **Connect over Bluetooth**.
4. Pick a texture in Plant Pulse. It starts immediately.

The bridge relays the local TCP stream (127.0.0.1:25666) to BLE, so the phone
needs no Wi-Fi network access.

## Quick start (Wi-Fi)

1. Run Plant Pulse.
2. On the phone: MCVibe → **Wi‑Fi** → tap **Plant Pulse** in the discovered list.

No bridge and no Bluetooth needed; both sides just need to be on the same
network. The app also accepts a typed IP.

## Textures

| Texture | What it does |
|---|---|
| **Heartbeat** | lub-dub plus a quiet clock tick, tempo-synced |
| **Groove** | kick, snare, hats and ticks on a 16-step bar |
| **Warp** | taps accelerate and rise into a three-hit drop |
| **Rain** | scattered soft droplets, never the same twice |
| **Purr** | continuous motor rumble with a 6 Hz wobble |
| **Riser** | six-second swell, a beat of silence, then the drop |

**Tempo** (40–180 BPM) drives Heartbeat, Groove and Warp. **Intensity**
(10–100%) scales everything. Click a texture again (or the big Play/Stop bar)
to stop it.

## Notes

- Close Minecraft while using Plant Pulse: the mod and this app both own
  TCP 25666, so only one can run at a time. The status line says when the port
  is busy.
- Settings are remembered in `%APPDATA%\PlantPulse\settings.json`; the log is
  `%APPDATA%\PlantPulse\log.txt`.
- The bridge is a separate, already-built app in `..\bridge\publish`. If it is
  missing, build it once with `cd bridge && dotnet publish -c Release -o publish`.
- Build Plant Pulse with `dotnet publish -c Release -o publish`.
