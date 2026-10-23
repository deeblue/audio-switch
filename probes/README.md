# 音訊接口拓樸探針

此工具只讀取 Windows Core Audio 與 PnP 資料，不設定預設端點、不寫入裝置屬性、不改登錄。輸出可能包含端點 ID、裝置名稱及硬體識別資訊，請留在本機處理。

## 安裝與執行

```powershell
py -3.12 -m venv probes/.venv
probes/.venv/Scripts/python.exe -m pip install -r probes/requirements.txt
probes/.venv/Scripts/python.exe probes/audio_topology.py tree
probes/.venv/Scripts/python.exe probes/audio_topology.py raw
probes/.venv/Scripts/python.exe probes/audio_topology.py json current
probes/.venv/Scripts/python.exe probes/audio_topology.py snapshot before
```

輸出快照都在 `probes/out/`。`json` 不給檔名時輸出到主控台，給檔名時寫入 `probes/out/<檔名>.json`。

```powershell
probes/.venv/Scripts/python.exe probes/audio_topology.py diff before after
probes/.venv/Scripts/python.exe probes/audio_topology.py calibrate usb-a-left before after
```

校準目前只有在差異快照含唯一 USB PnP LocationPaths 時才會寫入 `probes/ports.json`；候選不唯一或機型不符會拒絕。`tone` 目前未實作。

## 手動插拔驗證

不要在執行探針時變更 Windows 音效設定。每一步都先存快照、只改一條實體連線，再存另一份快照並比較。檔名可自行替換，勿覆寫有用的基準快照。

1. **USB-A 孔位**：拔除 USB 音訊裝置；執行 `snapshot usb-a-left-before`；把 USB speaker／耳麥插入左側 USB-A；執行 `snapshot usb-a-left-after`、`diff usb-a-left-before usb-a-left-after`。應看到新端點或 PnP 節點及其 LocationPaths。拔除後也存一份快照，確認項目消失。對右側 USB-A 重複。
2. **USB-C／Thunderbolt 孔位**：先移除其他外接裝置；對第一個 USB-C 孔執行 `snapshot usb-c-1-before`，接上 USB speaker 或 Dock，再存 `usb-c-1-after` 並執行 `diff usb-c-1-before usb-c-1-after`。第二孔以 `usb-c-2-before/after` 重複。只有不同裝置的路徑能穩定共享前綴時才可校準：`calibrate usb-c-left-1 usb-c-1-before usb-c-1-after`。
3. **HDMI**：每個 HDMI 孔各自做螢幕接入前後快照，執行 `diff hdmi-before hdmi-after`。DisplayConfig 會記錄 active path、connector instance、target／source 與 EDID；用差異確認是哪條顯示路徑新增，再透過 `calibrate hdmi-left hdmi-before hdmi-after` 將穩定的 connector ID 對應到機身孔位。若同一變更出現多個候選 connector，校準會拒絕，需減少同時變動的裝置後重試。
4. **Dock**：以 Dock 接入 USB-C 前後快照比較；接著分別測 Dock 自身 3.5mm、Dock USB 孔的 USB speaker、Dock 上螢幕，每次只新增一項。記錄新端點、PnP 祖先、ContainerId 與 LocationPaths。這些資料可檢視端點拓樸，但 removal policy／capabilities、KS sink 對應與 Dock 實體連接的驗證仍缺，因此目前不能可靠地把 Dock 內建音效及下游裝置合併成一組。
5. **機身 3.5mm**：接入耳麥前後執行 `snapshot jack-before/after` 和 `diff jack-before jack-after`；另以 Windows 錄音測試確認麥克風。探針目前尚未查詢 KS jack 描述，需人工記錄插拔現象。
6. **本機麥克風／喇叭與 7C**：不插拔時執行 `raw`，保存對應端點 ID；將 7C 接入或移除前後分別執行快照和 diff，確認 `Speakers (speaker)` 是否隨 7C 出現／消失。不要只依名稱或 VID 判定。

## 目前探針限制

- Core Audio 可列出作用中端點、方向、外形類型、端點 ID、預設 role 及部分屬性。
- CfgMgr32 可讀端點對應的 MMDEVAPI devnode、parent InstanceId 鏈及部分 DEVPKEY_NAME／FriendlyName、LocationPaths、ContainerId 等屬性；removal policy、capabilities、`_PLD` 等屬性仍可能缺少或不受支援。
- `QueryDisplayConfig` 已列舉本機 active paths、target／source 名稱、connector ID、EDID 與位置；jack 的 `IKsJackDescription`、`IKsJackDescription2`、`IKsJackSinkInformation` 尚未實作。缺少欄位會記在 errors，不會推測 jack 位置。
- 因上述資料缺口，USB 裝置可辨認為 A 類但未校準時會留在「未辨識」；內建類比端點缺 jack 證據時也留在「未辨識」。這是保守結果，不表示 Windows 無法提供關聯資料。

## 本機可行性結論

| 問題 | 目前結論 | 證據／待補資料 |
|---|---|---|
| IKsJackSinkInformation 的 PortId 能否對應 DisplayConfig | 待 KS 查詢 | DisplayConfig 已回報 2 條作用中路徑；sink PortId 尚未收集，不能下匹配結論。 |
| 兩個 USB-C 的顯示 connector 能否區分 | Windows 路徑資料可分辨，機身孔位仍待校準 | DisplayConfig 有 adapter LUID、target ID、connector instance；尚未分別在兩個 USB-C 孔直連螢幕比對。 |
| 同一孔的 Dock USB 與顯示輸出能否合併 | 待實體 Dock 驗證 | 尚無 Dock 插拔快照，也沒有 KS sink 對應結果。 |
| ContainerId／removability 能否區分 Dock 內建音效與 Dock USB 裝置 | 未能判斷 | ContainerId 可讀；removal policy／capabilities 尚未映射，Dock 尚未測試。 |
| `_PLD` 是否指出機身孔位 | 未能判斷 | PhysicalDeviceLocation 尚未映射；USB LocationPaths 已可讀但需逐孔校準。 |
| StableId 與指定端點試音 | StableId：5 個作用中端點皆為空；tone：未實作 | Core Audio 目前未提供 StableId；工具沒有播放或切換預設端點的程式碼。 |
| Realtek 播放／錄音端點與 jack 的關係；Speakers (speaker) 是否為 7C | 一次拔除使 Realtek 第二播放端點與麥克風端點同時消失，符合類比耳麥插孔切換；jack pin 尚待 KS 查詢。speaker：USB／Espressif 路徑成立，7C 實體確認待插拔。 | 兩個 Realtek 端點有相同 Intel Audio codec parent。speaker 祖先為 USB VID_303A／PID_2986；不得只依 VID 判定用途。S2719DGF 的 DisplayConfig adapter device path 也回報同一 VID/PID，仍不能單靠此關聯推斷實體接線。 |

### 本機拔除觀察（2026-10-23）

使用者拔除一個實體連接埠後，將新快照與先前的 `final-after` 比較。一次變更使以下作用中端點消失：

- 播放：`Realtek HD Audio 2nd output (Realtek(R) Audio)`
- 錄音：`麦克风 (Realtek(R) Audio)`

兩個端點對應的 MMDEVAPI PnP 節點都指向同一個 Intel Audio codec parent（`INTELAUDIO\\FUNC_01&VEN_10EC&DEV_0257&SUBSYS_17AA512D...`）；沒有新增端點，也沒有 DisplayConfig 路徑變化。這與同一個類比耳麥插孔同時切換耳機輸出和麥克風輸入相符，是有用的插拔關聯證據。探針仍未讀取 KS jack／sink 的 PortId 或 presence 狀態，因此尚不能把 codec 端點精確對應到 jack pin，也不能據此標定其他機身連接埠。
