# audio-switch Windows 正式版執行計畫

> **給執行者（Codex）**：依階段順序執行。每個階段結束前必須通過「自我審查」一節的全部檢查，寫出審查報告，才能進入下一階段。步驟用 `- [ ]` 追蹤。遇到「停止條件」時立即停下回報，不要自行繞過。

**目標**：在這台 ThinkPad T14p Gen 3（21RU）上完成 Windows 版常駐工具：系統匣快速切換面板依機身接口分組列出播放／錄音端點，點選即切換；設定視窗提供裝置樹、別名、筆電示意圖的接口定義與校準、快捷鍵、開機啟動。

**技術**：C#／.NET 10 LTS、WPF（Fluent 主題）、系統匣圖示用 WinForms `NotifyIcon`、CommunityToolkit.Mvvm、xUnit；Windows 平台層可用 NAudio 包裝 Core Audio。

**規格來源（以這些為準，計畫與規格衝突時停下詢問）**：

- `README.md`：「要做到的」「呈現方式」「介面」「可行性與限制」「端點來源分類與接口對應」「本機可行性實驗」。
- `CLAUDE.md`：「慣例」「技術上已知的事」「待決事項」「驗收」。
- 參考實作：`probes/audio_topology/`。分類規則見 `classify.py`，校準規則見 `calibration.py`，CfgMgr32 讀取見 `native/pnp.py`，KS jack 的 IID 與呼叫順序見 `native/audio_topology.py`（已在本機實測），DisplayConfig 讀取見 `native/display_config.py`。

## 範圍

**本計畫要做**：README「要做到的」第 1～4 項；「介面」中的快速切換面板、設定視窗（裝置、接口定義的筆電示意圖、快捷鍵、一般）、未設定時的行為；別名寫回 Windows；試音、錄音峰值、識別螢幕。

**本計畫不做**：自動切換（第 5 項）、個別程式指定（第 6 項）、Dock／Hub 示意圖與定義（待 Dock 實測；本計畫只做「Dock／Hub（未定義）」顯示）、macOS 平台層、安裝程式與程式碼簽章。

**待決事項的處理**：快捷鍵機制要完整實作，但**預設不綁定任何組合鍵**；開機自動啟動要能設定，**預設關閉**。不自行選定其他預設值。

## 環境前置

- [x] 執行 `dotnet --list-sdks`。若沒有 10.x，自行執行 `winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements` 安裝（使用者已授權），完成後重開 shell 再確認 `dotnet --list-sdks` 列出 10.x。安裝失敗時停止回報。
- [x] 先前為本計畫安裝、但本計畫已不再使用的項目必須移除，例如 Avalonia 專案範本（`dotnet new uninstall Avalonia.Templates`）、`dotnet tool` 全域工具、其他 winget 套件。以 `dotnet new list`、`dotnet tool list -g`、`winget list` 確認，並在階段 0 的審查報告列出移除了哪些項目。.NET 10 SDK 保留；NuGet 快取不需清除。
- [x] 確認 `probes/.venv/Scripts/python.exe` 可執行探針：`probes/.venv/Scripts/python.exe probes/audio_topology.py tree`。
- [x] 以 NuGet 查詢下列允許套件的最新穩定版，寫入 `src/Directory.Packages.props` 鎖定版本。不得使用預覽版。

  | 用途 | 允許的套件 | 版本規則 |
  |---|---|---|
  | MVVM | `CommunityToolkit.Mvvm` | 最新穩定版 |
  | Windows Core Audio | `NAudio.Core`、`NAudio.Wasapi`（或合併套件 `NAudio`） | 最新穩定版；只在 `AudioSwitch.Platform.Windows` 引用 |
  | 測試 | `Microsoft.NET.Test.Sdk`、`xunit` 或 `xunit.v3`、`xunit.runner.visualstudio` | 所有測試專案用同一個 xUnit 主版本 |

  WPF 與 WinForms 是 .NET SDK 內建框架（`UseWPF`、`UseWindowsForms`），不需 NuGet 套件。這些套件自動帶入的相依套件不需另外列出，也不算清單外套件。

## 全域限制

1. 介面文字用繁體中文；程式碼、識別字、commit 訊息用英文。
2. 文件只寫現狀，不寫經過。審查報告寫在 `.superpowers/reviews/`（已在 `.gitignore`），不寫進 README。
3. 直接引用的 NuGet 套件只限「環境前置」表格中的套件（相依套件不在此限）；需要其他套件時**停止**詢問。
4. `AudioSwitch.Core` 與 `AudioSwitch.Presentation` 目標為 `net10.0`，不得引用 Windows 專屬 API（P/Invoke、COM、`Microsoft.Win32`、NAudio）或 UI 框架（WPF、WinForms）；平台 API 只能在 `AudioSwitch.Platform.Windows`，UI 框架只能在 `AudioSwitch.App`。這是為了讓日後的 macOS 版（Avalonia、.NET for macOS 或其他）可以直接重用 Core 與 view model。
5. 不以裝置名稱、VID/PID、ContainerId 或匯流排標籤單獨推斷機身接口；不以 `_PLD` 判斷孔位或類型（`_PLD` 只在詳細資訊顯示）；程式碼中不得寫死本機裝置名稱或 VID/PID。
6. 只對 `DEVICE_STATE_ACTIVE` 端點做分類、切換、試音與峰值。
7. 系統狀態寫入只有四種，且各自集中在一個類別：設定預設端點（`IPolicyConfig`）、寫入端點 `PKEY_Device_DeviceDesc`、`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的開機啟動值、`%APPDATA%\AudioSwitch\` 下的設定檔。其他登錄、裝置屬性或系統設定一律不寫。不得呼叫 `AudioPolicyConfig`（個別程式指定不在本計畫）。
8. 會改變系統狀態的實機測試（live test）必須先保存原狀，結束時（含失敗）還原，並在測試內讀回確認已還原。
9. COM 回呼（`IMMNotificationClient`）不得直接操作 UI；先合併短時間內的多個事件（約 300 ms），再在 UI 執行緒重建資料。
10. 不修改 `D:\WORK\Claude\poc\AI-Vibe-Hub`；沒有 remote，不 push。
11. 每個階段通過自我審查後自行 commit 一次（使用者已授權，不需另外詢問）；commit 前工作目錄只能包含該階段的變更。

## 架構

```
src/
  AudioSwitch.sln
  Directory.Build.props          # net10.0、Nullable、TreatWarningsAsErrors、LangVersion
  Directory.Packages.props       # 集中鎖定套件版本
  AudioSwitch.Core/              # net10.0，純邏輯，無平台 API
  AudioSwitch.Platform.Windows/  # net10.0-windows，實作 Core 定義的介面
  AudioSwitch.Presentation/      # net10.0，view model（CommunityToolkit.Mvvm），不引用 WPF
  AudioSwitch.App/               # WPF，net10.0-windows；只有畫面、平台接線與系統匣
tests/
  AudioSwitch.Core.Tests/        # 純單元測試與 golden 測試
  AudioSwitch.Platform.Windows.Tests/  # live 測試，需 AUDIOSWITCH_LIVE=1
  AudioSwitch.Presentation.Tests/ # view model 單元測試（以假的平台介面）
```

**Core 定義的平台介面**（名稱可調整，職責不可合併到 App 或 Presentation）：

**WPF 注意事項**：主題用 WPF 的 Fluent 主題並跟隨系統淺色／深色（`ThemeMode`）。若該 API 在 .NET 10 仍標為實驗性而產生診斷（例如 `WPF0001`），只在 `AudioSwitch.App` 以 `NoWarn` 抑制該代碼並在專案檔註明原因，不得全域關閉警告。系統匣圖示用 `System.Windows.Forms.NotifyIcon`（App 專案同時設 `UseWPF` 與 `UseWindowsForms`），注意兩者同名型別的命名空間衝突。Presentation 需要回到 UI 執行緒、開關視窗或顯示對話框時，透過 Presentation 定義的介面（例如 `IUiDispatcher`、`IWindowService`），由 App 實作。

| 介面 | 職責 |
|---|---|
| `IAudioEndpointSource` | 列舉端點（ID、flow、state、名稱、三個 role 的預設狀態、devnode ID）；插拔與預設變更事件 |
| `IDeviceTreeSource` | 依 devnode ID 取祖先鏈（instance ID、parent、名稱、LocationPaths、ContainerId、RemovalPolicy、可移除位元、`_PLD` 原始值） |
| `IJackInfoSource` | 端點的 `IKsJackDescription`／`IKsJackDescription2`／`IKsJackSinkInformation` |
| `IDisplayPathSource` | DisplayConfig 作用中路徑、EDID 名稱、adapter instance ID、穩定 connector 欄位、螢幕範圍（供「識別」） |
| `IMachineIdentity` | SMBIOS 廠牌、機型、MTM |
| `IDefaultEndpointWriter` | 對某 flow 設定三個 role 的預設端點並讀回 |
| `IEndpointNameWriter` | 讀寫 `PKEY_Device_DeviceDesc` |
| `IAudioProbe` | 指定端點試音、錄音峰值 |
| `IHotkeyService`、`IAutostartService`、`ITrayAnchor` | 全域快捷鍵、開機啟動、彈出面板定位（工作列位置與游標） |

**Core 的資料流**：平台介面 → `Snapshot`（同探針的 snapshot 結構）→ `TopologyBuilder` → `TopologyTree` → App 的 view model。校準：兩份 `Snapshot` → `AttachmentDetector` → `PortTable` 更新。

**設定檔**（`%APPDATA%\AudioSwitch\`，UTF-8 JSON，含 `schema_version`，寫入時先寫暫存檔再取代）：

- `ports.json`：機器身分、接口清單（ID、使用者名稱、類型、示意圖上的邊與順序、各傳輸類型的 LocationPaths 前綴、顯示 connector）。機器身分不符時不套用，並在設定視窗提示。
- `aliases.json`：端點 ID → 原始 `DeviceDesc`（清除別名時還原用）。
- `settings.json`：快捷鍵綁定、開機啟動、通知是否已顯示過。

## 需求清單（審查時逐條追蹤）

| ID | 需求 | 規格出處 |
|---|---|---|
| R1 | 列出所有作用中播放／錄音端點；同名端點以端點 ID 各自列出 | README「要做到的」2、「呈現方式」5 |
| R2 | 四類端點分類與樹狀分組行為和探針 `build_tree` 一致（類別、接口 ID、裝置分組） | README「端點來源分類與接口對應」；`classify.py` |
| R3 | 校準接點偵測和探針 `attachment_nodes`／`calibrate_port_map` 一致：最上層新增或移動節點、只看 LocationPaths 變化、每種傳輸最多一個、PCIe 須在 root port 之後；同類兩個接點時不寫入 | `calibration.py`；`probes/README.md` |
| R4 | 接口表每台電腦一份，綁定 SMBIOS 廠牌與機型；不符時不套用並提示 | README「介面／接口定義」 |
| R5 | 選取端點即對該 flow 設定 `eConsole`、`eMultimedia`、`eCommunications`；切換後讀回三個 role 確認，失敗時顯示原因，不標示為成功 | CLAUDE.md「三個 role」；README「實作計畫」5 |
| R6 | 別名寫入 `DeviceDesc`；同一實體設備的播放與錄音端點寫同一別名；保存原始名稱並可還原；新端點不沿用其他端點的別名；別名不阻擋列出或切換 | CLAUDE.md「別名」；README「可行性與限制」 |
| R7 | 插拔與預設變更時更新面板、設定視窗與快捷鍵候選；裝置拔除不造成例外或當掉 | README「要做到的」4 |
| R8 | 快速切換面板：左鍵彈出、失焦關閉；播放／錄音兩頁；依接口分組；目前預設打勾；播放試音（面板不讀取麥克風）；齒輪開設定；右鍵選單（設定、識別螢幕、結束） | README「介面／快速切換面板」 |
| R9 | 設定視窗分頁：裝置、接口定義、快捷鍵、一般；裝置頁選取端點時示意圖對應孔標亮 | README「介面／設定視窗」 |
| R10 | 筆電示意圖：四邊（左、右、後、前）、從工具列拖放 USB-A／USB-C／HDMI／3.5mm、位置決定側別與順序、校準狀態標示、改名／重新校準／刪除、等價的鍵盤操作清單、擺放後名稱欄立即可編輯並預填全選 | README「介面／接口定義」 |
| R11 | 校準兩種順序（先擺再插、先插再擺）；只在接口定義頁開啟或校準待命時偵測插入；「這個裝置插在…」以目前路徑校準；校準時插入音訊裝置可選填別名；顯示輸出以接上螢幕校準；3.5mm 由 KS jack 對應不需插入 | README「介面／接口定義」 |
| R12 | 未設定時的分組與提示：「USB（未設定接口）」、不顯示 `USB(n)` 編號、「Dock／Hub（未定義）」、首次啟動通知一次、「設定接口…」連結；部分定義時混合顯示 | README「介面／未設定時」 |
| R13 | 試音（指定播放端點）、錄音峰值（`IAudioMeterInformation`，只在設定視窗裝置頁按「聽」時取樣）、識別螢幕（各螢幕中央顯示大字約 3 秒，其餘部分透明） | CLAUDE.md「確認方式」；README「介面」 |
| R14 | 全域快捷鍵：播放與錄音各一組，在目前可用端點間循環並跳過不可用端點；預設不綁定 | README「要做到的」3；CLAUDE.md 待決 1 |
| R15 | 開機自動啟動可設定，預設關閉，只寫 HKCU Run | CLAUDE.md 待決 2 |
| R16 | 單一執行個體；常駐系統匣；結束時釋放快捷鍵與 COM 物件 | — |
| R17 | 錯誤寫入本機記錄檔（`%LOCALAPPDATA%\AudioSwitch\logs\`），不傳送任何資料 | — |
| R18 | Core 與 Presentation 不含平台 API 與 UI 框架（全域限制 4） | CLAUDE.md「正式版技術」 |

## 階段 0：專案骨架

- [x] 建立上面的目錄、方案與專案；`Directory.Build.props` 設 `Nullable=enable`、`TreatWarningsAsErrors=true`、`ImplicitUsings=enable`。
- [x] App 只顯示一個系統匣圖示與「結束」選單，確認能啟動與結束。
- [x] 每個測試專案各有一個會通過的測試。
- [x] 在 `.gitignore` 確認 `bin/`、`obj/` 已忽略（現有規則已涵蓋）。
- [x] 通過自我審查，commit。

## 階段 1：Core 邏輯（R2、R3、R4、R6 的資料部分、R12 的分組部分、R18）

**Golden 測試資料**：

- [x] 新增 `probes/export_golden.py`（唯讀，只讀 snapshot 與 ports.json），對每份 snapshot 輸出：每個端點的 `category`、`interface_id`、所屬裝置分組 key（USB devnode instance ID 或無）、顯示路徑所屬分組；以及校準結果（`status`、各接點的 `transport` 與 `location_paths`）。
- [x] 把 `probes/out/` 中 `port-left-usba`、`port-right-usba`、`port-left-usbc-front`、`port-left-usbc-rear`、`final-after` 五份 snapshot 與 `probes/ports.json` 複製到 `tests/AudioSwitch.Core.Tests/Fixtures/`，並用上面的腳本產生 golden JSON。校準 golden 至少涵蓋：四組相鄰孔位移動、`final-after → port-left-usba`（舊快照，必須成功且不含 iGPU）。
- [ ] 若 `probes/out/` 缺少上述檔案，**停止**回報，不要自行偽造 snapshot。

**實作**：

- [x] `Snapshot` 等模型可讀取探針的 snapshot JSON。
- [x] `TopologyBuilder`：移植 `classify.py` 的 `classify_endpoint`、`is_physical_jack`、`build_tree`。產品的根節點命名依 README「介面」：未校準的 USB 根為「USB（未設定接口）」；接口根使用使用者名稱。
- [x] `AttachmentDetector` 與 `PortTable`：移植 `attachment_nodes`、`attachment_transport`、`calibrate_port_map`、`usb_path_type`、顯示 connector 校準。另外新增「以目前路徑校準」：取該端點祖先鏈中，父節點為 USB root hub 或 PCIe root port 的那一個節點作為接點。
- [x] 設定檔讀寫：三個 JSON、`schema_version`、原子寫入、機器身分比對。
- [x] 別名資料邏輯：同一實體設備（同一 USB 裝置分組或同一 3.5mm 插孔）的播放與錄音端點視為一組；保存原始名稱；第一次設別名才保存原始名稱，之後改名不覆蓋。

**測試**（全部在 Core.Tests，不需實機）：

- [x] Golden 比對：C# 對每份 fixture 產生的類別、接口 ID、分組 key 與 golden 完全相同。
- [x] 校準 golden 比對，含 ambiguous、PCIe root complex 排除、只有屬性變化不算移動。
- [x] 同名端點不合併；未校準時 USB 裝置落在「USB（未設定接口）」且不輸出 `USB(n)`。
- [x] 機器身分不符時接口表停用。
- [x] 設定檔損毀或 `schema_version` 不符時不當掉，回報錯誤並以空設定運作。
- [x] 通過自我審查，commit。

## 階段 2：Windows 平台層唯讀（R1、R7 的事件部分、R13 的螢幕範圍）

- [x] 實作 `IAudioEndpointSource`（含三個 role 的預設狀態與 `IMMNotificationClient` 事件）、`IDeviceTreeSource`、`IJackInfoSource`、`IDisplayPathSource`、`IMachineIdentity`。IID、結構與呼叫順序以探針 `native/` 為準；KS jack 須經 `IDeviceTopology` → connector → `IPart`，`Activate` 後要 `QueryInterface`。
- [x] 單一屬性或單一端點讀取失敗時記錄錯誤並繼續，不中斷整體列舉。
- [x] Live 測試（`AUDIOSWITCH_LIVE=1`）：
  - [x] 和探針比對：`probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare` 產生 `probes/out/live-compare.json`，C# 讀到的作用中端點 ID 集合、各端點 devnode 祖先鏈的 LocationPaths、jack 描述的連接類型，必須和探針相同。
  - [x] 至少一個端點讀得到 jack 描述。
  - [x] 事件測試：訂閱後取消訂閱不拋例外（插拔事件由使用者驗收）。
- [x] 通過自我審查，commit。

## 階段 2a：Core 修正（R2、R3、R6 的資料部分）

在階段 3 之前完成。每項先寫會失敗的測試，再修正。

- [x] **根節點分類**：`TopologyBuilder` 判斷「藍牙／無線」「虛擬」時，和探針 `classify_endpoint` 一樣排除端點本身的 `SWD\MMDEVAPI\` devnode，只看其上的祖先。沒有 USB、顯示 sink、jack 或 HDAUDIO 證據，且祖先不是 `ROOT\MEDIA`、`SWD\`、`ROOT\SYSTEM` 的端點必須落在「未辨識」。
  - 測試：端點 devnode 為 `SWD\MMDEVAPI\…`、父節點為未知 `PCI\` 裝置時，根為「未辨識」；父節點為 `ROOT\MEDIA\…` 時，根為「虛擬」。
  - Golden：`probes/export_golden.py` 對每個端點另外輸出所在根節點 ID（含「內建」「藍牙／無線」「虛擬」「未辨識」；探針中未校準 USB 的「未辨識」對應 C# 的「USB（未設定接口）」，在腳本中明確轉換），C# golden 測試一併比對。重新產生 golden 後在審查報告記錄 SHA-256。
- [x] **損毀設定檔不得被覆寫**：`ConfigurationStore.Load` 某個檔案讀取或驗證失敗時，先把該檔改名為 `<檔名>.corrupt-<UTC 時間 yyyyMMddHHmmss>` 保留原內容，再以空設定運作並回報錯誤；改名失敗時，該檔在這次執行期間不得被 `Save` 寫回（回報錯誤，其他檔案照常寫入）。
  - 測試：損毀的 `aliases.json` 經 `Load` → `Save` 後，原內容仍存在於 `.corrupt-*` 檔；改名失敗的情境（例如以唯讀或鎖定模擬）下 `Save` 不改動該檔。
- [x] **以目前路徑校準取最上層接點**：`AttachmentDetector.FindCurrentPathAttachment` 在祖先鏈中有多個「父節點為 USB root hub 或 PCIe root port」的節點時，取最靠近電腦根的那一個（例如 Thunderbolt／USB4 Dock 取 root port 底下的 Dock 節點，而不是 Dock 內部 root hub 底下的 USB 裝置）。PCIe root port 改以結構判斷，不以裝置名稱判斷：父節點是 `PCI\` devnode 且有一條 LocationPaths 為 `PCIROOT(n)#PCI(xxxx)`（只有一層 `#PCI(`），而目前節點本身也是 `PCI\` devnode、LocationPaths 至少兩層 `#PCI(`（與 `IsAttachment` 的 PCIe 規則相同）。只看父節點層數不夠：主機的 xHCI（`PCI(1400)`）與 iGPU 也只有一層，root hub 不能因此被當成接點。USB root hub 維持以 instance ID 的 `ROOT_HUB` 判斷。
  - 測試：合成鏈「端點 → USB 裝置 → Dock root hub → Dock xHCI → Dock PCIe 交換器 → root port」回傳 Dock PCIe 交換器；直接接在主機 root hub 的 USB 裝置經外接 hub 時回傳該 hub；root port 名稱為非英文時結果相同。
- [x] 通過自我審查，commit。

## 階段 3：Windows 平台層寫入（R5、R6、R13 的音訊部分）

- [x] `IDefaultEndpointWriter`：`IPolicyConfig.SetDefaultEndpoint`，宣告依 SoundSwitch 或 AudioDeviceCmdlets 的公開原始碼，在註解中標明來源與 IID；三個 role 依序設定後逐一讀回。
- [x] `IEndpointNameWriter`：`IMMDevice.OpenPropertyStore(STGM_READWRITE)`，寫 `PKEY_Device_DeviceDesc`（`{a45c254e-df1c-4efd-8020-67d146a850e0}`, 2）並 `Commit`，重新開啟讀回確認。
- [x] `IAudioProbe`：指定端點以共用模式播放約 1 秒的提示音；錄音峰值以 `IAudioMeterInformation` 讀取。
- [x] Live 測試（`AUDIOSWITCH_LIVE=1`），每項都在 `finally` 還原並讀回確認：
  - 切換：保存兩個 flow 各三個 role 的原預設 → 切到另一個作用中端點 → 讀回 → 還原 → 讀回與原狀相同。
  - 改名：保存原 `DeviceDesc` 與 `FriendlyName` → 寫入測試名稱 → 每 100 ms 輪詢，最多 3 秒，確認 `FriendlyName` 以「寫入值 + ` (`」開頭 → 還原 `DeviceDesc` → 輪詢至 `FriendlyName` 回到原值。只選作用中 render、拓樸根為「內建」且沒有 `IsPhysicalJack` KS 記錄的端點；找不到就失敗，不改用其他端點。
  - 試音與峰值：呼叫不拋例外，峰值介於 0 與 1。
- [x] 狀態檢查：每項 live test 執行前後立即擷取快照，以 `probes/compare_state.py`（唯讀）比較端點集合、state、`defaults` 與 `friendly_name`；state 轉換標示為實體狀態改變。若不一致，停止後續 live test 並回報差異。
- [x] 通過自我審查，commit。

## 階段 3a：測試與介面修正（R1、R6、R13）

在階段 4 之前完成。每項先寫或先跑出會失敗的測試，再修正。

- [x] **實機比對只看作用中端點**：`probes/audio_topology.py snapshot`／`json` 會記錄所有狀態的端點（狀態比對需要）。階段 2 的實機測試（`EndpointSourceLiveTests`、`DeviceTreeLiveTests`、`JackInfoLiveTests`、`TopologySnapshotLiveTests`）從探針快照取預期值時，只取 state 為 Active 的端點。先重新產生 `live-compare.json`，確認這四個測試在修正前失敗、修正後通過。`probes/README.md` 註明快照包含非作用中端點，只有作用中端點會讀取拓樸並進入樹狀。
- [x] **探針測試的匯入方式**：`probes/tests/test_compare_state.py` 與其他探針測試一致，從 `probes.audio_topology` 匯入；以自我審查第 3 步的固定指令全部通過。
- [x] **平台類別實作 Core 介面**：`WindowsEndpointNameWriter` 實作 `IEndpointNameWriter`、`WindowsAudioProbe` 實作 `IAudioProbe`。在 `AudioSwitch.Platform.Windows.Tests` 新增一般（非 live）測試：以反射列出 `AudioSwitch.Core` 中所有 `public interface`，扣除明列的非平台介面，每個平台介面在 `AudioSwitch.Platform.Windows` 都必須有一個實作類別；尚未到實作階段的介面（`IHotkeyService`、`IAutostartService`、`ITrayAnchor` 等）以明列的待實作清單排除，並在該階段完成時移出清單。
- [x] **錄音峰值**：`IAudioMeterInformation` 在沒有任何程式錄音時通常回報 0。`IAudioProbe` 提供開始／停止峰值取樣：取樣期間以共用模式開啟該錄音端點的擷取串流（只讀峰值，不保存、不傳送音訊），停止時釋放。live 測試確認開始、讀取、停止不拋例外且峰值介於 0 與 1，停止後串流已釋放；峰值會隨聲音變動列入使用者驗收。
- [x] `WindowsEndpointNameWriter` 改用 NAudio 的公開 API（`MMDevice.GetPropertyInformation(StorageAccessMode.ReadWrite)`、`PropertyStore.SetValue`）；只有公開 API 無法達成時才用反射，並在註解說明原因。改完後重跑改名 live 測試。
- [x] 通過自我審查（第 3、4 步已更新），commit。

## 階段 4：App 殼與快速切換面板（R7、R8、R12 的介面部分、R16、R17）

- [x] 單一執行個體（具名 mutex）；第二個執行個體啟動時喚起既有執行個體的面板後結束。
- [x] 系統匣圖示（`NotifyIcon`）：左鍵開關面板；右鍵選單「設定」「識別螢幕」「結束」。
- [x] 面板為無邊框視窗，依 `ITrayAnchor`（工作列位置、游標位置）貼齊系統匣；失焦關閉；跟隨系統淺色／深色主題。
- [x] 面板內容依 README「介面／快速切換面板」與「未設定時」：播放／錄音兩頁、接口分組、別名或 Windows 名稱、目前預設打勾、播放端點的試音按鈕、角落齒輪；面板不讀取麥克風（峰值見階段 4a、5）。
- [x] 點選端點呼叫 `IDefaultEndpointWriter`，依讀回結果更新打勾；失敗時在面板內顯示原因。
- [x] 插拔事件合併後重建；面板開啟中裝置消失時，該項目移除且不當掉。
- [x] 首次啟動通知一次；「USB（未設定接口）」旁的「設定接口…」開啟設定視窗的接口定義頁。
- [x] 記錄檔與未處理例外處理：例外寫入記錄檔並在面板顯示簡短訊息，程式不結束。
- [x] 測試：Presentation.Tests 的 view model 單元測試（分組、打勾、切換失敗顯示、項目消失、首次通知只出現一次）。面板外觀、貼齊位置與失焦關閉列入使用者驗收清單。
- [x] 通過自我審查，commit。

## 階段 4a：面板修正（R8、R13、R16）

在階段 5 之前完成。能自動測試的項目先寫會失敗的測試，再修正。

- [x] **座標單位**：這台電腦為 200% 縮放。WinForms 的 `Cursor.Position`、`Screen.WorkingArea`、`Screen.Bounds` 是實體像素，WPF 的 `Left`／`Top`／`Width`／`Height` 是裝置無關單位（DIP），不可直接混用。`ITrayAnchor` 的實作改為以游標所在螢幕的 DPI 換算（例如 `VisualTreeHelper.GetDpi`、`PresentationSource.CompositionTarget.TransformFromDevice`，或 Win32 `GetDpiForMonitor`），回傳 DIP。把換算抽成純函式（輸入實體像素矩形、游標位置、縮放比例、面板 DIP 大小，輸出 DIP 位置）放在 Presentation，並以單元測試涵蓋 100%、150%、200% 與工作列在下／右／上／左的情況，確認面板完整落在工作區內。
- [x] **識別螢幕**：每個螢幕依該螢幕的 DPI 換算位置與大小；視窗設 `AllowsTransparency="True"` 讓背景真正透明，只在螢幕中央顯示編號大字約 3 秒，其餘畫面不被遮住、不攔截滑鼠。位置計算同樣以單元測試涵蓋不同縮放比例。
- [x] **快速面板移除峰值**：刪除面板的峰值文字、取樣計時器與 App 中的峰值取樣流程；開啟面板、切換分頁都不得呼叫 `IAudioProbe.StartPeakSampling`。`IAudioProbe` 的峰值方法保留給階段 5 的裝置頁使用。
- [x] **系統匣圖示開關面板**：面板失焦關閉與點系統匣圖示的切換互不干擾——面板開啟時點圖示要關閉面板，不可因「先失焦關閉、再被點擊打開」而重新出現（例如記錄失焦關閉的時間，在極短時間內收到的圖示點擊視為關閉）。判斷邏輯放在 Presentation 並寫單元測試。
- [x] **邏輯移出 App**：面板開啟時的重新整理、分頁切換與錯誤顯示的流程移到 Presentation 的 view model（透過 Core／Presentation 定義的介面取得快照、寫入預設端點、試音），`App.xaml.cs` 只負責建立物件、接線、系統匣與視窗。補上對應的 view model 單元測試（以假介面驗證）。
- [x] 通過自我審查，commit。手動驗收項目（面板位置、開關、識別螢幕外觀）列入審查報告的使用者驗收清單。

## 階段 5：設定視窗（R9、R10、R11、R13、R14、R15）

- [x] **裝置頁**：完整樹（含「Dock／Hub（未定義）」與螢幕）、端點細節（含 `_PLD` 原始解析）、別名編輯與清除、試音、識別螢幕；錄音端點旁的「聽」按鈕（按下才經 `IAudioProbe` 取樣該端點並顯示峰值，再按一次、選取其他端點、切換分頁或關閉視窗即停止並釋放擷取串流，同一時間只量一個端點）；選取端點時接口示意圖標亮對應孔。
- [x] **接口定義頁**：筆電俯視示意圖與等價清單，行為依 README「介面／接口定義」與 R10、R11。校準狀態：已校準（列出已有的傳輸類型）、缺部分路徑、未校準。ambiguous 時提示「請只插入一個裝置後重試」。
- [x] 「這個裝置插在…」從裝置頁對未對應接口的裝置執行。
- [x] 校準時偵測到音訊端點，顯示選填的別名欄位；留空不寫入。
- [x] **快捷鍵頁**：播放、錄音各一組可錄製的組合鍵；預設空白；`RegisterHotKey` 失敗（已被佔用）時顯示原因。循環只在目前作用中端點間進行。
- [x] **一般頁**：開機自動啟動，預設關閉。
- [x] 測試：示意圖 view model（拖放到邊後的側別與順序、移動、刪除、名稱預填）、校準流程狀態機（兩種順序、ambiguous、取消）、快捷鍵循環跳過不可用端點、開機啟動開關呼叫正確服務（以假物件驗證，不在單元測試寫登錄）。
- [x] 通過自我審查，commit。

## 階段 6：整體驗收與文件

- [ ] 執行全部測試（含 `AUDIOSWITCH_LIVE=1`）與狀態檢查。
- [ ] 以 Release 自包含發佈：`dotnet publish src/AudioSwitch.App -c Release -r win-x64 --self-contained`，記錄輸出大小。
- [ ] 更新 README「狀態」「實作計畫」與 CLAUDE.md 開頭段落為現狀；新增 `src/README.md` 說明建置、測試、發佈與設定檔位置。
- [ ] 產生「使用者驗收清單」（寫在審查報告，不寫進 README），逐項列出需要使用者操作的檢查：
  1. 7C 插在已校準的孔時出現在該接口下；換孔後跟著移動。
  2. 3.5mm 耳麥插拔時面板即時更新；耳機與麥克風落在 3.5mm 插孔下。
  3. 點選切換後 YouTube 與 Teams 是否跟著改走新端點。
  4. 設定別名後 Windows 設定頁、Zoom、Teams（重新插拔或重開後）顯示新名稱；清除後還原。
  5. 從示意圖新增接口並以兩種順序完成校準；刪除接口後裝置回到「USB（未設定接口）」。
  6. 快捷鍵綁定後循環切換，拔除裝置後循環清單更新。
  7. 開機自動啟動開啟後重新登入會啟動；關閉後不再啟動。
  8. 識別螢幕在每個螢幕顯示正確編號。
  9. 設定視窗裝置頁按麥克風的「聽」後說話，峰值會動；停止、選取其他端點或關閉視窗後，Windows 的麥克風使用中圖示消失。開啟快速切換面板時麥克風使用中圖示不出現。
  10. 在 200% 縮放下（以及外接不同縮放的螢幕時），面板出現在系統匣附近且完整在螢幕內；點系統匣圖示可開也可關。
- [ ] 通過自我審查，commit。

## 自我審查（每個階段結束時執行）

審查時假設自己的實作有錯；逐條對照規格原文，不憑記憶。

1. **建置**：`dotnet build src/AudioSwitch.sln -c Release`，0 個警告（警告視為錯誤）。
2. **格式**：`dotnet format src/AudioSwitch.sln --verify-no-changes`。
3. **測試**：本步驟的指令一律在新開的 shell 執行，不沿用執行過程中設定的環境變數（`AUDIOSWITCH_LIVE` 除外），也不得另設 `PYTHONPATH`。記錄每組的通過數與略過數；略過的測試要說明原因。
   - `dotnet test src/AudioSwitch.sln -c Release`，全部通過。
   - `probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .`，全部通過。
   - 階段 2 起：先執行 `probes/.venv/Scripts/python.exe probes/audio_topology.py json live-compare` 重新產生比對檔，再以 `AUDIOSWITCH_LIVE=1` 執行 `dotnet test`。不得使用先前留下的 `live-compare.json`。
   - 本階段若改動探針的輸出格式或欄位（`snapshot`、`json`、`tree` 等），所有讀取這些輸出的地方都要重跑並列入報告：`probes/export_golden.py`（比對 golden SHA-256）、Core golden 測試、實機比對測試、`probes/compare_state.py`。
4. **需求追蹤**：對本階段涉及的每個 R 項目，列出實作位置（`檔案:行號`）、對應測試名稱與證據。沒有測試的項目要說明為何只能人工驗收。本階段新增或修改的平台類別，確認都實作了 Core 對應的介面（由階段 3a 新增的介面對應測試把關）；改動待實作清單時在報告說明。
5. **禁止事項掃描**（每項逐筆檢視結果，不是只看筆數）：
   - `rg -n "DllImport|LibraryImport|ComImport|Microsoft\.Win32|NAudio|System\.Windows|UseWPF|UseWindowsForms" src/AudioSwitch.Core src/AudioSwitch.Presentation` → 應為零筆。
   - `rg -n "DllImport|LibraryImport|ComImport|NAudio" src/AudioSwitch.App` → 應為零筆（平台呼叫一律經 Platform.Windows）。
   - `rg -n -i "303A|realtek|speakers \(|扬声器|S2719" src` → 應為零筆（fixture 與測試不在 `src/`）。
   - `rg -n -i "PhysicalDeviceLocation|pld" src/AudioSwitch.Core` → 只允許出現在模型與詳細資訊格式化，不得出現在分類或樹狀邏輯。
   - `rg -n "Registry\.|RegSetValue|CreateSubKey|OpenPropertyStore|SetDefaultEndpoint|AudioPolicyConfig" src` → 每筆都必須落在全域限制 7 允許的類別中。
   - `rg -n "Thread\.Sleep|async void|\.Result\b|\.Wait\(\)" src` → 除事件處理常式的 `async void` 外，每筆都要有理由。
6. **差異審查**：完整閱讀 `git diff <階段起點>..HEAD`（含未 commit 變更），檢查：
   - 行為與規格一致，沒有規格外的功能或預設值。
   - 錯誤處理：平台呼叫失敗有記錄並回報；沒有吞掉例外。
   - 執行緒：COM 回呼不直接碰 UI；UI 更新在 UI 執行緒。
   - 資源：COM 物件、事件訂閱、快捷鍵、計時器在結束時釋放。
   - 介面文字為繁體中文，識別字為英文；沒有殘留的除錯輸出或未說明的 TODO。
7. **系統狀態**：有跑寫入類 live 測試的階段，附上 `compare_state.py` 的比對結果。
8. **審查報告**：寫入 `.superpowers/reviews/phase-<n>.md`，包含：執行的指令與結果摘要、需求追蹤表、掃描結果與逐筆判斷、發現的問題與修正、剩餘風險、需要使用者驗收的項目。
9. **修正後重審**：審查中發現任何問題，修正後從第 1 步重新執行。報告中的結論只能根據最後一次的結果。

## 停止條件（停下回報，不要繞過）

- .NET 10 SDK 安裝失敗，或需要安裝 .NET 10 SDK 以外的任何軟體。
- 需要直接引用「環境前置」表格以外的 NuGet 套件。
- 寫入類 live 測試無法還原，或 `compare_state.py` 顯示前後不一致。
- 未公開介面（`IPolicyConfig`）在本機回傳失敗。
- 規格之間互相矛盾、規格沒有涵蓋某個必要行為，或需要選定待決事項的預設值。
- 需要實體操作才能驗證的項目（插拔、Dock、HDMI 螢幕、Teams）：列入使用者驗收清單，不要模擬結果或標示為已驗證。
- `probes/out/` 缺少 golden 測試需要的 snapshot。

## 完成條件

六個階段的審查報告都存在且結論為通過；全部自動測試與 live 測試通過；狀態比對一致；文件已更新為現狀；使用者驗收清單已交給使用者。使用者驗收結果由使用者回報後另行處理。
