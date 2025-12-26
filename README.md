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
- Command-line: `--minimized` starts the app hidden (used by autostart entry).

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

## Data & Persistence
- Profiles saved to: `%APPDATA%\NVibrance\profiles.json`
- Icons are loaded from executable files and cached.

## Troubleshooting
- If vibrance changes do not apply, verify NVIDIA drivers and that NvAPI is available.
- If icon loading fails on some executables, it will fallback gracefully.
- Autostart writes to the current user registry key `Software\Microsoft\Windows\CurrentVersion\Run`.

## Acknowledgements
- Inspired by [VibranceGUI](https://github.com/juv/vibranceGUI) — thanks to the original project for the idea and UX inspiration.

## Development status
This is a hobby project. Development is experimental — the project has only been worked on for two days and may contain rough edges.

