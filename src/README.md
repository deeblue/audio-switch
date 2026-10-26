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

## 畫面截圖

截圖工具 `tools/AudioSwitch.Screenshots` 以固定假資料產生快速切換面板、設定視窗各分頁、螢幕識別視窗與浮動說明的 PNG。情境包含只有內建端點、Dock／Hub 串接與藍牙／虛擬／未辨識、極長名稱、未設定接口的 USB、沒有端點、切換失敗、校準各步驟、大量接口與接口表屬於另一台電腦。每個畫面輸出淺色／深色與 100%／200% 四種版本，另附 `icon-overview.png`：各尺寸圖示在淺色與深色底上的樣子。輸出預設位於 `artifacts/screenshots/current/`，`artifacts/` 不納入版本控制：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release
```

也可指定輸出目錄：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release -- artifacts/screenshots/review
```

`--live` 使用目前 Windows 的端點與裝置拓樸，設定檔只從 `%APPDATA%\AudioSwitch\` 複製到暫存目錄後讀取；截圖結束會刪除暫存副本。此模式不切換預設端點、不改名、不試音、不取樣麥克風、不註冊快捷鍵，也不變更開機啟動設定：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release -- artifacts/screenshots/live --live
```

## 應用程式圖示

向量原稿在 `src/AudioSwitch.App/Assets/`：`AudioSwitch.svg` 與 `AudioSwitch.Tray.svg` 用於 32px 以上，`*.Small.svg` 是 24px 以下的簡化版；系統匣原稿以 `currentColor` 著色。修改原稿後重新產生 `.ico`（16、20、24、32、40、48、64、256 px），再重新建置 App，並把 `.ico` 一併 commit：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release -- --icons
```

產生的檔案為 `AudioSwitch.ico`（exe 與視窗）、`AudioSwitch.Tray.Dark.ico`（淺色工作列用的深色圖案）與 `AudioSwitch.Tray.Light.ico`（深色工作列用的白色圖案）。系統匣依 `SystemUsesLightTheme` 與目前 DPI 選用對應的檔案與尺寸。

## Python 探針測試

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
