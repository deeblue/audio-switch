# Phase 5a review

## Result

Stage 5a implementation and automated review passed. The 3.5 mm headset was connected before testing. Manual interaction with WPF controls and external USB/HDMI equipment remains on the acceptance list; automated tests do not claim those observations.

## Verification

Each command ran in a new PowerShell process. No `PYTHONPATH` was set.

| Command | Result |
|---|---|
| `dotnet test src/AudioSwitch.sln -c Release` | Passed: 83, skipped: 8, failed: 0. The 8 skips are hardware/live tests not enabled for this run. |
| `probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .` | Passed: 55. Parser tests print usage text while validating invalid arguments. |
| `dotnet format src/AudioSwitch.sln --verify-no-changes` | Passed. |
| `probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare` | Regenerated immediately before live tests. Preflight confirmed two active endpoints per flow and a connected 3.5 mm jack. |
| `$env:AUDIOSWITCH_LIVE='1'; dotnet test src/AudioSwitch.sln -c Release` | Passed: 91, skipped: 0, failed: 0. Platform live tests capture a snapshot immediately before and after each test and compare endpoint/devnode/display state; no state mismatch blocked the run. |
| `git diff --check` | Passed. |

The earlier build attempt exposed an ambiguous WPF/WinForms `KeyEventArgs` type and then nullable warnings in the topology view; both were corrected. Final Release test build completed with no compiler warnings.

## Requirement trace

| Plan item | Implementation | Evidence |
|---|---|---|
| Hotkey capture handles WPF `SystemKey`, `ImeProcessedKey`, modifier-only and Escape; parser shared with platform service | `src/AudioSwitch.App/SettingsWindow.cs:322`; `src/AudioSwitch.Presentation/HotkeyGestureFormatter.cs`; `src/AudioSwitch.Core/HotkeyBindingParser.cs`; `src/AudioSwitch.Platform.Windows/WindowsHotkeyService.cs:22` | `HotkeyGestureFormatterTests.FormatsSupportedModifierAndKeyCombinations`, `ModifierOnlyInputDoesNotProduceABinding`, `EscapeClearsTheBinding`, `FormattedBindingsAreAcceptedByTheSharedParser` |
| Core builds nested USB hub tree, retains direct USB devices, groups display and audio endpoint | `src/AudioSwitch.Core/TopologyBuilder.cs:130`, `:171` | `TopologyBuilderHubTests` covers external hub, nested hubs, direct USB, and combined USB display/audio; Core golden suite passed. |
| Alias input prefills Device Description and no-op writes are suppressed | `src/AudioSwitch.Presentation/DeviceSettingsViewModel.cs:50`, `:65` | `DeviceSettingsViewModelTests.SelectionPrefillsAliasFromDeviceDescription`, `ApplyingUnchangedAliasDoesNotWriteOrSave` |
| Alias transaction/rollback, clear, existing-path calibration, listening lifecycle in Presentation | `src/AudioSwitch.Presentation/DeviceSettingsViewModel.cs:60`, `:111`, `:167`, `:222` | `DeviceSettingsViewModelTests` covers reverse rollback, clear, current-path calibration, and stop on repeat/select/page change/close. |
| SettingsWindow is a thin control/event-forwarding layer with one statement per line | `src/AudioSwitch.App/SettingsWindow.cs` | `dotnet format` and full App build passed; tree construction uses Core `TopologyTree` and does not traverse devnode ancestry or classify hubs. |
| Configure link opens Interface Definition; gear and tray Settings open Device | `src/AudioSwitch.App/QuickSwitchPanelWindow.xaml.cs:66`; `src/AudioSwitch.App/App.xaml.cs:170` | Inspected click routing and `SettingsWindow.SelectTab`; WPF interaction remains manual. |
| Hotkey success shows destination name; failure shows reason and logs | `src/AudioSwitch.Presentation/HotkeySettingsViewModel.cs:51`; `src/AudioSwitch.App/App.xaml.cs:221` | `HotkeySettingsViewModelTests.PlaybackCycleMovesToNextActiveEndpointAndSkipsUnavailable`, `CycleDoesNothingWhenNoActiveEndpointIsAvailable`; live endpoint cycling and tray visibility are listed below for manual acceptance. |

## Manual acceptance list

1. Record Ctrl+Alt+F1, Ctrl+Shift+D1, Win+Alt+A; confirm modifier-only presses do not change the field and Escape clears it.
2. Connect a USB speaker directly and through a hub/dock (including a hub under another hub); confirm the tree shows the observed parent chain and groups USB display/audio functions together.
3. Confirm direct USB devices do not gain an unconfigured Dock/Hub node.
4. Select endpoints with and without aliases; confirm the prefilled text is the Windows Device Description, unchanged apply makes no rename, and clear restores the original name.
5. Open Settings from the quick-panel gear and tray menu; confirm Device opens. Use “設定接口…” and confirm Interface Definition opens, including when Settings is already open.
6. Cycle each flow by hotkey; confirm the tray notice names the selected endpoint and failure notice gives the reason while a log entry is written.
7. Start capture listening; confirm it stops on a second press, endpoint change, tab change, and window close.

## Scope notes

No probe schema or golden fixture changed. The live suite reported all 11 Platform.Windows tests passed with the state guard enabled. Physical USB topology, WPF interaction, and tray notification display require the manual checks above.
