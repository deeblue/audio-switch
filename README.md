# audio-switch

在 Windows 上一鍵切換預設的聲音裝置（喇叭與麥克風）。Windows 的設定藏得太深（設定 → 系統 → 音效 → 輸出／輸入），常常要切換時很不方便。

## 為什麼做

開發 7C（ESP32-S3-Touch-LCD-7C）時，電腦上的聲音裝置常常在換：

- 7C 的副螢幕韌體（`AI-Vibe-Hub/vibe-console`，分支 `exp/second-screen`）讓 7C 成為電腦的一個喇叭，Windows 裡叫「speaker」。
- 7C 原本的韌體讓 7C 成為一支麥克風，名稱含「Vibe audio」。
- 測試時要在 7C、筆電內建、耳機之間來回切換。

## 要做到的

1. **系統匣圖示**：點一下列出所有播放裝置與錄音裝置，目前的預設打勾，點哪個就設成預設。
2. **全域快捷鍵**：在常用的幾個裝置之間輪流切換（喇叭、麥克風各一組）。
3. **跟著插拔更新**：裝置插上或拔掉時，清單馬上跟著變。
4. （之後再說）指定的裝置一插上就自動切過去，例如 7C 的 speaker。

不做：每個應用程式各自的輸出裝置（Windows 的「應用程式音量與裝置喜好設定」）、音量混音器、音效處理。

## 先看現成的

動手之前先試試這些，夠用就不必做：

| 工具 | 說明 |
|---|---|
| [SoundSwitch](https://github.com/Belphemur/SoundSwitch) | 開源（GPL-3.0），系統匣加快捷鍵切換播放與錄音裝置，最接近這裡要的 |
| [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | 開源，系統匣的音量混音器，也能切換預設裝置 |
| [AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets) | PowerShell 模組，用指令切換，可以綁在捷徑上 |
| NirSoft `nircmd setdefaultsounddevice` | 命令列 |

決定自己做的理由可能是：要依 7C 的插拔自動切換、要跟 Vibe Console 的主機端整合，或是不想裝第三方軟體。

## 狀態

剛開始，只有這份 README 與 `CLAUDE.md`，還沒有程式。
