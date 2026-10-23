# 測試應用需求：audio_topology 探針

給負責實作的 agent。先讀 repo 根目錄的 `README.md`（特別是「呈現方式」「端點來源分類與接口對應」「本機可行性實驗」）與 `CLAUDE.md`。本文件只定義 `probes/` 測試應用的範圍；不要開始寫產品程式。

## 目的

驗證 README 的四類端點分類與機身接口對應在本機能否成立，並以 README「呈現方式」的樹狀格式印出結果。這是可丟棄、可重跑的命令列工具，結論要能回填 README。

## 限制

- Python 3.12（`py -3.12`），在 `probes/.venv` 建虛擬環境；依賴寫在 `probes/requirements.txt`，以 `comtypes` 與標準庫 `ctypes` 為主，`pycaw` 可選。
- **唯讀**：不得呼叫 `IPolicyConfig` 或任何設定預設端點、寫入裝置屬性、修改登錄的方法。不需要系統管理員權限；若某項資料必須提權才能讀，記錄錯誤即可。
- 單一端點或單一屬性失敗時記錄錯誤碼與訊息，繼續處理其他端點，不得整體中止。
- 只處理 `DEVICE_STATE_ACTIVE` 端點；`raw` 可另列其他狀態的端點供參考。
- 不修改 `D:\WORK\Claude\poc\AI-Vibe-Hub`。不 commit，除非使用者要求。
- 介面輸出與文件用繁體中文；程式碼與識別字用英文。
- 輸出檔（快照、JSON）放 `probes/out/`，並加入 `.gitignore`。

## 指令

入口為 `probes/audio_topology.py`：

| 指令 | 作用 |
|---|---|
| `tree` | 依四類分類與接口校準表，印出 README「呈現方式」格式的樹。每個接口關聯標示來源：`OEM`、`Windows`、`校準`、`推定`、`未知`。 |
| `raw` | 每個端點的完整資料與 PnP 祖先鏈（欄位見下）。 |
| `json [檔名]` | 將 `raw` 的全部資料輸出為 JSON。 |
| `snapshot <名稱>` | 存一份 JSON 快照到 `probes/out/`。 |
| `diff <前> <後>` | 比較兩份快照：新增／消失的端點、PnP 節點、顯示路徑，以及新節點的 LocationPaths 與共同前綴。 |
| `calibrate <接口 ID> <前> <後>` | 由 `diff` 的結果，把新出現的 USB LocationPaths 前綴或顯示 connector 寫入接口校準表。 |
| `tone <端點 ID 或短 ID>`（可選） | 以 WASAPI 共用模式對指定播放端點播放約 1 秒的測試音。 |

短 ID：端點 ID 的末段 GUID 取前 8 碼，供命令列與樹狀輸出使用。

## 要收集的資料

**每個端點**
- 端點 ID、方向、`PKEY_Device_FriendlyName`、`PKEY_AudioEndpoint_FormFactor`、`PKEY_Device_ContainerId`、`PKEY_AudioEndpoint_StableId`（缺少時記為空）、是否為各 role 的目前預設。

**端點到 PnP 裝置**（兩種方法都試，記錄哪一種成功）
- `IMMDevice::Activate(IDeviceTopology)` → `GetConnector(0)` → `GetConnectedTo` → `IPart::GetTopologyObject` → `IDeviceTopology::GetDeviceId` 得到轉接器的 KS 介面路徑，再以 `CM_Get_Device_Interface_PropertyW(DEVPKEY_Device_InstanceId)` 轉成 devnode。
- 以端點的 `SWD\MMDEVAPI\...` devnode 用 `CM_Locate_DevNodeW`／`CM_Get_Parent` 往上走。

**祖先鏈的每個 devnode**（以 `CM_Get_DevNode_PropertyW` 讀取，一直走到根）
- InstanceId、`DEVPKEY_NAME`／FriendlyName、`DEVPKEY_Device_BusReportedDeviceDesc`、`DEVPKEY_Device_Class`、`DEVPKEY_Device_LocationInfo`、`DEVPKEY_Device_LocationPaths`、`DEVPKEY_Device_ContainerId`、`DEVPKEY_Device_RemovalPolicy`、`DEVPKEY_Device_Capabilities` 的可移除位元、`DEVPKEY_Device_PhysicalDeviceLocation`（原始位元組，並嘗試解析 ACPI `_PLD` 的面板、垂直／水平位置、形狀）。

**插孔**（對 connector 另一端的 `IPart` 呼叫 `Activate`）
- `IKsJackDescription`：每個 jack 的 ConnectionType、GeoLocation、GenLocation、PortConnection、IsConnected、顏色。
- `IKsJackDescription2`：JackCapabilities（是否支援插入偵測）。
- `IKsJackSinkInformation`：ConnType、ManufacturerId、ProductId、SinkDescription、PortId（LUID）。

**顯示路徑**
- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)`；每條路徑以 `DisplayConfigGetDeviceInfo` 取 target name（monitorFriendlyDeviceName、outputTechnology、connectorInstance、edidManufactureId、edidProductCodeId、monitorDevicePath）、source name（GDI 名稱 `\\.\DISPLAYn`）、adapter name 與 adapter LUID；由 source mode 取位置與是否為主螢幕。
- 記錄原始 outputTechnology 數值，不自行換算成接口。GDI 名稱的編號不一定等於「設定」中的螢幕編號，兩者都記錄並標明來源。

## 分類與樹狀輸出規則

依 README「端點來源分類與接口對應」：

- **A. USB 音訊**：祖先鏈含 USB 匯流排節點。取最近的 USB 裝置節點的 LocationPaths，與校準表做最長前綴比對，得到機身接口。祖先中若有一組節點共用同一個非零 ContainerId，且該組含 hub，視為 Dock 或外接 hub 節點；端點所屬 USB 裝置若沿用該 ContainerId 且接在不可移除 port，標為「Dock 內建音訊」，否則標為「Dock USB port n」下的裝置。
- **B. 顯示器音訊**：connector 另一端提供 `IKsJackSinkInformation`。以 PortId 對 DisplayConfig 的 adapter／target 比對；不成立時退回 ManufacturerId／ProductId 對 EDID 比對，並把比對方式寫進結果。以 connector 對校準表得到機身接口；同一接口上已有 A 類 Dock 時併入 Dock 節點。
- **C. 主機板內建音效**：有 `IKsJackDescription` 且不屬 A／B。依 jack 的位置與 ConnectionType 分到「機身 3.5mm」或「內建」。
- **D. 其他**：藍牙、虛擬裝置或無法歸類者，放到「藍牙／無線」「虛擬」或「未辨識」。

中間節點（hub、composite、音訊介面）在 `tree` 中收起，只在 `raw` 顯示。任何判斷依據不足時標為 `未知`，不以名稱補猜。

## 接口校準表

`probes/ports.json`，納入版本控制。內容：

- 機器識別：SMBIOS 廠牌、機型、MTM（由程式讀取比對，不符時警告）。
- 機身接口清單：ID、顯示名稱（例如「左側 USB-C 1」）、類型、實體位置與來源。位置以 Lenovo 21RU 使用指南（README「本機基準」的連結）為準；查不到就標 `未知`，不要猜。
- 每個接口的 USB LocationPaths 前綴與顯示 connector，初始為空，由 `calibrate` 寫入，並記錄來源（`_PLD` 或 `校準`）。

## 須回答的問題

在本機盡可能實測，並把結論、證據（相關 `raw` 片段）與未能驗證的原因寫成報告：

1. `IKsJackSinkInformation` 的 PortId 能否對應到 DisplayConfig 的 adapter／target。
2. 兩個 USB-C 孔的顯示 connector 能否區分。
3. 同一孔上 Dock 的 USB 樹與顯示輸出能否正確合併。
4. ContainerId／可移除屬性能否區分 Dock 內建音效與 Dock USB 孔上的裝置。
5. `_PLD` 是否提供機身孔位。
6. 各端點是否有 StableId；`tone` 是否能從指定端點出聲（若有實作）。
7. 本機兩個 Realtek 播放端點各自對應哪個 jack；`Speakers (speaker)` 是否為 7C（拔插 `diff` 確認）。

需要實體插拔的項目（Dock、螢幕、逐孔校準），agent 無法自行操作：請產生一份逐步操作清單給使用者，每一步寫明要插拔什麼、執行哪個 `snapshot`／`diff`／`calibrate` 指令，以及預期看到什麼。

## 完成條件

- `tree`、`raw`、`json`、`snapshot`、`diff`、`calibrate` 在本機可執行；在目前接上的裝置上不出錯，失敗的欄位有錯誤訊息。
- `probes/ports.json` 列出 21RU 的機身接口，位置有來源或標為未知。
- `probes/README.md`：安裝、各指令用法、使用者插拔校準清單。
- 實測結論回填到根目錄 `README.md` 的「本機可行性實驗」，只寫現狀與證據，不寫經過；無法驗證的項目明列在「尚未驗證」。
- 最後回報：完成的指令、七個問題各自的結論（成立／不成立／待使用者插拔驗證）、已知問題。

## 第二輪修正

第一版已提供端點列舉、PnP 祖先鏈、DisplayConfig 與 `snapshot`／`diff`／`calibrate`，單元測試通過。以下缺口使 B、C 兩類無法驗證，須補齊。仍適用上方「限制」：唯讀、不提權、單項失敗只記錄錯誤。

### 已由實測確認、應作為測試案例的事實

- 7C（`USB\VID_303A&PID_2986`）同時提供 USB 播放端點 `Speakers (speaker)` 與一條 USB 顯示路徑（EDID 名稱 S2719DGF，adapter device path 含同一 VID/PID）；拔除或換孔時兩者一起消失或移動。
- 拔除機身 3.5mm 耳麥時，`Realtek HD Audio 2nd output` 與 `麦克风 (Realtek(R) Audio)` 消失，插回後恢復；`扬声器 (Realtek(R) Audio)` 與 `麦克风阵列` 不受影響。
- 同一 USB 2.0 裝置在四個孔分別得到 `USB(7)／HS07`、`USB(6)／HS06`、`USB(1)／HS01`、`USB(5)／HS05`。
- DisplayConfig 的 path ID、adapter LUID 與 `\.\DISPLAYn` 在重新連線後會改變。
- 本機 5 個作用中端點的 StableId 皆為空。

### 必修

1. **實作 KS jack／sink 查詢**（`native/audio_topology.py` 目前只回傳未實作）：以 comtypes 定義 `IDeviceTopology`、`IConnector`、`IPart`、`IKsJackDescription`、`IKsJackDescription2`、`IKsJackSinkInformation`，依本文件「要收集的資料」讀取。同時以 `IPart::GetTopologyObject` → `IDeviceTopology::GetDeviceId` 取得轉接器裝置，作為端點到 PnP 的第二種解析方法，記錄哪一種成功。
2. **修正分類順序**（`classify.py:21`）：每個端點祖先鏈的第一層都是自身的 `SWD\MMDEVAPI` 節點，現行規則因此把所有非 USB 端點判為 D 類，B、C 分支永遠不會執行。改為略過端點自身節點，依序判斷：USB 祖先 → A；jack sink → B；jack 描述或主機板音效轉接器（如 `INTELAUDIO\`、`HDAUDIO\`）→ C；藍牙、虛擬 → D。`build_tree` 須把實際的 sink 比對結果傳入分類，不得固定傳 `None`。
3. **讀取三個缺少的 DEVPKEY**（`native/pnp.py:119` 目前寫死為未映射）：`DEVPKEY_Device_RemovalPolicy`、`DEVPKEY_Device_Capabilities`（取可移除位元）、`DEVPKEY_Device_PhysicalDeviceLocation`（原始位元組並解析 `_PLD` 的面板、垂直／水平位置、形狀、群組代號）。讀不到時記錄 CR 錯誤碼。
4. **USB 顯示器規則**：顯示路徑的 adapter device path 或對應 devnode 位於 USB 祖先鏈下時（例如 7C、DisplayLink），該顯示路徑歸到同一個 USB 接口，並與同一 USB 裝置的音訊端點併成一個裝置節點。由顯示卡輸出的螢幕仍依 B 類處理。
5. **顯示校準改用穩定欄位**（`calibration.py`）：不得以 path ID、adapter LUID 或 GDI 名稱作為比對鍵。改存 adapter device path、target ID、connector instance、output technology；比對時也只用這些欄位。若實測發現其中某欄位也會變，記錄下來並排除。
6. **USB 2／USB 3 路徑**：同一實體孔的 USB 2 與 USB 3 裝置會出現在不同的 xHCI port（HSxx／SSxx），USB-C 的 USB 3 可能經過另一個控制器。校準表每個接口須能保存多組前綴；`calibrate` 的結果標明是 USB 2 或 USB 3 路徑。若 `_PLD` 的群組代號能把同一孔的 HS 與 SS port 配對，優先使用並標示來源 `Windows`。
7. **`ports.json`**：移除「最後測試的 USB 孔」（`usb-last`）這個非實體接口，以及它的校準資料。所有接口位置須有 Lenovo 使用指南的具體頁面或圖示依據，否則改為 `未知`，等使用者確認。
8. **`tree` 呈現**：接口下先顯示使用者認得的裝置節點（Dock、USB 裝置、螢幕），再列其端點；中間節點收起；沒有子項的接口根節點預設不顯示，`--all` 才顯示。`[來源]` 標記須依實際證據，不可把校準結果標為 `OEM`。
9. **小修**：`.gitignore` 移除重複的 `.superpowers/`。根目錄 README「本機可行性實驗」依上面「已由實測確認」的事實更新端點表與結論，並把 README「端點來源分類與接口對應」B 類一段補上 USB 顯示器的例外。

### 可選

- `tone`：以 WASAPI 共用模式對指定播放端點播放約 1 秒測試音。

### 測試

- 為分類順序、USB 顯示器併組、顯示校準鍵、HS／SS 多前綴與 `_PLD` 解析加入單元測試，使用上面已確認事實的資料形狀作為測試資料。
- 在 `requirements.txt` 或 `probes/README.md` 說明如何執行測試（目前 venv 沒有 pytest，`python -m unittest discover -s probes/tests -t .` 可執行）。

### 使用者插拔清單（更新 `probes/README.md`）

每一步都要求使用者說明實際插的是哪個孔，並以該孔的接口 ID 執行 `calibrate`：

1. 四個 USB 孔各用一個 USB 2.0 裝置（例如 7C）與一個 USB 3 裝置（例如 USB 3 隨身碟）各做一次 `snapshot`／`diff`／`calibrate`。
2. HDMI 與兩個 USB-C 孔各直接接一次螢幕，做 `snapshot`／`diff`／`calibrate`；同一個螢幕重插一次，確認校準仍能比對。
3. 機身 3.5mm 插拔耳麥，確認 `tree` 中耳機與麥克風端點出現在 3.5mm 接口下，並記錄 jack 描述的插入狀態。
4. 有 Dock 時，依原清單第 4 步測試。

### 完成條件

- `tree` 在目前接上的裝置上：`扬声器` 與 `麦克风阵列` 出現在「內建」，`2nd output` 與 `麦克风 (Realtek)` 出現在機身 3.5mm（若 jack 資料支持；否則說明缺什麼），7C 接上時其喇叭與螢幕併在同一個 USB 裝置節點下。
- 七個問題的結論表更新；問題 1、4、5 至少要有實際讀取結果（成立、不成立或 Windows 不提供）。
- 最後回報每一項必修的完成狀態與已知問題。
