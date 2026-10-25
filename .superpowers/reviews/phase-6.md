# Phase 6 review

## Result

Automated Release and live validation passed. The self-contained win-x64 publish completed. The physical 3.5 mm headset was connected at live-test preflight; Render and Capture each had two Active endpoints, and both headset KS jack records reported connected. Automated checks do not establish behavior for every external display, dock, conferencing app, or DPI arrangement.

## Verification

Every test/build/format command ran in a fresh PowerShell process. No `PYTHONPATH` was set.

| Command | Result |
|---|---|
| `dotnet build src/AudioSwitch.sln -c Release` | Passed, 0 warnings and 0 errors. |
| `dotnet format src/AudioSwitch.sln --verify-no-changes` | Passed. |
| `dotnet test src/AudioSwitch.sln -c Release` | Passed: 83, skipped: 8, failed: 0. The 8 skipped cases are opt-in hardware/live tests. |
| `probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .` | Passed: 55. Parser tests print usage while asserting invalid argument handling. |
| `probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare` | Regenerated before the live suite. Snapshot had 2 Active Render endpoints, 2 Active Capture endpoints, and connected KS records for the headset playback and capture endpoints. |
| `$env:AUDIOSWITCH_LIVE='1'; dotnet test src/AudioSwitch.sln -c Release` | Passed: 91, skipped: 0, failed: 0. The Platform.Windows suite ran 11/11 live tests. Each live test snapshots state immediately before and after; no state comparison blocked the suite. |
| `dotnet publish src/AudioSwitch.App -c Release -r win-x64 --self-contained` | Passed. Output: `src/AudioSwitch.App/bin/Release/net10.0-windows/win-x64/publish/`, 485 files, 181,251,635 bytes (172.86 MiB). |
| `git diff --check` | Passed. |

## Documentation changes

- README status and implementation plan now describe the completed Windows app and record remaining manual acceptance.
- CLAUDE.md opening now points to the current app state and setup guide.
- `src/README.md` documents build, automated and live tests, publish, configuration, and log locations.

## User acceptance checklist

1. Connect the 7C to a calibrated port and confirm it appears beneath that interface; move it to another port and confirm it moves with the connection.
2. Unplug and reconnect the 3.5 mm headset; confirm the quick panel updates and its playback and microphone endpoints appear under the 3.5 mm jack.
3. Select different endpoints during YouTube playback and a Teams call; confirm whether each application follows the new Windows default.
4. Apply an alias and check Windows Settings, Zoom, and Teams after their normal refresh/reconnect behavior; clear the alias and confirm the original name returns.
5. Add a port in the diagram; calibrate once by selecting the port before insertion and once by inserting before port placement. Delete a calibrated port and confirm its devices return to “USB（未設定接口）”.
6. Bind each global hotkey, cycle through active endpoints, unplug one candidate, and confirm the next cycle skips it and the tray notice identifies the selected endpoint.
7. Enable autostart, sign out and back in, then disable it and confirm the app no longer starts automatically.
8. Use Identify Screens and confirm each display shows its correct number.
9. In the Device tab, listen to the microphone and speak; confirm the meter responds. Stop and confirm the Windows microphone-use indicator clears on stop, endpoint change, and window close. Open the quick panel and confirm it does not activate microphone capture.
10. Use Windows display scaling at 200% and a second monitor with a different scale; confirm the quick panel stays near the tray and fully inside the correct screen. Confirm clicking the tray icon opens and closes it.

These checks require the user's 7C, docks, displays, applications, and account sign-in. No result is inferred for them from the automated test run.
