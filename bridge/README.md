# Plant Bridge (Windows)

Connects the local Plant API service to your iPhone over **Bluetooth** — no Wi-Fi,
no same-network requirement.

```
Plant API service (TCP 127.0.0.1:25666) -> PlantBridge (BLE GATT server) -> iPhone (Bluetooth mode)
```

## Usage (in order)

1. Start the game with the Plant API mod loaded.
2. Double-click **`run-bridge.bat`** (a console window opens — keep it open).
3. On the phone: MCVibe app → **Bluetooth** tab → **Connect over Bluetooth**.

The phone connects automatically and retries on its own if the connection drops.

## First-time build (only if `publish\PlantBridge.exe` is missing)

```
dotnet publish -c Release -o publish
```

Requires the .NET SDK (already installed on this PC: .NET 10).
