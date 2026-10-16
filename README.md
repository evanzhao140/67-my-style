# Plant API + MCVibe

Two pieces that work together:

- **`mod/`** — a small Fabric mod called **Plant API** that runs a local helper service and turns Minecraft moments into vibration events.
- **`ios/`** — **MCVibe**, a tiny iPhone app that connects over Wi-Fi or Bluetooth and vibrates on those events.

Your phone buzzes on ~65 different events (taking damage, breaking ore, creeper fusing, advancements, boss kills...) and hums constantly while conditions are active (status effects, low health, low air, darkness...). Every event has an intensity slider from 0 to 100 — **0 disables it**.

No Mac is needed: the IPA is built by GitHub Actions and sideloaded from Windows.

---

## 1. Install the mod (PC)

You need Minecraft **Java 26.2** with Fabric.

1. Install the [Fabric loader](https://fabricmc.net/use/installer/) for 26.2.
2. Download and drop these into `.minecraft/mods/`:
   - [Fabric API](https://modrinth.com/mod/fabric-api) for 26.2
   - [Mod Menu](https://modrinth.com/mod/modmenu) (for the config screen)
   - [Cloth Config](https://modrinth.com/mod/cloth-config) (config screen library)
   - **`plantapi-1.0.0.jar`** (this repo — grab it from the GitHub Actions artifact or `mod/build/libs/`)
3. Start the game. In the mod list you'll see **Plant API** ("Small generic helper API...") — that's it.
4. Open **Mod Menu → Plant API → Config** to tune everything:
   - **Connection** — ports, burst rate limit, master intensity, single-player mode.
   - **Bursts** — 65 one-shot vibrations (intensity / duration / pulse count).
   - **Status Effects** — 38 per-effect constants.
   - **Conditions** — health, hunger, air, fire, lava, freezing, darkness, hostiles, raids, boss proximity...
   - **Test** — sends a test buzz, or stops any stuck constant vibration.

The same values live in `config/plantapi.json` if you prefer editing text.

## 2. Build the iPhone app (no Mac needed)

1. Push this repo to GitHub (public or private, both work — Actions minutes are free for public repos).
2. Go to **Actions → build → Run workflow** (it also runs automatically on push).
3. When both jobs finish, open the workflow run and download the **plantapi-ipa** artifact.
4. Sideload it from Windows with [Sideloadly](https://sideloadly.io/) (or AltStore):
   - Connect the iPhone (or use Wi-Fi sideloading), drag `plantapi-unsigned.ipa` in, enter your free Apple ID, start.
   - Trust the developer profile on the phone: **Settings → General → VPN & Device Management**.
   - Note: with a free Apple ID the app expires after **7 days**; just sideload again.

## 3. Pair and play

1. Phone and PC on the **same Wi-Fi/network** (or skip Wi-Fi and use Bluetooth below).
2. Open MCVibe — your PC appears in the list (the mod answers UDP discovery on port 25667). If it doesn't, type the PC's IP manually.
3. Tap the PC, then tap **Lock**. The app stays awake while connected.
4. Play. Vibration happens only while the app is in the foreground.

**Bluetooth (no same network needed):** run `bridge\run-bridge.bat` on the PC (see `bridge/README.md`), then in the app open the **Bluetooth** tab and tap **Connect over Bluetooth**.

**Firewall:** the first time you launch Minecraft after installing the mod, Windows will ask — allow `java.exe` on **Private networks** (or manually allow inbound **TCP 25666** and **UDP 25667**). Phone hotspots / networks with client isolation can block discovery; the manual IP entry still works if you can ping the PC.

---

## Repo layout

```
mod/                  Fabric mod (Plant API)
  src/main/java/com/plantapi/...
ios/                  iOS app (MCVibe)
  project.yml         XcodeGen project (CI generates the .xcodeproj)
bridge/               Windows helper (Plant Bridge) -> relays events to the phone over Bluetooth
.github/workflows/    build.yml -> mod jar + unsigned plantapi-unsigned.ipa artifacts
docs/ACTIONS.md       full list of every vibration id and its defaults
```

## Build locally (optional)

```powershell
cd mod
.\gradlew.bat build        # -> mod/build/libs/plantapi-1.0.0.jar
```

The iOS app can only be compiled on macOS; use the Actions workflow otherwise.
