# AudioSwitch Windows 應用程式

Windows 桌面程式以 .NET 10 建置，視窗使用 WPF，系統匣使用 WinForms `NotifyIcon`。

## 建置

在 PowerShell 從儲存庫根目錄執行：

```powershell
dotnet build src/AudioSwitch.sln -c Release
```

## 測試

執行完整自動測試：

```powershell
dotnet test src/AudioSwitch.sln -c Release
```

實機 live 測試預設停用。執行前立刻重新產生探針比對檔：

```powershell
probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare
$env:AUDIOSWITCH_LIVE = '1'
dotnet test src/AudioSwitch.sln -c Release
```

每個 live 測試會在測試前後立即擷取系統狀態並比較；若發現狀態不一致，該測試程序會停止後續會寫入狀態的 live 測試。

探針的 Python 單元測試：

```powershell
probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .
```

## 發佈

建立 Windows x64 Release 自包含版本：

```powershell
dotnet publish src/AudioSwitch.App -c Release -r win-x64 --self-contained
```

發佈目錄為 `src/AudioSwitch.App/bin/Release/net10.0-windows/win-x64/publish/`。

## 使用者資料

設定檔存放於 `%APPDATA%\AudioSwitch\`：

- `ports.json`：本機實體接口示意圖與校準路徑。
- `aliases.json`：還原端點原始描述所需的資料。
- `settings.json`：快捷鍵、自動啟動偏好與首次通知狀態。

程式記錄存放於 `%LOCALAPPDATA%\AudioSwitch\logs\`。
