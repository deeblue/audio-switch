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

實機 live 測試預設停用。執行前立刻重新產生探針比對檔，並排除會改動系統狀態的 `EndpointWriteLiveTests`：

```powershell
probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare
$env:AUDIOSWITCH_LIVE = '1'
dotnet test src/AudioSwitch.sln -c Release --filter "FullyQualifiedName!~EndpointWriteLiveTests"
```

`EndpointWriteLiveTests` 會實際改動這台電腦：暫時替內建播放端點改名、切換每個方向三個 role 的預設端點、播放試音並開啟麥克風取樣，結束後還原。只在可以接受這些變動時才拿掉篩選條件執行。每個 live 測試會在測試前後立即擷取系統狀態並比較；若發現狀態不一致，該測試程序會停止後續會寫入狀態的 live 測試。

## 畫面截圖

截圖工具 `tools/AudioSwitch.Screenshots` 以固定假資料產生快速切換面板、設定視窗各分頁、螢幕識別視窗與浮動說明的 PNG。情境包含只有內建端點、Dock／Hub 串接與藍牙／虛擬／未辨識、極長名稱、未設定接口的 USB、沒有端點、切換失敗、校準各步驟、大量接口與接口表屬於另一台電腦。每個畫面輸出淺色／深色與 100%／200% 四種版本，另附 `icon-overview.png`：各尺寸圖示在淺色與深色底上的樣子。輸出預設位於 `artifacts/screenshots/current/`，`artifacts/` 不納入版本控制：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release
```

加上 `--english` 以英文介面產生同一組截圖（假資料中的裝置名稱維持原樣），用來檢查英文版排版。

也可指定輸出目錄：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release -- artifacts/screenshots/review
```

`--live` 使用目前 Windows 的端點與裝置拓樸，設定檔只從 `%APPDATA%\AudioSwitch\` 複製到暫存目錄後讀取；截圖結束會刪除暫存副本。此模式不切換預設端點、不改名、不試音、不取樣麥克風、不註冊快捷鍵，也不變更開機啟動設定：

```powershell
dotnet run --project tools/AudioSwitch.Screenshots -c Release -- artifacts/screenshots/live --live
```

## 介面語言

介面文字在程式碼與 XAML 中以繁體中文撰寫：C# 經 `Loc.T`／`Loc.F` 取用，XAML 寫成 `{app:Loc '…'}`。英文對照表在 `src/AudioSwitch.Core/EnglishText.cs`。新增或修改介面文字時一併更新對照表；`LocalizationTests` 會掃描 `src/` 的所有中文字串，缺少英文、含中文的插值字串、XAML 中未經 `app:Loc` 的中文、未使用的對照項目或 `{0}` 佔位符不一致都會讓測試失敗。

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

建立 Windows x64 Release 自包含版本。結果是單一個 `AudioSwitch.App.exe`（約 170 MB），內含 .NET 執行環境，不需安裝，複製到任何 Windows 10／11 x64 電腦即可執行；換新版時直接覆蓋這個檔案，設定檔與記錄在使用者資料夾，不受影響：

```powershell
Remove-Item -Recurse -Force dist/AudioSwitch -ErrorAction SilentlyContinue
dotnet publish src/AudioSwitch.App -c Release -r win-x64 --self-contained -o dist/AudioSwitch
```

單一檔案由 `AudioSwitch.App.csproj` 的 `PublishSingleFile` 設定，不壓縮：壓縮後檔案約小一半，但執行時記憶體約多一倍，對常駐系統匣的程式不划算。除錯資訊內嵌在組件中，記錄檔裡的錯誤堆疊仍有行號。發佈前先刪除舊的輸出資料夾，免得留下多檔案版本的舊檔。執行中的程式會鎖住 exe，發佈前要先結束它。

輸出到 `dist/AudioSwitch/`（不納入版本控制），執行 `dist/AudioSwitch/AudioSwitch.App.exe`。`src/AudioSwitch.App/bin/` 下的檔案是建置與測試用的中間產物。

版號設定在 `src/AudioSwitch.App/AudioSwitch.App.csproj` 的 `<Version>`。建置時另外寫入 commit、工作目錄是否有未提交修改，以及建置時間；設定視窗「一般」頁顯示完整版本資訊，系統匣圖示的提示文字顯示版號。

## 使用者資料

設定檔存放於 `%APPDATA%\AudioSwitch\`：

- `ports.json`：本機實體接口示意圖與校準路徑。
- `aliases.json`：還原端點原始描述所需的資料。
- `settings.json`：快捷鍵、自動啟動偏好、介面語言、首次通知狀態與 Dock／Hub 名稱。

程式記錄存放於 `%LOCALAPPDATA%\AudioSwitch\logs\`。
