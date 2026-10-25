# AudioSwitch Windows app

The Windows desktop app targets .NET 10 and uses WPF for its windows and WinForms `NotifyIcon` for the system tray.

## Build

From the repository root in PowerShell:

```powershell
dotnet build src/AudioSwitch.sln -c Release
```

## Tests

Run the full automated suite:

```powershell
dotnet test src/AudioSwitch.sln -c Release
```

The hardware live tests are opt-in. Regenerate the probe comparison file immediately before running them:

```powershell
probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare
$env:AUDIOSWITCH_LIVE = '1'
dotnet test src/AudioSwitch.sln -c Release
```

Each live test records system state immediately before and after the test and compares the snapshots. A detected state mismatch blocks later state-changing live tests in that test process.

The probe Python unit tests run with:

```powershell
probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .
```

## Publish

Create a self-contained Windows x64 Release build:

```powershell
dotnet publish src/AudioSwitch.App -c Release -r win-x64 --self-contained
```

The publish directory is `src/AudioSwitch.App/bin/Release/net10.0-windows/win-x64/publish/`.

## User data

Configuration is stored under `%APPDATA%\AudioSwitch\`:

- `ports.json`: per-machine physical interface diagram and calibration paths.
- `aliases.json`: original endpoint descriptions needed to restore aliases.
- `settings.json`: hotkeys, autostart preference, and first-run notice state.

Application logs are written under `%LOCALAPPDATA%\AudioSwitch\logs\`.
