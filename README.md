#  <img width="32" height="32" alt="logo" src="https://github.com/user-attachments/assets/1299b402-4e81-48fd-a89d-7663a3d19030" /> NVibrance

NVibrance is a small Windows app (WPF) that applies per-application NVIDIA digital vibrance profiles. It runs in the tray, monitors the foreground application and applies a saved vibrance value for matching executables.

<img width="1349" height="901" alt="image" src="https://github.com/user-attachments/assets/751522db-b394-476c-a7ce-1f93d13e7e66" />

## Features
- Per-application vibrance profiles
- Single-file publish support (framework-dependent or self-contained)
- Tray icon with autostart toggle
- Profiles persisted to `%APPDATA%\NVibrance\profiles.json`
- Simple UI for adding, renaming and deleting profiles

## Usage
- Launch app (or let it start on login if autostart enabled).
- Add a profile by selecting a running process or browsing an executable.
- Set profile vibrance and it will be applied automatically when that executable gains focus.
- Tray menu: Open UI, toggle "Start with Windows", or Exit.

### Command-line switches
| Switch | Effect |
| --- | --- |
| `--minimized` | Starts hidden in the tray (used by the autostart entry). |
| `--verbose` | Writes detailed detection diagnostics to the log file. Off by default. |

<img width="229" height="130" alt="image" src="https://github.com/user-attachments/assets/b31343be-8092-4692-9736-71303c6f8c51" />

## Requirements
- Windows (desktop)
- .NET 10 runtime for framework-dependent builds, or none for self-contained builds
- NVIDIA drivers and NvAPI available (uses `NvAPIWrapper.Net`)

## Quick start (development)
1. Clone the repo.
2. Restore and build:
   - `dotnet restore NVibrance.slnx`
   - `dotnet build NVibrance.slnx -c Release --no-restore`
3. Run from IDE (Rider) or run `dotnet run --project NVibrance/NVibrance.csproj` for debugging.

## Where NVibrance stores things

| What | Location |
| --- | --- |
| Profiles | `%APPDATA%\NVibrance\profiles.json` |
| Corrupt-profile backup | `%APPDATA%\NVibrance\profiles.json.bad` |
| Log file | `%LOCALAPPDATA%\NVibrance\logs\nvibrance.log` |
| Previous log (rotated) | `%LOCALAPPDATA%\NVibrance\logs\nvibrance.1.log` |
| Autostart entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `NVibrance` |

Notes:
- Profiles are written a moment after you make a change, and again on exit. Writes are atomic, so an interrupted save cannot leave a half-written file.
- If `profiles.json` is ever unreadable, it is copied to `profiles.json.bad` and startup continues with an empty list, so the damaged file is still recoverable by hand.
- The two folders differ on purpose. `%APPDATA%` (Roaming) holds settings that should follow you to another machine, so profiles live there. `%LOCALAPPDATA%` holds machine-specific data, so logs live there — they reference local paths, process ids and hardware, and where roaming profiles or folder redirection are configured, anything under Roaming is copied at logon.
- The log rotates at 1 MB and keeps one previous file, so it cannot grow without bound.
- Icons are loaded from executable files and cached in memory only; nothing is written to disk.
- Uninstalling is just deleting the executable — remove the two folders above and the registry value to clear everything.

## Troubleshooting

**Start with the log.** `%LOCALAPPDATA%\NVibrance\logs\nvibrance.log` records every profile applied and restored. To watch it live while you reproduce a problem:

```powershell
Get-Content "$env:LOCALAPPDATA\NVibrance\logs\nvibrance.log" -Wait -Tail 20
```

**If a game is not being detected**, restart with `--verbose` and reproduce. Every foreground change then logs the window handle, process id, resolved executable path, and whether a profile matched:

```
NVibrance.exe --verbose
...
[DEBUG] Foreground hwnd=0x50A32 pid=18244 path=C:\Games\Apex\r5apex_dx12.exe -> profile 'Apex'
```

That line tells you which of three things went wrong: `path=<unresolved>` means the executable path could not be read (the profile can still match by process name); a path that does not match your profile means the game launched from a different location than the one you added; and `no matching profile` with the correct path means the profile exists for a different executable — many games ship separate DirectX 11 and 12 binaries, so a profile for `r5apex.exe` will not match `r5apex_dx12.exe`.

Other notes:
- If vibrance changes do not apply at all, verify NVIDIA drivers and that NvAPI is available. Errors from the driver are logged.
- If icon loading fails on some executables, it falls back gracefully.
- Autostart writes to the current user registry key `Software\Microsoft\Windows\CurrentVersion\Run`.

## Acknowledgements
- Inspired by [VibranceGUI](https://github.com/juv/vibranceGUI) — thanks to the original project for the idea and UX inspiration.

## Development status
This is a hobby project. Development is experimental — the project has only been worked on for two days and may contain rough edges.

