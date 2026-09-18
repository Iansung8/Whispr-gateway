# WhisprGateway

**OpenWhispr 用的隨需載入語音辨識閘道** — 讓 [OpenWhispr](https://github.com/OpenWhispr/openwhispr) 用你自選的 whisper.cpp 模型（預設聯發科 Breeze-ASR-25，台灣華語＋中英夾雜）做聽寫，模型**平時不佔 GPU**，還會自動補上中文標點。

> *English summary:* a tiny Windows tray app + Node gateway that exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint for OpenWhispr's **self-hosted** mode. It spawns whisper.cpp's `whisper-server` only when a request arrives and unloads it after N idle minutes (0 VRAM when idle), lets you pick GPU / CPU and the ggml model from the tray, can serve other PCs on your LAN/VPN, and adds Chinese punctuation from segment boundaries (useful for Breeze-ASR-25, which emits almost none). Nothing large is bundled — it borrows the engine that OpenWhispr's "Enable GPU" button installs. UI and docs are in Traditional Chinese.

非官方專案，與 OpenWhispr、MediaTek 無關。開發與測試環境：Windows 11、OpenWhispr 1.9.1、Node 26.7、RTX 5080。

## 為什麼需要它

OpenWhispr 1.9.1 的本機模式：只能用內建的 6 個 Whisper 模型、一啟動就把模型常駐在 GPU 直到關閉（沒有閒置卸載）、模型也不能給別台電腦用。這個閘道補上這三點，OpenWhispr 本身完全不用改——只要把轉錄模式切到「自架主機」。

```
OpenWhispr（模式＝自架主機；同一台或別台電腦）
   └─ POST http://<這台的位址>:8790/v1/audio/transcriptions
        └─ whisper-gateway.js（Node，常駐、幾乎不耗資源）
             └─ 有請求才啟動 whisper.cpp 的 whisper-server（127.0.0.1:8791）＋模型，閒置後結束
WhisprGateway.exe = 系統匣圖示（語）：看管閘道 + 手動控制
```

實測（RTX 5080、Breeze-ASR-25 q8_0、13 秒音檔）：冷啟動首句約 2.2 秒、之後約 0.75 秒，載入時 VRAM 約 +2.3 GB、卸載後歸零；純 CPU 約 8.5–10 秒。

## 相依項目（**不內附**，請自行準備）

這個 app 本身只有幾十 KB；大的東西（辨識引擎約 1 GB、ffmpeg 約 80 MB）不隨附、也不轉散布，啟動時依下列順序自動尋找。缺少時，系統匣選單會以紅字顯示缺什麼。

| 相依 | 需求 | 尋找順序 | 怎麼取得 |
|---|---|---|---|
| **Node.js** | 18.2 以上（實測 26.7） | `runtime\node.exe` → PATH | <https://nodejs.org> |
| **辨識引擎（GPU）** | whisper.cpp `whisper-server`；CUDA 版需 NVIDIA 驅動、運算能力 6.1+（GTX 10 系列以上） | `engine\cuda\` → `%APPDATA%\open-whispr\bin\whisper-cuda\` | **最簡單：安裝 OpenWhispr → 設定 → 語音轉文字 → 本機 →「啟用 GPU」**（約 770 MB）。閘道直接借用，不需複製。 |
| 辨識引擎（Vulkan，選配） | AMD／Intel GPU 用 | `engine\vulkan\` → `%APPDATA%\open-whispr\bin\whisper-vulkan\` | 在沒有 NVIDIA GPU 的電腦上按 OpenWhispr 的「啟用 GPU」會裝這個。**此路徑尚未實測。** |
| **辨識引擎（CPU）** | 同上的 CPU 版；也是 GPU 失敗時的退路 | `engine\cpu\` → `<OpenWhispr 安裝目錄>\resources\bin\` | 隨 OpenWhispr 安裝就有 |
| **ffmpeg** | 4.0 以上、含 opus 解碼（實測 6.1.1）。OpenWhispr 送來的是 webm/opus，whisper-server 只讀 wav | `engine\ffmpeg\ffmpeg.exe` → PATH → OpenWhispr 內附的 ffmpeg-static | 裝了 OpenWhispr 就有；或 `winget install Gyan.FFmpeg` |
| **模型** | whisper.cpp 的 ggml `.bin` | `models\<modelFile>` | 見 [models/README.md](models/README.md) |

不想裝 OpenWhispr 的話，引擎的原始出處是 <https://github.com/OpenWhispr/whisper.cpp/releases>（tag `0.0.9`：`whisper-server-win32-x64-cuda.zip`／`-vulkan.zip`／`-cpu.zip`），解壓到對應的 `engine\<cuda|vulkan|cpu>\`，檔名保持 `whisper-server-win32-x64[-cuda|-vulkan].exe`。

> 借用 OpenWhispr 的引擎時，若在 OpenWhispr 裡刪除 GPU 引擎或解除安裝 OpenWhispr，閘道會退回 CPU 或無法辨識。

## 安裝

1. 準備上表的相依（Node.js、OpenWhispr 並按「啟用 GPU」、下載模型到 `models\`）。
2. 取得程式：從 **Releases** 下載 `WhisprGateway-vX.Y.Z.zip` 解壓即可——那個 zip 是 GitHub Actions 從該版本標籤的原始碼自動編譯、打包的（發佈說明附 sha256）。也可以自己編譯：clone 之後執行 `build.ps1`（用 Windows 內建的 C# 編譯器，不需要安裝 SDK）。
3. 執行 `WhisprGateway.exe`。系統匣出現「語」圖示（Windows 11 預設收在「^」裡，可拖到工作列）；有缺東西會以紅字提示。第一次執行會由 `gateway.config.default.json` 產生 `gateway.config.json`。
4. 點圖示 →「OpenWhispr 要填什麼…」，照著在 OpenWhispr 填：設定 → 語音轉文字 → **自架主機** → 端點 URL `http://127.0.0.1:8790/v1`（API Key、模型名稱留空）；偏好設定 → 轉錄語言 → **中文（繁體）**。
5. 要跟著系統啟動，就在選單勾「開機（登入）時自動啟動」。資料夾放哪裡都可以（設定用相對路徑）；搬動後重新勾一次自啟動即可。

### 給其他電腦用

勾選「允許其他電腦連線（區域網路／VPN）」後，閘道會綁定這台電腦**所有私有網段的位址**（區域網路、ZeroTier、Tailscale…），並只放行來自同一個網段的來源；沒勾時只聽 `127.0.0.1`。「OpenWhispr 要填什麼…」會列出每個位址和它的介面名稱——在另一台電腦的 OpenWhispr 填它連得到的那一個（`http://<位址>:8790/v1`）。

- 第一次開啟時 Windows 可能詢問是否允許 Node.js 通過防火牆，請只允許**私人網路**。
- **沒有 API Key**：安全性來自「只綁私有位址＋只放行同網段」。不要把這個埠轉發到網際網路。想再收緊，可在設定檔用 `remoteInterfaces`（介面名稱的正規表示式，例如 `zerotier`）限定只走某個介面。
- OpenWhispr 的自架模式只允許對私有位址用純 http，這正好符合。

## 系統匣選單

| 項目 | 作用 |
|---|---|
| 立即載入／立即卸載 | 手動控制模型是否佔用資源 |
| 閒置自動卸載 | 5／15／30／60 分鐘／不自動卸載 |
| 模型 | 列出 `models\` 內的 `*.bin`；**更換模型＝把新的 ggml `.bin` 放進去再選它** |
| 運算裝置 | 每張 NVIDIA GPU 各一項、Vulkan（有裝該引擎才出現）、純 CPU。指定的 GPU 不在時（例如筆電省電模式移除獨顯）自動退到其他 GPU → Vulkan → CPU，並標示「目前實際使用」 |
| 自動補標點（依停頓切句） | 見下節；預設開啟 |
| 允許其他電腦連線（區域網路／VPN） | 見上節；預設關閉 |
| OpenWhispr 要填什麼… | 顯示端點網址，可一鍵複製 |
| 開機（登入）時自動啟動 | HKCU `…\Run\WhisprGateway` |

圖示顏色：綠＝已載入、橘＝載入中、灰＝未載入、紅＝閘道沒回應。

## 標點符號

Breeze-ASR-25 本身幾乎不輸出標點（試過多種 prompt，最多換來零星的半形逗號）。但它的時間軸對齊很好：開啟時間軸後，**分段剛好切在子句邊界**（實測一段含 8 個標點的話切出 8 段、位置完全一致）。所以標點由閘道補：

- 分段結尾是 `嗎／呢／是不是／有沒有／好不好…` → `？`
- 下一段以 `另外／此外／對了／首先／其次／再來／最後／總之／接下來／順便／話說` 開頭、或兩段之間停頓 ≥ 0.7 秒、或最後一段 → `。`
- 其餘 → `，`；並把半形 `, ? !` 轉全形、去掉中文與數字間多餘的空格。純英文的分段不動。

規則刻意保守（錯的逗號比錯的句號好讀），在 `whisper-gateway.js` 的 `QUESTION_END`／`SENTENCE_OPENER`／`LONG_PAUSE_SECONDS`，歡迎調整。幾乎不增加延遲。

限制：目前只用合成語音驗證過，停頓很規律；真人說話若句中猶豫會多出逗號、一口氣講完則標點偏少。要更像書面語，可再疊 OpenWhispr 的「語言模型」文字整理。用會自己輸出標點的模型（例如原版 Whisper large-v3）時，可以把這個功能關掉。

## 自架模式的取捨

沒有「即時轉錄預覽」；OpenWhispr 內的自訂字典不會送出——專有名詞請加在 `gateway.config.json` 的 `prompt`。

## 設定檔 `gateway.config.json`

| 欄位 | 說明 |
|---|---|
| `port` / `backendPort` | 對外 8790／內部 8791（刻意避開 OpenWhispr 自己的 8178–8199） |
| `listen` | `"auto"`＝127.0.0.1＋（允許外部連線時）本機所有私有 IPv4 位址；也可寫成位址陣列 |
| `remoteInterfaces` | 介面名稱的正規表示式，空字串＝全部（例：`zerotier|tailscale`） |
| `allowCidrs` | `"auto"`＝上述介面所在網段；也可寫成 CIDR 陣列 |
| `allowRemote`、`idleMinutes`、`device`、`modelFile`、`punctuation` | 由系統匣選單寫入 |
| `language`、`prompt` | 傳給 whisper 的語言與提示詞 |
| `modelsDir`、`engineDir`、`tmpDir` | 相對路徑＝相對於本資料夾 |

**執行中只有閘道會寫這個檔**；要手改請先從選單「結束」，改完再開。`/control/*`（系統匣用的控制介面）只接受本機呼叫。`gateway.log` 只記時間、來源 IP、耗時，不記辨識內容。

## 其他

- [docs/openwhispr-notes.md](docs/openwhispr-notes.md)：做這個專案時整理的 OpenWhispr 1.9.1 行為筆記（固定模型槽位、GPU 失敗旗標、Vulkan 裝置釘選、自架模式的請求格式…）。
- `tools\`：離線測試腳本（用 Windows 內建 TTS 合成測試音檔）。
- `src\GatewayTray.cs`：系統匣程式原始碼（C# 5 語法）。`WhisprGateway.exe --render-info out.png` 可把資訊視窗畫成圖檢查版面。
- 已知限制：僅 Windows；自啟動是「登入時」而非開機未登入時；whisper-server 的 `--prompt` 在 Windows 會被 ANSI code page 弄壞，所以中文 prompt 由閘道以 UTF-8 multipart 欄位注入。

## 致謝

[OpenWhispr](https://github.com/OpenWhispr/openwhispr)、[whisper.cpp](https://github.com/ggml-org/whisper.cpp)、[MediaTek Research Breeze-ASR-25](https://huggingface.co/MediaTek-Research/Breeze-ASR-25)、[shdennlin 的 ggml 轉檔](https://huggingface.co/shdennlin/breeze-asr-25-ggml)。
