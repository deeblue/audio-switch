# audio-switch：給接手的 session

目的與範圍見 `README.md`。這是一個 side project，跟 `D:\WORK\Claude\poc\AI-Vibe-Hub` 是不同的 repo，不要去改那邊的檔案。

## 慣例

- 文件與介面文字用繁體中文；程式碼、識別字、commit 訊息用英文。
- 只在使用者要求時 commit；沒有 remote，不 push。
- 文件只寫現狀，不寫經過。
- 先讓使用者決定下面「還沒決定」的事，再開始寫程式。

## 技術上已知的事

- **列出裝置與監看插拔**：Windows Core Audio，`IMMDeviceEnumerator`（`EnumAudioEndpoints`、`GetDefaultAudioEndpoint`），插拔與預設變更用 `IMMNotificationClient`。這些是公開 API。
- **設定預設裝置沒有公開 API**。所有現成工具（SoundSwitch、EarTrumpet、AudioDeviceCmdlets、nircmd）都用未公開的 COM 介面 `IPolicyConfig`（CLSID `870af99c-171d-4f9e-af0d-e63df40c2bc9`）的 `SetDefaultEndpoint(deviceId, role)`。它從 Windows 7 用到現在，但微軟沒有保證，Windows 更新後可能失效。
- **三個 role**：`eConsole`、`eMultimedia`、`eCommunications`。一般「設成預設」要三個都設；只設 `eCommunications` 是「預設通訊裝置」（Teams、Zoom 會用那個）。介面上要不要分開讓使用者選，是個設計決定。
- 每個應用程式各自的輸出裝置要用另一個未公開介面（`IAudioPolicyConfigFactory`），不在範圍內。
- 這台電腦：Windows 11 x64；有 Python 3.12（`py -3.12`）；**沒有 .NET SDK**。

## 還沒決定（先問使用者）

1. **語言**：
   - C# / .NET 8（WinForms 系統匣）：COM interop 最直接，可以發佈成單一 exe，常駐記憶體小。需要先裝 .NET SDK。我傾向這個。
   - Python（`pycaw` / `comtypes` 處理 Core Audio，`pystray` 做系統匣）：跟 Vibe Console 主機端同一種語言，以後要整合比較容易；但要帶 Python 執行環境，`IPolicyConfig` 要自己用 comtypes 定義。
2. **快捷鍵**：預設組合鍵是什麼，輪流切換時要在哪幾個裝置之間。
3. **開機自動啟動**：要不要預設開啟。
4. **自動切換**（README 第 4 項）：要不要做，做的話規則怎麼設定。

## 驗收

在這台電腦上，插著 7C（副螢幕韌體會出現「speaker」）：從系統匣與快捷鍵都能在 7C 和筆電喇叭之間切換，切完 YouTube 的聲音馬上換到另一邊；拔掉 7C 時清單同步更新，程式不當掉。
