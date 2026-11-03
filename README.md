# RL Offline Launcher

**Unofficial** Windows desktop utility for Rocket League.  
Not affiliated with, endorsed by, or connected to Psyonix or Epic Games.

## Disclaimers (read these)

- This tool **never** bypasses, disables, patches, hooks, injects into, or tampers with Easy Anti-Cheat or any other security mechanism.
- It **never** modifies online inventory, forges entitlements, or contacts game servers for cosmetic/ownership data.
- Cosmetic selection in the UI **does not** grant ownership. All cosmetic state is **temporary, local, in-memory only** and is cleared on reset, game exit, or application exit.
- The flags `-offline` and `-noanticheat` are passed exactly as requested. **Their effect on the current Rocket League version is unverified.** The official documented way to launch without EAC is the launcher option / `-noeac`.
- The cosmetic system ships as **Preview only**. There is no supported public API for applying cosmetics inside the game without injection; therefore none is implemented.

## Features & status

| Feature | Status |
|---------|--------|
| Path selection + basic validation | Implemented |
| EAC process-name heuristic scan | Implemented (blocks launch on Detected / Inconclusive) |
| Offline launch with requested flags | Implemented (direct .exe only; effect unverified) |
| Dark-themed WPF UI | Implemented |
| Cosmetic catalog + search / filter | Implemented (placeholder items) |
| Local preview apply / reset | Implemented (in-memory only) |
| F3 + T hotkey chord (configurable) | Implemented (low-level keyboard hook) |
| External overlay window | Scaffold only (preview UI is in-app) |
| Real in-game cosmetic application | **Not available** |
| Anti-cheat circumvention | **Never implemented** |

## Requirements

- Windows 10 / 11 (x64)
- For building: .NET 8 SDK

## Local build

```powershell
dotnet restore RLOfflineLauncher.sln
dotnet build RLOfflineLauncher.sln -c Release
dotnet test RLOfflineLauncher.sln -c Release
dotnet publish src/RLOfflineLauncher/RLOfflineLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

The published executable is `./publish/RLOfflineLauncher.exe`.

## GitHub Actions

1. Create a new GitHub repository.
2. Push this project to it.
3. Actions → the “Build & Publish” workflow runs on every push / PR.
4. Download the artifact **RLOfflineLauncher-win-x64** from a successful run.
5. Unzip and run `RLOfflineLauncher.exe`. (Windows SmartScreen may warn about an unsigned binary – this is expected for community builds.)

## Usage

1. **Launcher** tab → Browse to `RocketLeague.exe` (or the binary you use).
2. Click **Scan now**. Launch is blocked if EAC processes are detected or the scan is inconclusive.
3. If EAC is detected you will see the exact message  
   `Please launch Rocket League without Easy Anti-Cheat`  
   and be offered help (opens https://youtube.com). The cosmetic subsystem is **not** initialized.
4. When the scan is clear, click **Launch with selected flags**. The exact command line is shown for transparency.
5. **Cosmetics** tab → browse placeholder items, apply local previews. Status always reports that no game integration is active.
6. **Settings** → toggle the two flags, change the hotkey chord, reset settings.
7. **Logs** → live log + diagnostic summary; copy / export.

Hotkey default is **F3 + T** (both keys held). It only toggles when the integration layer has been legitimately initialized (or when “allow while focused” is enabled for testing). The hook never logs other keystrokes.

## Known limitations

- `-offline` / `-noanticheat` effect is **unverified**. Prefer the official launcher “Play without Easy Anti-Cheat” / `-noeac` when you need a guaranteed no-EAC session.
- No real in-game cosmetic or rendering integration exists; the applier is preview-only by design.
- Overlay is an external topmost WPF window; exclusive fullscreen may hide it.
- EAC detection is a **heuristic** based on process names; the list must be kept up to date.
- Unsigned binary → SmartScreen warning.

## Privacy

- No telemetry.
- No network calls except the optional help link the user explicitly confirms.
- Keyboard hook evaluates only the configured chord; no keylogging.
- Settings stored under `%LocalAppData%\RLOfflineLauncher\settings.json`. Cosmetic selections are never persisted as ownership.

## License

MIT – see [LICENSE](LICENSE).
