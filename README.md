# audio-switch

在 Windows 上以本機實體接口為單位，快速辨認並切換預設的播放與錄音裝置。Windows 的設定藏得太深（設定 → 系統 → 音效 → 輸出／輸入），而且多個端點常顯示相同的驅動或型號名稱，單看名稱無法知道聲音會從哪裡出來。

## 為什麼做

開發 7C（ESP32-S3-Touch-LCD-7C）時，電腦上的聲音裝置常常在換：

- 7C 的副螢幕韌體（`AI-Vibe-Hub/vibe-console`，分支 `exp/second-screen`）讓 7C 成為電腦的一個喇叭，Windows 裡叫「speaker」。
- 7C 原本的韌體讓 7C 成為一支麥克風，名稱含「Vibe audio」。
- 測試時要在 7C、筆電內建、耳機、螢幕與 Dock 之間來回切換。

需要區分的實際連接情境：機身 3.5mm 耳麥、USB 耳麥、一個以上的 HDMI／USB-C 螢幕喇叭、筆電內建麥克風、筆電內建喇叭，以及 Dock 的 3.5mm 插孔與 Dock 上的螢幕。耳麥可能同時提供播放與錄音端點；相同型號的螢幕可能同名，且可能一台接在 Dock、一台接在筆電 HDMI。

## 要做到的

1. **以機身接口為根呈現**：根節點是本機實體接口（USB-A、USB-C／Thunderbolt、HDMI、3.5mm）與「內建」，其下依序是 Dock／hub、外接設備或螢幕，最後是播放與錄音端點。端點仍是實際切換的單位。新接上的設備不需預先登記，即可出現在對應接口下；無法對應到接口的端點放在「其他／未辨識」。別名是選用的標籤，寫回 Windows 端點名稱，讓其他程式的裝置選單也看得到；不是使用前提。呈現細節見「呈現方式」。
2. **系統匣快速切換面板**：點一下列出所有目前可用的播放與錄音端點，依接口分組，目前的預設打勾，點哪個就設成預設（一般預設與通訊預設一起切換，所有跟隨系統預設的程式都會改走該端點）；同名端點仍須各自列出。
3. **全域快捷鍵**：在目前可用的端點間輪流切換（播放、錄音各自處理），跳過不可用端點；若提供自訂循環清單，新增裝置仍須能直接從系統匣選取，不要求先加入清單。
4. **跟著插拔更新**：端點新增、移除或狀態變更時更新清單與快捷鍵候選；辨認不明時保留各端點供選取，不憑同名猜測實體位置。
5. （後續範圍）指定的端點一插上就自動切過去，例如 7C 的 speaker；規則尚未決定。
6. （後續範圍，進階設定）個別程式指定播放／錄音端點，與 Windows「音量混音程式」中的應用程式裝置設定相同。被指定的程式不跟隨第 2、3 項的切換，因此系統匣須標示哪些程式有個別指定，並提供「改回跟隨預設」與「全部清除」。

不做：音量混音器、音效處理。

## 呈現方式

Windows 多半能列出所需資料；難處在於現有的排列方式讓人無法立刻找到對的端點。呈現原則：

1. **根節點用機身接口**，依使用者在「接口定義」設定的位置排序與命名，例如「左側 USB-C（前）」「右側 HDMI」，不用 bus 或驅動名稱當根。另有「內建」「藍牙／無線」「虛擬」「未辨識」等非接口根節點。
2. **以接口合併不同來源**：同一個 USB-C 孔上的 Dock，其 USB 樹（Dock 3.5mm、Dock USB 孔上的裝置）與顯示輸出（Dock 上的螢幕）併在同一個 Dock 節點下；螢幕不另列在獨立的「顯示器」根下。
3. **只顯示使用者認得的節點**：Dock、螢幕、3.5mm 孔、USB 裝置。hub、composite、音訊介面等中間節點收在詳細資訊。
4. **螢幕以使用者看得到的資訊標示**：型號、Windows 螢幕編號、排列位置（左／右／主螢幕），並提供「識別」在該螢幕上顯示大字。播放端點可試音，錄音端點顯示輸入峰值。
5. **葉節點是可切換的端點**：目前預設打勾，點選即切換。同名端點以所在位置區分，必要時附短 ID。
6. **標記來源**：每條接口關聯標記 Windows 屬性、使用者校準或未知。資料不足時顯示「未辨識」，不以名稱猜測。
7. **快速切換面板依接口分組**，只到端點層；含 Dock、Hub 細節的完整樹在設定視窗。見「介面」。

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

## 介面

分成兩種：每天使用的**快速切換面板**，與偶爾使用的**設定視窗**。

### 快速切換面板

左鍵點系統匣圖示彈出，點面板外即關閉；右鍵選單只有設定、識別螢幕、結束。

- 分「播放」「錄音」兩頁，依接口分組列出目前可用端點；有別名顯示別名，否則顯示 Windows 名稱。
- 點一下即切換（三個 role 一起），目前預設打勾。
- 播放端點旁有「試音」，錄音端點旁顯示即時峰值，用來確認就是這個裝置。
- 不放設定項目，只在角落放開啟設定視窗的齒輪。

### 設定視窗

| 分頁 | 內容 |
|---|---|
| 裝置 | 完整接口樹（含 Dock、Hub、螢幕）與端點細節；別名、試音、錄音峰值、識別螢幕。選取端點時，接口示意圖上對應的孔會標亮。 |
| 接口定義 | 筆電、Dock、Hub 的接口示意圖與校準 |
| 快捷鍵 | 組合鍵與循環方式 |
| 一般 | 開機自動啟動 |
| 個別程式（後續範圍） | 個別程式指定的端點、改回跟隨預設、全部清除 |
| 自動切換（後續範圍） | 規則 |

### 接口定義

接口的位置與名稱全部由使用者決定；程式不依機型、規格資料或 `_PLD` 推測孔位與類型。程式只透過插入裝置取得「這個孔對應哪些 LocationPaths」的證據。接口表每台電腦各自一份，主機型號不符時不套用。

**筆電示意圖**：筆電俯視圖，四邊為左、右、後（轉軸）、前（使用者側）。使用者從工具列把 USB-A、USB-C、HDMI、3.5mm 圖示拖到邊上，擺放位置即決定側別與同側順序，可沿邊拖動調整或移到別的邊。圖示上標示校準狀態：已校準、缺部分路徑（例如缺 USB 3）、未校準。點圖示可改名、重新校準、刪除。另提供可用鍵盤操作的清單，功能相同。

校準有兩種順序：

- **先擺再插**：點圖示 →「校準」→ 插入任一裝置。
- **先插再擺**：插入裝置後提示把它拖到圖上的位置（既有圖示或從工具列拖新的）。

偵測方式同探針的 `calibrate`：取這次新出現或移動的最上層接點，一個接口最多記 USB 2、USB 3、PCIe 隧道各一組；同類接點出現兩個時請使用者只插一個裝置重試。顯示輸出以接上螢幕的方式校準。3.5mm 擺在圖上決定位置，對應關係由 KS jack 資訊自動取得，不需插入校準。內建喇叭與麥克風不出現在圖上。

**名稱**：擺好圖示後名稱欄立即可編輯，預填依位置產生的名稱（例如「左側 USB-C（前）」）並全選，直接輸入即取代，按 Enter 沿用。接口名稱只存在程式設定檔。若校準時插入的裝置有音訊端點，可選填裝置別名（寫回 Windows 端點，見「可行性與限制」）。接口名稱與裝置別名分開保存，因為裝置會換孔；面板上組合顯示。

**Dock 與 Hub 示意圖**：Dock 為前後兩面的盒狀圖，Hub 為長條圖，擺放、命名、校準方式與筆電相同。孔位記錄為相對於 Dock／Hub 接點的路徑後段，因此 Dock 換插到筆電另一個孔或另一台筆電時沿用同一份定義（待 Dock 實測）。程式以 Dock／Hub 內部 hub 晶片的 VID/PID（必要時加序號）辨認使用者定義過的那台；同型號共用定義。Dock 內建 3.5mm 一直存在、無法以插拔偵測，改為在該 Dock 底下的裝置中點選指定。Dock 的 HDMI／DP 輸出孔能否分辨待 Dock 實測；無法分辨時只以螢幕編號與排列位置標示。Hub 接在 Dock 孔上時，樹狀依實際連接逐層展開。未定義的 Dock／Hub 顯示為「Dock／Hub（未定義）」，其下裝置仍各自列出。

### 未設定時

初次安裝、未定義任何接口時仍可完整切換，只是 USB 裝置不依機身孔位分組：

```text
3.5mm 插孔                  ← KS jack 自動判斷，不含側別
├─ Realtek HD Audio 2nd output
└─ 麦克风
內建                        ← KS jack 自動判斷
├─ 扬声器
└─ 麦克风阵列
USB（未設定接口）
└─ speaker                  ← 同一 USB 裝置的播放、錄音、螢幕仍收在一起
   ├─ Speakers (speaker)
   └─ 🖥 S2719DGF
螢幕
└─ 型號（螢幕 2）
藍牙
└─ 裝置名稱
```

- USB 裝置只依裝置分組，不顯示 `USB(6)` 等路徑編號，也不用 `_PLD` 猜側別；兩台同款裝置分成兩個節點，以「試音」分辨。
- Dock／Hub 顯示為「Dock／Hub（未定義）」。
- 試音、錄音峰值、識別螢幕、插拔更新、別名都可用。
- 第一次啟動顯示一次通知，說明可在設定中定義接口；「USB（未設定接口）」群組旁有「設定接口…」連結。部分接口已定義時，已定義的依位置顯示，其餘仍在「USB（未設定接口）」。

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

裝置清單以 Windows 當下列出的端點為準，不依賴硬編碼型號或預先建立的實體設備清冊。優先使用 Windows 11 24H2（組建 26100）起可能提供的 `PKEY_AudioEndpoint_StableId`；若沒有，退回端點 ID 作為端點識別。StableId 可能缺失或改變，端點 ID 則可能在系統或驅動更新後改變。別名寫入端點的 `PKEY_Device_DeviceDesc`，由 Windows 依端點保存；Windows 顯示名稱為「別名 (介面卡名稱)」，Windows 設定、Teams、Zoom 都讀這個名稱。同一實體設備的播放與錄音端點寫入同一別名。程式另存「端點 ID → 原始名稱」，供清除別名時還原。Windows 列出新端點時（例如沒有序號的 USB 裝置換孔）顯示該端點自己的名稱，程式不推測沿用其他端點的別名；別名不得阻擋切換。寫入端點屬性不需系統管理員權限，但 Microsoft 文件註明用戶端不應設定這些屬性，沒有相容性保證。程式只能切換 Windows 實際列出的端點，不能把驅動合併的喇叭與耳機拆開。

切換系統預設端點的實作需要未公開的 Windows COM 介面，可能受系統更新影響。已在播放的應用程式是否立刻跟著切換，取決於該程式的音訊路由方式，須以目標程式實測。Teams 跟隨系統預設，通話中切換 Windows 預設播放與錄音端點時立即跟著換；Zoom 的「與系統相同」選項跟隨系統預設。兩者跟隨的是一般預設或通訊預設尚未驗證。

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

**B 類**：一般顯示器音訊端點掛在 Intel Display Audio 或 GPU 音訊下；由 KS sink 的 PortId 嘗試對應 DisplayConfig adapter／target，再依校準找到機身接口。USB 顯示裝置是例外：若顯示 adapter device path 與 USB 音訊端點的 PnP USB devnode 相符，兩者歸在同一 USB 裝置節點及接口下；關聯須由共同 PnP 身分證據成立，不能只憑名稱或 VID/PID。傳統 GPU 顯示路徑仍需 sink 與 connector 校準才能併入 Dock。[IKsJackSinkInformation](https://learn.microsoft.com/en-us/windows/win32/api/devicetopology/nn-devicetopology-iksjacksinkinformation)

**接口校準**對機身接口與使用者定義的 Dock／Hub 各做一次；端點每次動態掛上。操作介面見「介面」的「接口定義」：

1. 使用者在示意圖上建立接口並決定位置與名稱。程式不依機型、OEM 手冊或 `_PLD` 建立接口；接口表綁定這台主機的 SMBIOS 廠牌與機型，換一台筆電需重新定義。
2. USB-A／USB-C：逐孔插入任意 USB 裝置，記錄 LocationPaths 前綴，建立「前綴 ↔ 機身孔位」校準表；校準表是孔位的唯一依據。`DEVPKEY_Device_PhysicalDeviceLocation`（ACPI `_PLD`）只在詳細資訊顯示，不用於判斷孔位或類型：韌體填寫的面板值不一定正確（本機兩個 USB-C 孔在左側，`_PLD` 卻回報面板「上」）。兩孔無法區分時標為未辨識。[LocationPaths 屬性](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-locationpaths)
3. HDMI／USB-C 顯示輸出：每個接口各直接接一次螢幕，記錄 DisplayConfig connector 與新出現音訊端點的對應。
4. 3.5mm：讀 jack 描述；沒有描述或插入偵測時，以實際插拔／試音確認，仍不能判定就標為未知。[IKsJackDescription](https://learn.microsoft.com/en-us/windows/win32/api/devicetopology/nn-devicetopology-iksjackdescription)、[插孔欄位定義](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksjack-description)

Windows PnP 裝置樹會隨裝置加入／移除更新；USB4 拓樸也可呈現 host router 與下游 device router。[Windows 裝置樹](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/device-tree)、[Windows USB4 連線管理員](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/usb4-intro-to-connection-manager)

**本機基準**：本機 SMBIOS 回報 Lenovo ThinkPad T14p Gen 3，Machine Type 21RU（完整 MTM 為 `21RUZCQXCN`）。規格列出 2 個 Thunderbolt 4／USB-C、2 個 USB-A、1 個 HDMI、1 個 3.5mm 耳機／麥克風複合插孔；側視圖與接口位置以 Lenovo 21RU 使用指南為準。[Lenovo 21RU 使用指南](https://iknow.lenovo.com.cn/detail/428178.html)、[經銷商規格摘要](https://thinkpro.vn/laptop/lenovo-thinkpad-t14p-ai-2025)

## 本機可行性實驗

本 repo 的唯讀 Python 3.12 探針支援 Core Audio、CfgMgr32、DisplayConfig 與 KS jack/sink 查詢，操作方式見 [`probes/README.md`](probes/README.md)。最近拔除裝置後仍列出 **3 個作用中播放端點、2 個作用中錄音端點**。同名或同驅動端點仍以獨立 endpoint ID 保留：

| 方向 | Windows 名稱 | 目前讀到的線索 |
|---|---|---|
| 播放 | `Realtek HD Audio 2nd output (Realtek(R) Audio)` | 獨立端點 ID；上層為內建 Realtek／Intel Audio。現為一般預設播放。 |
| 播放 | `扬声器 (Realtek(R) Audio)` | 另一個端點 ID；與前者同屬 Realtek／Intel Audio 上層，尚未確認各自對應的實體插孔。 |
| 播放 | `Speakers (speaker)` | 7C 接入時出現、拔除時消失；USB PnP 節點為 `USB\VID_303A&PID_2986`，S2719DGF 顯示 adapter path 也含此裝置識別字。 |
| 錄音 | `麦克风 (Realtek(R) Audio)` | 上層為內建 Realtek／Intel Audio。現為一般預設錄音。 |
| 錄音 | `麦克风阵列 (适用于数字麦克风的英特尔® 智音技术)` | 上層為 Intel Digital Microphone；與 Realtek 麥克風分別列出。 |

拔插觀察確認 7C 的 `Speakers (speaker)`、USB PnP 分支與 S2719DGF active display path 會共同出現／消失；7C（USB 2）逐孔插入的結果如下，四孔皆已寫入 `probes/ports.json` 校準，`tree` 會把 7C 歸到實際所在的接口下：

| 機身接口 | LocationPaths | ACPI | `_PLD` 面板／水平／垂直 | 形狀 | 群組位置 |
|---|---|---|---|---|---|
| 左側 USB-A | `USB(6)` | `HS06` | 左／中／中 | 橫向矩形 | 4 |
| 右側 USB-A | `USB(7)` | `HS07` | 右／右／中 | 橫向矩形 | 3 |
| 左側前方 USB-C | `USB(1)` | `HS01` | 上／左／上 | 橢圓（8×3） | 2 |
| 左側後方 USB-C | `USB(5)` | `HS05` | 上／左／上 | 橢圓（8×3） | 1 |

LocationPaths 前綴共同為 `PCIROOT(0)#PCI(1400)#USBROOT(0)#`。本機 USB 有三組上游：PCH xHCI `PCI(1400)`（`XHCI`，四孔的 USB 2 與 USB-A 的 USB 3）、TCSS xHCI `PCI(0D00)`（`TXHC`，USB-C 的 USB 3）、兩個 USB4 Host Router `PCI(0D02)`／`PCI(0D03)`（`TDM0`／`TDM1`）。同一個 USB-C 孔可同時帶出 USB 2、USB 3 與 USB4 隧道路徑，Dock 上的音訊裝置走哪一條取決於 Dock 晶片與協商模式，須以 Dock 實測校準。USB-A 的 `_PLD` 面板與實際側別相符；USB-C 的面板回報「上」，與實際（左側）不符，形狀與尺寸則符合 USB-C。四孔群組代號都是 0，群組位置 1～4 各不相同，但前後兩個 USB-C 的 `_PLD` 其餘欄位相同，無法只靠面板與位置分辨。DisplayConfig path ID、adapter LUID 與 `\\.\DISPLAYn` 會在重新連線後改變，不能作為校準鍵；校準改用 adapter device path、target ID、connector instance 與 output technology。

機身 3.5mm 耳麥拔除時，`Realtek HD Audio 2nd output` 與 `麦克风 (Realtek(R) Audio)` 消失，重插後恢復；`扬声器 (Realtek(R) Audio)` 與 `麦克风阵列` 不受影響。五個作用中端點 StableId 均為空。`IKsJackDescription` 在五個作用中端點皆可讀取：`2nd output` 與 `麦克风 (Realtek)` 為 3.5mm 插孔且支援插入偵測，`扬声器` 與 `麦克风阵列` 為內建裝置，與插拔結果一致；探針據此把前兩者歸到機身 3.5mm、後兩者歸到內建。3.5mm 插孔的 GeoLocation 回報為 Rear，與實際側別無關，不採用。7C 的喇叭與 S2719DGF 顯示路徑在 `tree` 中併在同一個 USB 裝置節點下。探針唯讀。

播放切換也做了可回復實驗：先保存 `eConsole` 與 `eMultimedia` 原本的 `Realtek HD Audio 2nd output`，短暫將兩個 role 設成 `Speakers (speaker)`，逐一讀回確認，再恢復原值並讀回確認。兩次確認皆成功；實驗沒有呼叫錄音或 `eCommunications` 的設定方法。這證明未公開的設定介面在本機目前可呼叫，**不代表聲音一定從預期的實體設備播放，也不保證未來 Windows 更新後仍有效**。

端點改名實驗（以一般使用者權限、未提升）：以 `READWRITE` 開啟端點屬性，寫入 `PKEY_Device_DeviceDesc` 並 `Commit` 成功，`PKEY_Device_FriendlyName` 隨之變為「新值 (Realtek(R) Audio)」，`Get-PnpDevice` 另一程序讀到新名稱。Windows 設定與 Zoom 立即顯示新名稱；Teams 在 3.5mm 耳麥重新插拔後顯示新名稱，內建喇叭須完整結束並重開 Teams 才更新。測試後三個端點皆已還原原名。

個別程式指定實驗（一般使用者權限）：以 `RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig")` 取得 21H2 後的介面（IID `ab3d4648-e242-459f-b02f-541c70306324`，vtable 配置依 EarTrumpet），對 Chrome 音訊程序呼叫 `SetPersistedDefaultAudioEndpoint` 設定 `eConsole` 與 `eMultimedia` 的播放端點為內建喇叭。Chrome 立即改從喇叭出聲，Firefox 仍走系統預設（3.5mm 耳機）；切換 Windows 預設時 Chrome 維持在喇叭、Firefox 跟著切換；Windows「音量混音程式」同步顯示 Chrome 的指定。Chrome 音訊程序重啟換了 PID 後新程序仍沿用指定，表示 Windows 依應用程式保存；設定與清除都須傳入該程式目前執行中的 PID，程序已結束時回傳 `E_INVALIDARG`。傳入空字串即清除，測試後已清除。介面未公開，21H2 前後 IID 不同。[EarTrumpet 實作](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/DataModel/WindowsAudio/Internal/AudioPolicyConfigService.cs)

**尚未驗證**：錄音端點的個別程式指定、沒有序號的 USB 裝置換孔是否產生新端點、裝置插入當下寫入別名時 Teams 是否直接顯示新名稱、KS sink 與 PortId 對 DisplayConfig 的匹配（本機目前沒有 HDMI／DisplayPort 螢幕音訊端點）、HDMI 與兩個 USB-C 的逐孔 connector 校準、Dock 內建音效與 Dock USB 裝置分組、USB-C 的 USB 3 與 USB4 隧道路徑（目前只校準四孔的 USB 2 路徑，待 Dock 實測）、USB 耳麥、通知事件、指定端點試音及實際收音。`ports.json` 中四個 USB 孔的位置來自使用者實測；HDMI 與 3.5mm 的位置仍標為未知。

## 實作計畫

**先以測試應用驗證分類與接口對應，再定產品設計。** 唯讀探針已實作（計畫見 [`2026-10-23-audio-topology-probe.md`](docs/superpowers/plans/2026-10-23-audio-topology-probe.md)），是正式版的參考實作；Windows 正式版計畫見 [`2026-10-25-audio-switch-windows-app.md`](docs/superpowers/plans/2026-10-25-audio-switch-windows-app.md)，使用者插拔步驟見[`probes/README.md`](probes/README.md)。設計前提是任意新裝置均能即時出現，不要求事先盤點或登錄。

1. **測試應用**：Python 3.12 命令列工具，放在 `probes/`，可重複執行；完整需求與 CLI 子命令見 `probes/TASK.md`。`tree` 依「端點來源分類」把可用端點歸入 A～D 類並印出「呈現方式」中的樹；`raw` 輸出每個端點的完整鏈（InstanceId、LocationPaths、ContainerId、可移除與否、`_PLD`、jack 描述、jack sink、DisplayConfig 路徑）；`json` 匯出資料；`snapshot`／`diff`／`calibrate` 支援插拔前後比較與接口校準。須回答：
   - jack sink 的 PortId 能否對應到 DisplayConfig 顯示路徑；
   - 兩個 USB-C 孔的顯示 connector 能否區分；
   - 同一孔上 Dock 的 USB 樹與顯示輸出能否正確合併；
   - ContainerId／可移除屬性能否區分 Dock 內建音效與 Dock USB 孔上的裝置；
   - `_PLD` 是否提供機身孔位；
   - StableId 是否存在，以及指定端點試音。
2. **接口校準與代表性設備驗證**：用 `snapshot`／`diff`／`calibrate` 子命令逐孔校準機身 USB 與顯示接口，再以手邊的 3.5mm、USB 耳麥、螢幕、Dock、7C 測試端點與裝置節點的新增、移除和重名情形。紀錄用來驗證動態發現與標示能力，不作為可支援設備的固定清冊。
3. **技術選擇**：正式版用 C#／.NET 10 LTS + Avalonia（Fluent 主題、內建系統匣圖示），先做 Windows。專案分為共用核心（資料模型、分類、樹狀、校準、設定）、平台層（Windows：Core Audio、CfgMgr32、KS、DisplayConfig、切換、別名）與 Avalonia 介面；macOS 之後只需新增平台層。快速切換面板為貼齊系統匣的自訂視窗。實作前需安裝 .NET 10 SDK。執行計畫見 [`docs/superpowers/plans/2026-10-25-audio-switch-windows-app.md`](docs/superpowers/plans/2026-10-25-audio-switch-windows-app.md)。快捷鍵預設組合與開機啟動預設值仍待決定。
4. **建立接口拓樸與端點資料**：啟動及通知後重建四類端點與機身接口的關聯，依「呈現方式」產生樹狀資料；別名寫回 Windows 端點並另存原始名稱。以重名、Dock 與筆電同型號螢幕、端點消失及重新接入案例檢查。
5. **完成辨認與手動切換**：快速切換面板、設定視窗（裝置樹、接口定義示意圖與校準）、螢幕「識別」、播放試音、錄音峰值、選用別名與播放／錄音快捷鍵。切換後重新讀取 Windows 預設值確認結果；失敗時顯示原因，不把失敗的切換標成成功。
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

唯讀 Python 探針已完成機身四個 USB 孔的 USB 2 路徑校準；Dock、HDMI 與 USB-C 顯示輸出待實測。Windows 正式版尚未開始，執行計畫已建立。

**macOS（後續範圍）**：切換預設輸出／輸入有公開 API，但沒有通訊預設；USB 孔位可能以 IOKit `locationID` 校準；別名寫回系統與個別程式指定沒有已知的系統層級做法；內建喇叭與 3.5mm 是否為獨立裝置依機種而定。以上皆待 macOS 探針實測。
