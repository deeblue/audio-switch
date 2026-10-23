# audio-switch

在 Windows 上以本機實體接口為單位，快速辨認並切換預設的播放與錄音裝置。Windows 的設定藏得太深（設定 → 系統 → 音效 → 輸出／輸入），而且多個端點常顯示相同的驅動或型號名稱，單看名稱無法知道聲音會從哪裡出來。

## 為什麼做

開發 7C（ESP32-S3-Touch-LCD-7C）時，電腦上的聲音裝置常常在換：

- 7C 的副螢幕韌體（`AI-Vibe-Hub/vibe-console`，分支 `exp/second-screen`）讓 7C 成為電腦的一個喇叭，Windows 裡叫「speaker」。
- 7C 原本的韌體讓 7C 成為一支麥克風，名稱含「Vibe audio」。
- 測試時要在 7C、筆電內建、耳機、螢幕與 Dock 之間來回切換。

需要區分的實際連接情境：機身 3.5mm 耳麥、USB 耳麥、一個以上的 HDMI／USB-C 螢幕喇叭、筆電內建麥克風、筆電內建喇叭，以及 Dock 的 3.5mm 插孔與 Dock 上的螢幕。耳麥可能同時提供播放與錄音端點；相同型號的螢幕可能同名，且可能一台接在 Dock、一台接在筆電 HDMI。

## 要做到的

1. **以機身接口為根呈現**：根節點是本機實體接口（USB-A、USB-C／Thunderbolt、HDMI、3.5mm）與「內建」，其下依序是 Dock／hub、外接設備或螢幕，最後是播放與錄音端點。端點仍是實際切換的單位。新接上的設備不需預先登記，即可出現在對應接口下；無法對應到接口的端點放在「其他／未辨識」。別名是選用的個人標籤，不是使用前提。呈現細節見「呈現方式」。
2. **系統匣圖示**：點一下列出所有目前可用的播放與錄音端點，各項附接口路徑，目前的預設打勾，點哪個就設成預設；同名端點仍須各自列出。
3. **全域快捷鍵**：在目前可用的端點間輪流切換（播放、錄音各自處理），跳過不可用端點；若提供自訂循環清單，新增裝置仍須能直接從系統匣選取，不要求先加入清單。
4. **跟著插拔更新**：端點新增、移除或狀態變更時更新清單與快捷鍵候選；辨認不明時保留各端點供選取，不憑同名猜測實體位置。
5. （後續範圍）指定的端點一插上就自動切過去，例如 7C 的 speaker；規則尚未決定。

不做：每個應用程式各自的輸出裝置（Windows 的「應用程式音量與裝置喜好設定」）、音量混音器、音效處理。

## 呈現方式

Windows 多半能列出所需資料；難處在於現有的排列方式讓人無法立刻找到對的端點。呈現原則：

1. **根節點用機身接口**，依實體位置命名與排序，例如「左側 USB-C 1」「右側 HDMI」，不用 bus 或驅動名稱當根。另有「內建」「藍牙／無線」「虛擬」「未辨識」等非接口根節點。
2. **以接口合併不同來源**：同一個 USB-C 孔上的 Dock，其 USB 樹（Dock 3.5mm、Dock USB 孔上的裝置）與顯示輸出（Dock 上的螢幕）併在同一個 Dock 節點下；螢幕不另列在獨立的「顯示器」根下。
3. **只顯示使用者認得的節點**：Dock、螢幕、3.5mm 孔、USB 裝置。hub、composite、音訊介面等中間節點收在詳細資訊。
4. **螢幕以使用者看得到的資訊標示**：型號、Windows 螢幕編號、排列位置（左／右／主螢幕），並提供「識別」在該螢幕上顯示大字。播放端點可試音，錄音端點顯示輸入峰值。
5. **葉節點是可切換的端點**：目前預設打勾，點選即切換。同名端點以所在位置區分，必要時附短 ID。
6. **標記來源**：每條接口關聯標記 OEM 資料、Windows 屬性、實際校準或未知。資料不足時顯示「未辨識」，不以名稱猜測。
7. **系統匣用平面清單加路徑**，例如「左 USB-C › Dock › 螢幕 2 Dell（右）」；完整樹狀圖在主視窗。

範例：左側 USB-C 接 Dock，Dock 上接一台螢幕與 USB speaker；筆電 HDMI 另接同型號螢幕。

```text
ThinkPad T14p
├─ 左側 USB-C 1
│  └─ Dock
│     ├─ 🖥 螢幕 2 · Dell U2720Q（右）      [識別]
│     │   └─ 🔊 Dell U2720Q
│     ├─ 🎧 Dock 3.5mm
│     │   ├─ 🔊 耳機 ✓
│     │   └─ 🎤 麥克風
│     └─ USB port 3
│        └─ 🔊 USB speaker                    [試音]
├─ 右側 HDMI
│  └─ 🖥 螢幕 3 · Dell U2720Q（左）      [識別]
│     └─ 🔊 Dell U2720Q
├─ 右側 3.5mm
│  └─ 🎧 耳機（已插入）
└─ 內建
   ├─ 🔊 喇叭
   └─ 🎤 麥克風陣列
```

兩台同名螢幕分別掛在 Dock 與筆電 HDMI 下，並附螢幕編號與排列位置。「螢幕在 Dock 上」由顯示路徑與 Dock 落在同一個 USB-C 孔推得。Dock 內部常把 DisplayPort 轉成 HDMI，Windows 通常只看得到 DisplayPort 與 MST 分支，因此不標示「Dock 的 HDMI 孔」等無法確認的 Dock 輸出孔，改以型號、位置與「識別」辨認。

## 可行性與限制

Windows Core Audio 的公開 API 可以列舉播放／錄音端點、讀取目前預設、監看裝置狀態，也可以讀取顯示名稱、端點 ID、外形類型與部分 PnP／插孔資訊。試音可直接開啟指定播放端點的音訊串流；錄音端點可讀取音量峰值。**這些資訊是辨認線索，不保證能還原使用者眼中的實體設備。**

| 情境 | 可能呈現的連接路徑 | 不能預設的事 |
|---|---|---|
| 機身 3.5mm 耳麥、筆電內建喇叭 | 內建音訊轉接器 → 驅動暴露的端點或實體插孔 | 3.5mm 插入可能只改變同一個端點／插孔狀態；耳麥與內建喇叭不一定是獨立端點，也不能讀出耳麥品牌。 |
| USB 耳麥、會議室 USB speaker | USB 控制器 → hub → USB 裝置 → 播放與錄音端點 | hub 或裝置父子鏈可能不提供使用者熟悉的產品名稱；播放與錄音端點不能只因名稱相同就自動配成一組。 |
| 多個 HDMI／USB-C 螢幕喇叭 | Intel Display Audio／GPU 音訊 → 端點；另由顯示路徑 → 顯示 connector → 機身接口 | 顯示器音訊不在 USB 樹下，經 Dock 時也一樣；Dock 上的具體輸出孔通常無法判斷。 |
| 筆電內建麥克風 | 內建音訊介面／DSP → 錄音端點 | 「Realtek」或「Intel」等元件名稱不一定代表一個可單獨辨認的實體麥克風。 |
| Dock 的 3.5mm 插孔 | USB-C／Thunderbolt → Dock 內部 hub → Dock USB 音效晶片 → 端點 | 許多 USB 音效晶片不回報插入偵測，端點可能一直存在；插入耳麥不一定有任何可見變化。 |

PnP 的 parent／instance ID 提供裝置樹資訊；`PKEY_Device_ContainerId` 可協助將屬於同一裝置容器的節點歸組；傳統音訊轉接器的實體插孔資訊可能由 `IKsJackDescription` 暴露。這些來源涵蓋範圍不同，不可把 PnP 父節點直接當成物理線材路徑，也不可因端點被歸在同一容器就推斷一定是同一個可插拔產品。[Windows 裝置父節點](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/determining-the-parent-of-a-device)、[Core Audio 端點屬性](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties)、[插孔描述](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/jack-description-property)、[音訊端點與轉接器關係](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-endpoint-devices)

裝置清單以 Windows 當下列出的端點為準，不依賴硬編碼型號或預先建立的實體設備清冊。優先使用 Windows 11 24H2（組建 26100）起可能提供的 `PKEY_AudioEndpoint_StableId`；若沒有，退回端點 ID 作為端點識別。StableId 可能缺失或改變，端點 ID 則可能在系統或驅動更新後改變。使用者可選擇保存別名；重新找到端點時只在識別結果唯一且可靠時沿用，否則仍以即時端點資訊顯示，不能因別名失配而阻擋切換。程式只能切換 Windows 實際列出的端點，不能把驅動合併的喇叭與耳機拆開。

切換系統預設端點的實作需要未公開的 Windows COM 介面，可能受系統更新影響。已在播放的應用程式是否立刻跟著切換，取決於該程式的音訊路由方式，須以目標程式實測。

參考：[端點屬性](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties)、[插孔資訊](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/jack-description-property)、[端點狀態](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-state-xxx-constants)、[端點 ID](https://learn.microsoft.com/en-us/windows/desktop/CoreAudio/endpoint-id-strings)、[StableId](https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-audioendpoint-stableid)、[指定端點音訊串流](https://learn.microsoft.com/en-us/windows/win32/coreaudio/stream-management)、[錄音峰值](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudiometerinformation)。

## 端點來源分類與接口對應

每個可用端點歸入下列四類之一，每類只用一套方法對應到根節點；Dock 不需要專門的判斷規則。

| 類別 | 範例 | 判斷方式 | 根節點 |
|---|---|---|---|
| A. USB 音訊 | 7C、USB 耳麥、USB speaker、Dock 3.5mm | 從端點上層的 USB 音訊介面沿 parent 往上，取 `DEVPKEY_Device_LocationPaths`，與接口校準表做最長前綴比對 | 機身 USB-A／USB-C |
| B. 顯示器音訊 | HDMI、USB-C DisplayPort、Dock 上的螢幕 | `IKsJackSinkInformation` 取得螢幕名稱、ManufacturerId／ProductId 與 PortId，對應 DisplayConfig 的顯示路徑與 connector | 機身 HDMI／USB-C |
| C. 主機板內建音效 | 內建喇叭、麥克風、機身 3.5mm | DeviceTopology connector 的 `IKsJackDescription`：連接器類型、位置、插入偵測 | 內建／機身 3.5mm |
| D. 其他 | 藍牙、虛擬音效裝置 | 不判斷實體位置 | 藍牙／無線、虛擬、未辨識 |

**A 類**：Dock、外接 hub 或直插只差在路徑長短，中間節點保留在詳細資訊。Thunderbolt 3 Dock 常有自己的 USB 控制器，經 PCIe 通道接入；Thunderbolt 4／USB4 Dock 可能走主機控制器的 USB 通道。兩種路徑都應落在某個已校準的機身 USB-C 前綴，須實測確認。Dock 的 hub、網卡與音效晶片通常共用 `PKEY_Device_ContainerId`，據此歸成一個 Dock 節點。Dock 內建音效晶片通常接在不可移除的內部 port，沿用 Dock 的 ContainerId；插在 Dock USB 孔的裝置接在可移除 port，有自己的 ContainerId；據此區分「Dock 3.5mm」與「Dock USB 孔上的裝置」，VID 只作輔助。這項判斷須以測試應用實測。Dock 自身 USB 孔沒有 ACPI 位置資料，不逐款校準 Dock，只顯示路徑上的 port 編號。

**B 類**：顯示器音訊端點掛在 Intel Display Audio 或 GPU 音訊下，不在 USB 樹中，螢幕接在 Dock 上時也一樣。由顯示路徑找到機身接口；若該接口上已有 A 類找到的 Dock，螢幕就併入該 Dock 節點。[IKsJackSinkInformation](https://learn.microsoft.com/en-us/windows/win32/api/devicetopology/nn-devicetopology-iksjacksinkinformation)

**接口校準**只對機身接口做一次；外接設備與端點每次動態掛上：

1. 讀取主機 SMBIOS 的廠牌與機型，依 OEM 手冊建立機身接口節點與數量。這是主機機殼接口圖，不是外接設備清冊；換一台筆電才需要換。
2. USB-A／USB-C：先讀 `DEVPKEY_Device_PhysicalDeviceLocation`（ACPI `_PLD`），若韌體提供面板與位置，可直接得到側別。不足時逐孔插入任意 USB 裝置，以測試應用的差異模式記錄 LocationPaths 前綴，建立「前綴 ↔ 機身孔位」校準表。兩孔無法區分時標為未辨識。[LocationPaths 屬性](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-locationpaths)
3. HDMI／USB-C 顯示輸出：每個接口各直接接一次螢幕，記錄 DisplayConfig connector 與新出現音訊端點的對應。
4. 3.5mm：讀 jack 描述；沒有描述或插入偵測時，以實際插拔／試音確認，仍不能判定就標為未知。[IKsJackDescription](https://learn.microsoft.com/en-us/windows/win32/api/devicetopology/nn-devicetopology-iksjackdescription)、[插孔欄位定義](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksjack-description)

Windows PnP 裝置樹會隨裝置加入／移除更新；USB4 拓樸也可呈現 host router 與下游 device router。[Windows 裝置樹](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/device-tree)、[Windows USB4 連線管理員](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/usb4-intro-to-connection-manager)

**本機基準**：本機 SMBIOS 回報 Lenovo ThinkPad T14p Gen 3，Machine Type 21RU（完整 MTM 為 `21RUZCQXCN`）。規格列出 2 個 Thunderbolt 4／USB-C、2 個 USB-A、1 個 HDMI、1 個 3.5mm 耳機／麥克風複合插孔；側視圖與接口位置以 Lenovo 21RU 使用指南為準。[Lenovo 21RU 使用指南](https://iknow.lenovo.com.cn/detail/428178.html)、[經銷商規格摘要](https://thinkpro.vn/laptop/lenovo-thinkpad-t14p-ai-2025)

## 本機可行性實驗

已用暫存目錄中的 Python 3.12／pycaw 做可丟棄的探針，沒有在此 repo 加入依賴。Windows Core Audio 目前回報 **3 個可用播放端點、2 個可用錄音端點**：

| 方向 | Windows 名稱 | 目前讀到的線索 |
|---|---|---|
| 播放 | `Realtek HD Audio 2nd output (Realtek(R) Audio)` | 獨立端點 ID；上層為內建 Realtek／Intel Audio。現為一般預設播放。 |
| 播放 | `扬声器 (Realtek(R) Audio)` | 另一個端點 ID；與前者同屬 Realtek／Intel Audio 上層，尚未確認各自對應的實體插孔。 |
| 播放 | `Speakers (speaker)` | 獨立端點 ID；PnP 上層為 `USB\VID_303A&PID_2986`，裝置名稱為 `speaker`。VID `303A` 屬 Espressif，與 7C 的 ESP32-S3 相符，但尚未透過拔插確認。 |
| 錄音 | `麦克风 (Realtek(R) Audio)` | 上層為內建 Realtek／Intel Audio。現為一般預設錄音。 |
| 錄音 | `麦克风阵列 (适用于数字麦克风的英特尔® 智音技术)` | 上層為 Intel Digital Microphone；與 Realtek 麥克風分別列出。 |

這已確認「列舉可用端點、取得各自 ID、讀取外形類型與部分 PnP 上層、讀取目前預設」在本機可行。`Realtek HD Audio 2nd output` 的外形類型為耳機，`Speakers (speaker)` 與 Realtek 喇叭為喇叭，兩個錄音端點為麥克風。兩個可用麥克風都能啟用 `IAudioMeterInformation` 並讀取峰值；當時音量接近零，尚未用說話確認哪支麥克風在收音。端點通知 callback 已成功註冊並解除，但未發生插拔事件，因此事件內容與即時更新尚未驗證。

播放切換也做了可回復實驗：先保存 `eConsole` 與 `eMultimedia` 原本的 `Realtek HD Audio 2nd output`，短暫將兩個 role 設成 `Speakers (speaker)`，逐一讀回確認，再恢復原值並讀回確認。兩次確認皆成功；實驗沒有呼叫錄音或 `eCommunications` 的設定方法。這證明未公開的設定介面在本機目前可呼叫，**不代表聲音一定從預期的實體設備播放，也不保證未來 Windows 更新後仍有效**。

**尚未驗證** StableId、插孔拓樸與位置、顯示器音訊與顯示路徑的對應、`_PLD`、Dock 的 ContainerId 分組、USB 耳麥、插拔通知事件、指定端點試音，以及實際收音。

## 實作計畫

**先以測試應用驗證分類與接口對應，再定產品設計。** 以下按依賴順序進行；尚未開始寫產品程式。設計前提是任意新裝置均能即時出現，不要求事先盤點或登錄。

1. **測試應用**：Python 3.12 命令列工具，放在 `probes/`，可重複執行；完整需求見 `probes/TASK.md`。依「端點來源分類」把可用端點歸入 A～D 類，印出「呈現方式」中的樹；`--raw` 輸出每個端點的完整鏈（InstanceId、LocationPaths、ContainerId、可移除與否、`_PLD`、jack 描述、jack sink、DisplayConfig 路徑）；`--diff` 比較插拔前後，列出新增與消失的節點和端點；另輸出 JSON。須回答：
   - jack sink 的 PortId 能否對應到 DisplayConfig 顯示路徑；
   - 兩個 USB-C 孔的顯示 connector 能否區分；
   - 同一孔上 Dock 的 USB 樹與顯示輸出能否正確合併；
   - ContainerId／可移除屬性能否區分 Dock 內建音效與 Dock USB 孔上的裝置；
   - `_PLD` 是否提供機身孔位；
   - StableId 是否存在，以及指定端點試音。
2. **接口校準與代表性設備驗證**：用 `--diff` 逐孔校準機身 USB 與顯示接口，再以手邊的 3.5mm、USB 耳麥、螢幕、Dock、7C 測試新增、移除、重名和通知。紀錄用來驗證動態發現與標示能力，不作為可支援設備的固定清冊。
3. **確定首版行為與技術選擇**：正式版建議 C#／.NET 10 LTS + Windows Forms，可直接整合 Core Audio、CfgMgr32、DisplayConfig、NotifyIcon 與 TreeView，並自包含發佈；實作前需安裝 .NET 10 SDK。另決定快捷鍵、切換哪些 role、別名、開機啟動，以及自動切換是否留到後續。
4. **建立接口拓樸與端點資料**：啟動及通知後重建四類端點與機身接口的關聯，依「呈現方式」產生樹狀資料；別名以端點識別碼保存，識別不確定時不套用。以重名、Dock 與筆電同型號螢幕、端點消失及重新接入案例檢查。
5. **完成辨認與手動切換**：主視窗樹狀圖、系統匣路徑清單、螢幕「識別」、播放試音、錄音峰值、選用別名與播放／錄音快捷鍵。切換後重新讀取 Windows 預設值確認結果；失敗時顯示原因，不把失敗的切換標成成功。
6. **處理插拔並驗收**：收到端點變更通知後更新清單與快捷鍵候選，跳過不可用端點。另記錄 YouTube 等已播放程式是否跟著切換。若 Windows 只提供共用端點，記錄該限制，不把它算作程式故障。

指定裝置插入後自動切換屬後續階段；若決定納入，須另外定義優先順序、拔除後回復及使用者手動改選時的行為。

## 現成工具評估

SoundSwitch 能以系統匣與快捷鍵切換端點，裝置選單是 Windows 名稱的平面清單，沒有接口位置。其同名螢幕只列出一台的問題（[#338](https://github.com/Belphemur/SoundSwitch/issues/338)）已在 v4.15.1 修正。Windows 聲音控制台可替端點改名以區分同名端點，但同樣不提供接口位置，且驅動重新安裝後可能重設。兩者都不提供以機身接口為單位的呈現。[選單原始碼](https://github.com/Belphemur/SoundSwitch/blob/v7.3.3/SoundSwitch/Util/ToolStripDeviceItem.cs)

其他已知工具如下，尚未逐一檢查是否提供接口位置：

| 工具 | 說明 |
|---|---|
| [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | 開源，系統匣的音量混音器，也能切換預設裝置 |
| [AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets) | PowerShell 模組，用指令切換，可以綁在捷徑上 |
| NirSoft `nircmd setdefaultsounddevice` | 命令列 |

自行實作的主要理由是以機身接口為單位呈現端點，讓同名與多端點環境下能立刻找到對的裝置；後續也可加入依 7C 插拔自動切換或與 Vibe Console 整合。

## 狀態

剛開始，只有這份 README 與 `CLAUDE.md`，還沒有程式。
