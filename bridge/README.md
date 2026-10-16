# MCVibe Bridge (Windows)

Connects the Minecraft mod to your iPhone over **Bluetooth** — no Wi-Fi, no
same-network requirement.

```
Minecraft mod (TCP 127.0.0.1:25666) -> MCVibeBridge (BLE GATT server) -> iPhone (Bluetooth mode)
```

## Usage (in order)

1. Start Minecraft with the Plant API mod loaded.
2. Double-click **`run-bridge.bat`** (a console window opens — keep it open).
3. On the phone: MCVibe app → **Bluetooth** tab → **Connect over Bluetooth**.

The phone connects automatically and retries on its own if the connection drops.

## First-time build (only if `publish\MCVibeBridge.exe` is missing)

```
dotnet publish -c Release -o publish
```

Requires the .NET SDK (already installed on this PC: .NET 10).
