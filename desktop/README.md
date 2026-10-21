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

## osu! sync (lazer)

Plant Pulse can turn **osu!lazer** gameplay into haptics. The game stays
untouched: lazer has no third-party mod API, so Plant Pulse reads the live play
state through **tosu** (the standard open-source memory reader) over its local
websocket and streams hits to the phone.

### One-time setup

1. Install [tosu](https://tosu.app) — portable `tosu.exe`, or
   `winget install tosuapp.tosu`. A portable copy is already bundled in
   `desktop\publish\tosu\tosu.exe` on this PC; **Start tosu** launches it.
2. Start tosu before you play; it listens on `127.0.0.1:24050`.
3. In Plant Pulse, click **osu! sync** (top right). Sync is on by default.
4. Connect the phone as usual — Wi-Fi, or Bluetooth via **Start bridge**.
5. Play lazer.

Every hit is a short tick, every 4th/8th/16th hit is accented, combo adds a
little energy, and misses get a low thud. **Hit intensity**, **Accent**,
**Miss buzz** and **Start ping** are adjustable, and the current beatmap,
combo, hits and misses are shown live.

Notes:

- tosu's precise feed polls every 10 ms by default; that is what keeps the taps
  tight. If you edited `PRECISE_DATA_POLL_RATE`, keep it at 10 ms or lower.
- osu!catch has no hit-error stream, so it automatically falls back to the
  score counters (still synced, just slightly coarser).
- Nothing is injected or patched in lazer, and no phone-app change is needed.
- Use the osu! sync window while playing; the main pattern screen and osu! sync
  share the same phone link, so don't run both at once.
- Close Minecraft while using Plant Pulse (port 25666).

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
