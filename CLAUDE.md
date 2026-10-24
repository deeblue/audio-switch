# audio-switch：給接手的 session

目的與範圍見 `README.md`。這是一個 side project，跟 `D:\WORK\Claude\poc\AI-Vibe-Hub` 是不同的 repo，不要去改那邊的檔案。

核心目標是**以本機實體接口（USB-A、USB-C／Thunderbolt、HDMI、3.5mm、內建）為根**呈現音訊端點，讓使用者立刻找到對的裝置；端點仍是切換單位。呈現原則見 README「呈現方式」，端點分類與接口對應見「端點來源分類與接口對應」，執行順序見「實作計畫」。下一步是 `probes/` 的 Python 測試應用，需求見 `probes/TASK.md`，逐步執行計畫見 `docs/superpowers/plans/2026-10-23-audio-topology-probe.md`；尚未開始寫產品程式。產品必須動態列舉當下端點，不要求預先登記設備。

## 慣例

- 文件與介面文字用繁體中文；程式碼、識別字、commit 訊息用英文。
- 只在使用者要求時 commit；沒有 remote，不 push。
- 文件只寫現狀，不寫經過。
- 先讓使用者決定下面「待決事項」，再開始寫產品程式。`probes/` 的測試應用不受此限。

## 技術上已知的事

- **列出裝置與監看插拔**：Windows Core Audio，`IMMDeviceEnumerator`（`EnumAudioEndpoints`、`GetDefaultAudioEndpoint`），插拔與預設變更用 `IMMNotificationClient`。這些是公開 API。
- **設定預設裝置**：公開 API 沒有受支援的設定方法；SoundSwitch 使用未公開的 `IPolicyConfig.SetDefaultEndpoint(deviceId, role)`。沒有相容性保證，Windows 更新後可能失效。不要未查證就推論其他工具使用同一實作。
- **三個 role**：`eConsole`、`eMultimedia`、`eCommunications`。播放與錄音各有自己的預設端點；通訊預設可能與一般預設不同。切換哪些 role 是待決事項。
- **四類端點**：A. USB 音訊（含 Dock 3.5mm）沿 parent 取 `DEVPKEY_Device_LocationPaths`，對接口校準表做最長前綴比對；B. 顯示器音訊掛在 Intel Display Audio／GPU 音訊下，**不在 USB 樹中**，經 Dock 也一樣，要用 `IKsJackSinkInformation` 對 DisplayConfig 顯示路徑；USB 顯示裝置（7C、DisplayLink）例外，其顯示 adapter 在 USB 樹下，與同一 USB 裝置的音訊併組；C. 主機板內建音效用 `IKsJackDescription`；D. 藍牙、虛擬等不判斷位置。Dock 沒有專門規則：以 `PKEY_Device_ContainerId` 歸組，以可移除與否區分 Dock 內建音效和 Dock USB 孔上的裝置；Dock 上的螢幕依同一個機身接口併入 Dock 節點。Dock 的具體輸出孔與 Dock USB 孔位置通常無法判斷，只顯示型號、螢幕編號／排列位置或 port 編號。
- **辨認限制**：任何單一線索都不能證明「這是左螢幕／Dock 耳機孔」。3.5mm 插孔不會提供耳麥品牌；驅動可能讓耳機和內建喇叭共用一個端點；許多 USB 音效晶片不回報插入偵測。無法判定時標示未辨識，不以名稱猜測。
- **確認方式**：播放端點用 WASAPI 試音；錄音端點用 `IAudioMeterInformation` 顯示峰值；螢幕用「識別」大字。只對 `DEVICE_STATE_ACTIVE` 端點執行；插拔通知後重讀狀態。
- **別名**：寫入端點的 `PKEY_Device_DeviceDesc`，一般使用者權限即可（本機已驗證）；Windows 顯示為「別名 (介面卡名稱)」，Windows 設定、Teams、Zoom 都看得到。同一實體設備的播放與錄音端點共用同一別名。Windows 依端點保存名稱；程式只另存「端點 ID → 原始名稱」供還原，新端點不推測沿用別名。Teams 對可插拔裝置在重新插拔後更新名稱，內建裝置須完整重開 Teams。Microsoft 文件註明用戶端不應設定端點屬性，沒有相容性保證。
- **其他程式跟隨預設**：Teams 通話中跟隨 Windows 預設播放與錄音端點（本機已驗證）；Zoom「與系統相同」跟隨系統預設。跟隨哪個 role 未驗證。
- 每個應用程式各自的輸出裝置要用另一個未公開介面（`IAudioPolicyConfigFactory`），不在範圍內。
- **這台電腦**：Lenovo ThinkPad T14p Gen 3（21RU），Windows 11 Pro x64 組建 26300；有 Python 3.12（py -3.12）與 .NET 8.0.11 執行環境，沒有 .NET SDK。唯讀探針在 `probes/`：讀取 Core Audio、PnP 祖先鏈（LocationPaths、ContainerId、RemovalPolicy、`_PLD`）、KS jack 描述與 DisplayConfig；結果見 README「本機可行性實驗」。`Speakers (speaker)`（`USB\VID_303A&PID_2986`）是 7C，同一 USB 裝置也提供 S2719DGF 顯示路徑。

## 待決事項

**正式版語言建議**：C#／.NET 10 LTS + Windows Forms（`NotifyIcon` 常駐系統匣、TreeView 呈現接口樹、可自包含發佈）。需先安裝 .NET 10 SDK；.NET 10 支援至 2028-11-14。測試應用用 Python。[官方支援期限](https://dotnet.microsoft.com/en-us/platform/support/policy)

以下首版行為仍待使用者決定：

1. **正式版語言**：是否採用上面的建議。
2. **切換哪些 role**：一般預設與通訊預設要一起切，或分開。
3. **快捷鍵**：預設組合鍵；在當下可用端點中循環，或另提供可選的自訂循環集合（所有端點仍可從系統匣直接選取）。
4. **開機自動啟動**：要不要預設開啟。
5. **自動切換**（README 第 5 項，後續範圍）：若納入首版，規則怎麼設定。

## 驗收

在這台電腦上：

- 端點依機身接口呈現；接在 Dock 上的螢幕與接在筆電 HDMI 的同型號螢幕，能從樹狀位置、螢幕編號與排列位置立即分辨。
- Dock 3.5mm 與 Dock USB 孔上的 USB speaker 各自出現在 Dock 節點下。
- 未曾登記、由 Windows 新列出的播放／錄音端點，不經預先設定即出現在系統匣並可選取；無法對應接口的放在未辨識。
- 快捷鍵只循環目前可用的候選，拔除後清單更新且程式不當掉。
- 7C 播放模式能與筆電喇叭切換，錄音模式能與內建／其他麥克風切換。
- 記錄各情境實際拓樸可見程度，不把盤點結果當成支援清單。若驅動只提供共用端點，以端點層級切換。YouTube 等已在播放的程式是否立即改走新預設端點須實測記錄。
