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
