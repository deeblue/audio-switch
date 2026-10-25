# 音訊接口拓樸探針

此工具只讀取 Windows Core Audio、CfgMgr32、DisplayConfig 與可用的 KS topology 資料。不設定預設端點、不寫入裝置屬性、不改登錄。

## 安裝與執行

```powershell
py -3.12 -m venv probes/.venv
probes/.venv/Scripts/python.exe -m pip install -r probes/requirements.txt
probes/.venv/Scripts/python.exe probes/audio_topology.py tree
probes/.venv/Scripts/python.exe probes/audio_topology.py tree --all
probes/.venv/Scripts/python.exe probes/audio_topology.py raw
probes/.venv/Scripts/python.exe probes/audio_topology.py json current
probes/.venv/Scripts/python.exe probes/audio_topology.py snapshot before
probes/.venv/Scripts/python.exe probes/audio_topology.py diff before after
probes/.venv/Scripts/python.exe probes/audio_topology.py calibrate usb-a-left before after
```

測試使用標準庫 unittest，無需 pytest：

```powershell
probes/.venv/Scripts/python.exe -m unittest discover -s probes/tests -t .
```

快照與 JSON 存在 `probes/out/`。`tree` 預設隱藏沒有子項的機身接口；`--all` 顯示全部接口。`calibrate` 會把新發現的 LocationPaths 前綴加到指定接口，並記錄 USB 2／USB 3／PCIe 隧道／未知路徑類型；一個接口可保留多組前綴。顯示校準只保存 adapter device path、target ID、connector instance、output technology，不用 path ID、LUID 或 `DISPLAYn` 作鍵。

`snapshot` 與 `json` 會記錄所有 Core Audio 端點狀態，包含 Disabled、NotPresent 與 Unplugged。只有 Active 端點會讀取 PnP／KS／顯示拓樸資料，並進入 `tree` 分類。

## 插拔校準清單

每次只改一條實體連線。執行前記下**實際插入的外殼接口**，再把下列 `<接口 ID>` 換成 `ports.json` 裡對應 ID。接口 ID：`usb-a-left`、`usb-a-right`、`usb-c-left-front`、`usb-c-left-rear`、`hdmi`、`jack-combo`。四個 USB 孔的位置來自使用者實測；HDMI 與 3.5mm 仍標為「未知」。勿依 USB(1) 或 HS07 等 OS 路徑編號猜測機身位置。

`calibrate` 取前後快照之間新出現或 LocationPaths 改變的最上層節點（父節點不在變動集合內），作為這次連線的接點；只有屬性改變不算移動，所以同一 USB 裝置從前一孔移到下一孔的兩份快照也能用來校準。一個實體接口最多帶出 USB 2、USB 3、PCIe 隧道各一個接點，例如 Dock 一次帶出 USB 2 Hub、USB 3 Hub 與 USB4 隧道後的 PCIe 橋接器，全部記到同一接口；同一種傳輸出現兩個接點表示同時改了兩條連線，結果為 ambiguous。PCIe 接點須位於 root port 之後（路徑至少兩層 `PCI()`），iGPU 等直接掛在 root complex 的裝置不算。

1. **四個 USB 孔**：HS 路徑已用 7C（USB 2）校準。USB-C 的 USB 3 與 USB4 路徑由 Dock 測試一併校準；USB-A 的 USB 3 路徑不影響 USB 音訊裝置，暫不校準。每次執行 `snapshot <id>-before`，插入裝置，執行 `snapshot <id>-after`、`diff <id>-before <id>-after`、`calibrate <接口 ID> <id>-before <id>-after`。同一接口的多筆校準會累積。預期能看到 USB PnP LocationPaths 新增或改變，校準資料記錄 USB 2 或 USB 3。
2. **HDMI 與兩個 USB-C 顯示輸出**：每個接口直接連接螢幕前後各存快照、執行 diff，再用該接口 ID 執行 calibrate。重插同一螢幕再比對。校準只比對穩定欄位；path ID、adapter LUID 與 `\.\DISPLAYn` 可能改變。不要同時經 Dock 測試，先分開建立基準。
3. **機身 3.5mm**：插入耳麥前後執行 `snapshot`／`diff`，並檢查 `raw` 的 KS jack presence。確認播放與錄音端點是新增／消失，還是同一端點狀態改變。
4. **Dock**：Dock 接入 USB-C 前後快照；再逐一測 Dock 3.5mm、Dock USB 孔的 USB speaker、Dock 螢幕，每次只改一項。確認 USB 音訊和 USB 顯示路徑是否落在同一裝置節點；記錄 ContainerId、removal policy、capabilities 及 LocationPaths。需要時以 USB-C 接口 ID 校準 Dock 所在孔。

## 七項可行性結論

| 問題 | 結論 | 證據／待補資料 |
|---|---|---|
| KS sink PortId 能否匹配 DisplayConfig | 待 HDMI／DisplayPort 螢幕實測 | 本機目前沒有顯示卡輸出的螢幕音訊端點；Realtek 與 7C 端點都不提供 `IKsJackSinkInformation`，符合預期。程式已實作 PortId 與 adapter LUID 比對及 EDID fallback。 |
| 兩個 USB-C 顯示 connector 能否區分 | 待逐孔校準 | 已改用穩定欄位；尚未分別直接連接螢幕測試。 |
| 同孔 Dock USB 與顯示能否合併 | USB 顯示裝置成立；Dock 待實測 | 7C 的 `Speakers (speaker)` 與 S2719DGF 顯示路徑在 `tree` 中併在同一個 `speaker` 裝置節點下（顯示 adapter 為 `MI_00`、音訊為 `MI_03`，共用上層 USB 裝置）。 |
| ContainerId／removability 能否辨識 Dock 內建音效與 USB 子裝置 | 待 Dock 實測 | ContainerId、RemovalPolicy、Capabilities 已加入讀取；無 Dock 對照快照。 |
| `_PLD` 是否給出機身孔位 | 部分；只作輔助 | 四孔皆有 `_PLD`。USB-A 面板值與實際側別相符（左 panel 2、右 panel 3）；兩個左側 USB-C 都回報 panel 0（上），與實際不符，且兩孔除群組位置（前 2、後 1）外欄位相同。孔位以 LocationPaths 校準為準。其他多數 devnode 回報 CR `0x0025`（屬性不存在）。 |
| StableId 與指定端點試音 | StableId 不存在；試音未實作 | 五個作用中端點 StableId 均空。 |
| Realtek jack 對應、7C 路徑與插拔 | 成立 | `IKsJackDescription`：`2nd output` 與 `麦克风 (Realtek)` 為 3.5mm、插孔連接、支援插入偵測；`扬声器` 為其他類比、內建裝置；`麦克风阵列` 為其他數位、內建裝置。與耳麥插拔結果一致。7C 端點與顯示路徑同時消失／恢復。 |

## 目前實測與限制

- 本次即時讀取有 3 個播放、2 個錄音端點；7C 的 `Speakers (speaker)` 與 S2719DGF 顯示路徑同時存在，拔插快照證明兩者共同出現／消失。7C 逐孔校準結果：左側 USB-A `USB(6)/HS06`、右側 USB-A `USB(7)/HS07`、左側前方 USB-C `USB(1)/HS01`、左側後方 USB-C `USB(5)/HS05`；校準後 `tree` 把 7C 歸到實際所在接口，來源標示「校準」。
- 3.5mm 耳麥拔除時 `Realtek HD Audio 2nd output` 與 `麦克风 (Realtek(R) Audio)` 消失，重新插入後恢復；內建 Realtek 喇叭與麥克風陣列維持作用中。
- 五個作用中端點 StableId 均為空。
- KS 查詢經 `IDeviceTopology` → connector → `IPart` 讀取 `IKsJackDescription`／`IKsJackDescription2`／`IKsJackSinkInformation`，五個作用中端點皆可讀到 jack 描述。歸到機身 3.5mm 的條件是連接類型為 3.5mm／1/4 吋／複合且連接方式為插孔；內建喇叭同樣回報 IsConnected，不能只看插入狀態。Realtek 3.5mm 插孔的 GeoLocation 回報為 Rear，與機身實際位置無關，側別不採用此欄位。`test_jacks` 含一項實機測試，確認至少一個端點讀得到 jack 描述。
- PnP 讀取已包含 RemovalPolicy、Capabilities 可移除位元與 PhysicalDeviceLocation 原始值／`_PLD` 解析。四孔 `_PLD` 對照見上表；USB-C 的面板值與實際不符，因此 `_PLD` 只顯示在裝置節點旁供參考，不參與歸類。ACPI 欄位依 [ACPI 6.5 §6 Device Configuration](https://uefi.org/specs/ACPI/6.5/06_Device_Configuration.html) 解碼，Windows 屬性來源見 [DEVPKEY_Device_PhysicalDeviceLocation](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-physicaldevicelocation)。接口表不保留「最後測試的 USB 孔」這種非實體接口；四個 USB 孔位置來自使用者實測，HDMI 與 3.5mm 位置為「未知」。
- USB 音訊端點按 USB PnP devnode 分組；USB 顯示路徑先以 adapter instance ID 精確比對，再取其上層 USB 裝置併組。GPU 顯示音訊仍用 sink/EDID 路徑。USB 裝置節點旁顯示 `_PLD` 的面板、形狀與群組位置。
- `tree` 的使用者節點先顯示 USB 裝置或螢幕再顯示端點；無子項接口預設隱藏，`--all` 可展開。識別來源以 Windows、校準或未知標示。
