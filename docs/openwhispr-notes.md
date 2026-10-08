# OpenWhispr 1.9.1 行為筆記（讀原始碼＋實測）

> **English summary.** Notes on OpenWhispr 1.9.1, read from its `app.asar` and checked on Windows 11; not official documentation. Its local mode has six fixed Whisper slots and keeps `whisper-server` running for as long as the app runs, with no idle unload. A failed CUDA start sets `WHISPER_GPU_FAILED=cuda` in `.env`, and that flag keeps the app on the CPU until the GPU engine is retried or reinstalled, which laptops that power the dGPU off run into easily; the Vulkan engine re-pins the discrete GPU by itself. Self-hosted mode posts `multipart/form-data` (`file`, `model`, `language`) to `{endpoint}/audio/transcriptions`, sends no prompt (so the in-app dictionary does nothing), and allows plain `http://` only for private addresses. Choosing "Chinese (Traditional)" runs every result through OpenCC cn→twp, which also swaps vocabulary (數據→資料, 參數→引數 …); Transcription language "Auto" plus Chinese script "Keep as transcribed" avoids it.


做這個閘道時從 OpenWhispr 的 `app.asar` 讀到、並在 Windows 11 上驗證過的行為。版本不同請自行再確認；這些都不是官方文件。

## 本機 Whisper 模型

- 只有 6 個固定槽位：`tiny / base / small / medium / large / turbo`，各自對應固定檔名（例如 medium → `ggml-medium.bin`），**不支援自訂模型**。
- 「已下載」只用檔案是否存在判斷，不驗 hash／大小，也沒有依模型名稱的特殊邏輯。所以把任何 whisper.cpp 的 ggml 模型存成某個空槽位的檔名，UI 上那個槽位就會變成它（例如把 Breeze-ASR-25 存成 `ggml-medium.bin`）。
- 模型目錄：`%USERPROFILE%\.cache\openwhispr\whisper-models\`；使用者路徑含空白或非 ASCII 時改用 `C:\ProgramData\OpenWhispr\cache\`。App 更新不會動它。
- 選了哪個模型、轉錄語言、字典等設定存在 renderer 的 localStorage；`%APPDATA%\open-whispr\.env` 只是給主行程預熱用的鏡像，從外部改它沒有用。

## 本機引擎的生命週期

- App 一啟動就預熱 `whisper-server` 並**常駐到 App 結束**；睡眠喚醒後還會主動重載。**沒有閒置卸載的設定**——這就是本專案存在的原因。
- 引擎只在這些時候停止：結束 App、切到雲端／自架主機／Parakeet、刪除或重裝 GPU 引擎。
- 閒置時從外部結束 `whisper-server-*.exe` 是安全的（下次聽寫自動重啟）；**但轉錄進行中被結束會被判定為 GPU 當機**，見下一節。

## GPU 引擎（「啟用 GPU」）

- 按鈕下載的是 `OpenWhispr/whisper.cpp` release 的 `whisper-server`：偵測到 NVIDIA 給 CUDA 版（約 770 MB），否則給 Vulkan 版（約 23 MB），各自裝在 `%APPDATA%\open-whispr\bin\whisper-cuda\`、`whisper-vulkan\`，下載時有 sha256 驗證。
- **CUDA 啟動失敗或轉錄中當掉 → 退回 CPU，並把 `WHISPER_GPU_FAILED=cuda` 寫進 `.env`。這個旗標會一直留著**，直到在 UI 按重試、重裝引擎，或 App 升版後第一次啟動。會切換獨顯／內顯的筆電（省電模式移除獨顯）很容易踩到：獨顯回來後仍然一直跑 CPU。
- Vulkan 版沒有這個問題：啟動時列舉裝置，device 0 是內顯且有獨顯時自動釘到獨顯（`WHISPER_VULKAN_DEVICE`）；獨顯消失後釘選超出範圍會自動清掉改跑內顯，回來又自動釘回去。所以這類筆電建議用 Vulkan 引擎——在獨顯被移除的狀態下按「啟用 GPU」就會拿到 Vulkan 版。
- RTX 50 系列（Blackwell）實測可用 CUDA 版。

## 自架主機（self-hosted）轉錄模式

- 對 `{端點}/audio/transcriptions` 送 `POST multipart/form-data`：`file`（`audio/webm`，opus）、`model`（選填）、`language`（如 `zh`）；**不送 `prompt`**，所以 App 內的自訂字典在這個模式無效。回應只讀 JSON 的 `text`。
- 純 `http://` 只允許私有位址（`127.x`、`10.x`、`172.16–31.x`、`192.168.x`、`100.64/10`、`*.local`、`*.ts.net`）；其餘要 https。沒有送 API Key。
- 這個模式沒有「即時轉錄預覽」（那需要串流伺服器）。
- 轉錄語言選「中文（繁體）」時，結果會再過 OpenCC（cn → twp）。

## 「中文（繁體）」會偷偷換掉你的用詞（OpenCC twp）

轉錄語言選 **zh-TW** 時，OpenWhispr 對每一段轉錄結果套用 OpenCC 的 **cn → twp**（`chineseScript.js`），而且「zh-CN／zh-TW 是使用者明示，永遠生效」——中文書寫的「保持轉錄原樣」在這時不起作用。twp 是「台灣用語」模式，除了轉字形還會做詞彙替換，於是模型明明聽對的詞也會被改：實測 50 筆聽寫裡出現 `數據→資料`（26 次）、`文件→檔案`、`參數→引數`、`腳本→指令碼`、`擴展→擴充套件`、`分區→分割槽`、`打開→開啟`、`調用→呼叫`、`只→隻`、`了→瞭`、`台→臺`。對已經是繁體的輸出（Breeze、TEA-ASR）一樣會做。

要關掉：偏好設定 → 轉錄語言 選 **自動**，並把「中文書寫（語音輸入）」設成 **保持轉錄原樣**。自架主機模式下語言由閘道決定（`language` 設定），所以選「自動」不會影響辨識。

## whisper.cpp `whisper-server` 的兩個坑（Windows）

- `--prompt "中文"` 由命令列傳入會被 ANSI code page 弄壞；要用請求裡的 multipart `prompt` 欄位（UTF-8）。
- 它原生只讀 wav；收 webm 要加 `--convert`，而且 `ffmpeg` 必須在該行程的 PATH 上。`--inference-path /v1/audio/transcriptions` 可以讓它直接當 OpenAI 相容端點。
