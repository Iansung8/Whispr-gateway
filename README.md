# WhisprGateway

**OpenWhispr 用的隨需載入語音辨識閘道** — 讓 [OpenWhispr](https://github.com/OpenWhispr/openwhispr) 用你自選的本機模型做聽寫：Qwen3-ASR 系列（GGUF）或 whisper.cpp 模型。模型**平時不佔 GPU**，可從系統匣切換模型／GPU／CPU，也能給區域網路或 VPN 上的其他電腦用。

> *English summary:* a tiny Windows tray app + Node gateway that exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint for OpenWhispr's **self-hosted** mode. It runs llama.cpp's `llama-server` (Qwen3-ASR family GGUF + mmproj) or whisper.cpp's `whisper-server` (ggml), spawns the server only when a request arrives and unloads it after N idle minutes, lets you pick the model and GPU / CPU from the tray, can release the GPU while a game is running, converts Simplified output to Taiwan Traditional without rewriting vocabulary, tidies acronyms and numbers, and can serve other PCs on your LAN/VPN. Nothing large is bundled. UI and docs are in Traditional Chinese.

非官方專案，與 OpenWhispr、阿里 Qwen、MediaTek 無關。開發與測試環境：Windows 11、OpenWhispr 1.9.1、Node 26.7、RTX 5080。

## 為什麼需要它

OpenWhispr 1.9.1 的本機模式只能用內建的 6 個 Whisper 模型、模型一啟動就常駐在 GPU、不能給別台電腦用；選「中文（繁體）」時還會用 OpenCC「台灣用語」表改寫你的用詞（`數據→資料`、`參數→引數`…，見 [docs/openwhispr-notes.md](docs/openwhispr-notes.md)）。這個閘道補上這些，OpenWhispr 本身不用改——只要把轉錄模式切到「自架主機」。

```
OpenWhispr（模式＝自架主機；同一台或別台電腦）
   └─ POST http://<這台的位址>:8790/v1/audio/transcriptions
        └─ whisper-gateway.js（Node，常駐、幾乎不耗資源）
             └─ 有請求才啟動辨識伺服器（127.0.0.1:8791）＋模型，閒置後結束
                  *.gguf → llama.cpp llama-server（Qwen3-ASR / TEA-ASR）
                  *.bin  → whisper.cpp whisper-server（Breeze-ASR-25 / Whisper）
WhisprGateway.exe = 系統匣圖示（語）：看管閘道 + 手動控制
```

## 模型

| 模型 | 引擎 | 大小 | 特點 |
|---|---|---|---|
| **Qwen3-ASR-1.7B**（推薦） | llama.cpp | Q8 2.17 GB 或 Q4_K_M 1.28 GB ＋ mmproj 0.36 GB | 台灣口音與中英夾雜都不需微調，自帶標點；輸出簡體，由閘道轉成台灣繁體字形（只轉字、不換詞） |
| Qwen3-ASR-0.6B | llama.cpp | 0.80 GB ＋ mmproj 0.21 GB | CPU 上最快，錯字約是 1.7B 的兩倍 |
| TEA-ASR-1.1 | llama.cpp | Q6_K 1.42 GB ＋ mmproj 0.36 GB | Qwen3-ASR-1.7B 的台灣微調，原生繁體＋台灣用字 |
| Breeze-ASR-25 | whisper.cpp | q8_0 1.66 GB | MediaTek 台灣華語微調；標點由閘道依停頓補上；只需要 OpenWhispr 的引擎 |

下載指令與 sha256 見 [models/README.md](models/README.md)；各模型的實測比較見 [docs/asr-model-comparison.md](docs/asr-model-comparison.md)。**更換模型＝把檔案放進 `models\`，再從系統匣「模型」選它。**

## 相依項目（**不內附**，請自行準備）

程式本身只有幾十 KB；引擎、ffmpeg、模型都不隨附，啟動時依下列順序自動尋找。缺少時，系統匣選單會以紅字顯示缺什麼。

| 相依 | 需求 | 尋找順序 | 怎麼取得 |
|---|---|---|---|
| **Node.js** | 18.2 以上 | `runtime\node.exe` → PATH | <https://nodejs.org> |
| **llama.cpp**（GGUF 模型用） | `llama-server`，b11178 以上 | `engine\llama-cuda\` → `engine\llama-vulkan\` → OpenWhispr 的 `%APPDATA%\open-whispr\bin\llama-vulkan\` → `engine\llama-cpu\` → OpenWhispr 內附的 CPU 版 | 從 <https://github.com/ggml-org/llama.cpp/releases> 下載 `llama-bNNNN-bin-win-vulkan-x64.zip` 解壓到 `engine\llama-vulkan\`（任何廠牌 GPU；NVIDIA 卡用它與 CUDA 版一樣快）。只裝了 OpenWhispr 也能跑（用它內附的 CPU 版），只是慢 |
| **whisper.cpp**（ggml 模型用） | `whisper-server` | `engine\whisper-cuda\` → `%APPDATA%\open-whispr\bin\whisper-cuda\`（Vulkan、CPU 類推） | OpenWhispr → 設定 → 語音轉文字 → 本機 →「啟用 GPU」，閘道直接借用；或從 <https://github.com/OpenWhispr/whisper.cpp/releases> 自取 |
| **ffmpeg** | 4.0 以上、含 opus 解碼 | `engine\ffmpeg\ffmpeg.exe` → PATH → OpenWhispr 內附的 ffmpeg-static | 裝了 OpenWhispr 就有；或 `winget install Gyan.FFmpeg` |
| **模型** | 見上表 | `models\` | [models/README.md](models/README.md) |
| OpenCC 字典（簡→繁用） | 3 個文字檔約 1 MB | `data\opencc\` | 第一次需要時自動從 OpenCC 的 GitHub 下載 |

> 借用 OpenWhispr 的引擎時，若在 OpenWhispr 裡刪除 GPU 引擎或解除安裝 OpenWhispr，閘道會退回 CPU 或無法辨識。

## 安裝

1. 準備上表的相依。
2. 取得程式：從 **Releases** 下載 `WhisprGateway-vX.Y.Z.zip` 解壓（GitHub Actions 由該版本的原始碼自動編譯）。也可以 clone 後執行 `build.ps1`（用 Windows 內建的 C# 編譯器，不需要 SDK）。
3. 執行 `WhisprGateway.exe`。系統匣出現「語」圖示；第一次執行會產生 `gateway.config.json`，到「模型」選單選你下載的模型。
4. 點圖示 →「OpenWhispr 要填什麼…」，照著在 OpenWhispr 填：
   - 設定 → 語音轉文字 → **自架主機** → 端點 URL `http://127.0.0.1:8790/v1`（API Key、模型名稱留空）。
   - 偏好設定 → 轉錄語言 → **自動**；中文書寫 → **保持轉錄原樣**。**不要選「中文（繁體）」**，那會讓 OpenWhispr 改寫你的用詞。
5. 要跟著系統啟動，就在選單勾「開機（登入）時自動啟動」。

### 給其他電腦用

勾選「允許其他電腦連線（區域網路／VPN）」後，閘道會綁定這台電腦所有私有網段的位址，並只放行同網段的來源；沒勾時只聽 `127.0.0.1`。在另一台電腦的 OpenWhispr 填 `http://<位址>:8790/v1`。

- 第一次開啟時 Windows 可能詢問防火牆，請只允許**私人網路**。
- **沒有 API Key**：安全性來自「只綁私有位址＋只放行同網段」，不要把這個埠轉發到網際網路。可用 `remoteInterfaces` 限定只走某個介面。

## 系統匣選單

| 項目 | 作用 |
|---|---|
| 立即載入／立即卸載 | 手動控制模型是否佔用資源 |
| 閒置自動卸載 | 5／15／30／60 分鐘／不自動卸載（選這個時，啟動後會先把模型載好） |
| 模型 | 列出 `models\` 內的 `*.bin` 與 `*.gguf`（mmproj 自動配對） |
| 運算裝置 | 各張 NVIDIA GPU、Vulkan、純 CPU。指定的 GPU 不在時自動退到其他裝置 |
| VRAM 不足時自動讓出 GPU | 見「讓出 GPU」；預設關閉 |
| 自動補標點（whisper 模型） | 見「標點符號」；預設開啟 |
| 簡體輸出轉台灣繁體（Qwen3-ASR 系列） | OpenCC s2tw 字形轉換，不做用語替換；預設開啟 |
| 詞彙（Qwen3-ASR 系列） | 開啟 `data\vocabulary.txt`（詞彙提示）或 `data\taiwan-lexicon.txt`（替換表）；存檔即生效 |
| 允許其他電腦連線 | 見上節；預設關閉 |
| OpenWhispr 要填什麼… | 顯示端點網址，可一鍵複製 |
| 開機（登入）時自動啟動 | 建立工作排程器的 `WhisprGateway` 工作（登入後 20 秒啟動，每 5 分鐘檢查一次是否還在執行） |

圖示顏色：綠＝模型在 GPU 上、藍＝目前用 CPU 辨識、橘＝載入中、灰＝未載入、紅＝閘道沒回應。

## 效能

RTX 5080，模型已載入時：

| 模型 | 載入 | 每句 | VRAM |
|---|---|---|---|
| Qwen3-ASR-1.7B Q8_0 | 約 3 秒 | 0.3–0.4 秒 | 約 3 GB |
| Qwen3-ASR-0.6B Q8_0 | 約 2 秒 | 0.25 秒 | 約 1.9 GB |
| TEA-ASR-1.1 Q6_K | 約 3 秒 | 0.3–0.8 秒 | 約 2.8 GB |
| Breeze-ASR-25 q8_0 | 約 2 秒 | 0.75 秒 | 約 2.3 GB |

閘道本身（系統匣＋Node）約佔 60 MB RAM。

## 功能說明

### 讓出 GPU

開遊戲、LM Studio 這類吃 VRAM 的程式時，閘道可以自動卸下模型：

- 每 10 秒讀一次 Windows 的「GPU Process Memory」計數器。有單一程式佔用 ≥ 2 GB（`vramGuardProcessMiB`），或剩餘 VRAM < 1 GB（`vramGuardMinFreeMiB`）→ 讓出 GPU。
- 讓出期間聽寫改用 CPU，並換成較小的檔案（`vramGuardCpuModel`）：預設先找 `Qwen3-ASR-1.7B.Q4_K_M.gguf`，再找 `Qwen3-ASR-0.6B-Q8_0.gguf`。
- 那個程式離開 60 秒後（`vramGuardResumeSeconds`）回到 GPU。
- `dwm` 與 NVIDIA Overlay 不算；其他要排除的程式用 `vramGuardIgnore`。

`bash tools/watch-guard.sh` 可即時觀察它的動作。

### 詞彙提示（Qwen3-ASR 系列）

閘道把一份詞彙表當 system prompt 送給模型，讓英文術語寫回英文（例如「酷達」→ CUDA）。

- 常見科技名詞已內建 35 個（VRAM、NVIDIA、repo、GitHub…；`builtinVocabulary: false` 可關）。
- `data\vocabulary.txt` 放你自己常講、而模型常寫錯的詞，一行一個。
- 不要放模型本來就寫得對的短詞（GPU、AI、API）：列出來的詞會把發音相近的字吸過去。總數上限 100。

### 縮寫與數字（Qwen3-ASR 系列）

- 被拆開的縮寫會合併：`G P U`→`GPU`、`N V I D I A`→`NVIDIA`
- 國字數字在確定是數字時轉成阿拉伯數字：`RTX 五零八零`→`RTX 5080`、`DLSS 五`→`DLSS 5`、`十六 GB`→`16 GB`、`三十秒`→`30秒`、`九月十一號`→`9月11號`、`百分之二十`→`20%`
- 不動的：`一個`、`兩顆`、`一點`、`萬一`、`十分`、`一二三`、`十幾個`

個別的詞可以用替換表 `data\taiwan-lexicon.txt` 修（每行「模型寫法、Tab、改成」），預設內容是把「網絡、軟件、服務器」這類用詞改回台灣說法。

### 標點符號（whisper 模型）

Breeze-ASR-25 幾乎不輸出標點，但它的分段剛好切在子句邊界，所以由閘道補：疑問結尾（嗎／呢／是不是…）→「？」；下一段以「另外／首先／最後…」開頭、停頓 ≥ 0.7 秒或最後一段 →「。」；其餘 →「，」。Qwen3-ASR 系列自帶標點。

## 設定檔 `gateway.config.json`

| 欄位 | 說明 |
|---|---|
| `port` / `backendPort` | 對外 8790／內部 8791 |
| `listen` | `"auto"`＝127.0.0.1＋（允許外部連線時）本機所有私有 IPv4 位址；也可寫成位址陣列 |
| `remoteInterfaces` | 介面名稱的正規表示式，空字串＝全部（例：`zerotier|tailscale`） |
| `allowCidrs` | `"auto"`＝上述介面所在網段；也可寫成 CIDR 陣列 |
| `allowRemote`、`idleMinutes`、`device`、`modelFile`、`punctuation`、`convertSimplified`、`vramGuard` | 由系統匣選單寫入 |
| `vramGuardProcessMiB`、`vramGuardMinFreeMiB`、`vramGuardResumeSeconds` | 讓出 GPU 的門檻（預設 2000、1024、60） |
| `vramGuardIgnore` | 額外不算的程式名稱（正規表示式，不含 .exe，例如 `^(chrome|obs64)$`） |
| `vramGuardCpuModel` | 讓出 GPU 期間在 CPU 上用的模型檔；空字串＝用原本的模型 |
| `builtinVocabulary` | `false`＝不送內建的科技名詞 |
| `language` | 辨識語言（`zh`、`auto`…） |
| `prompt` | 只給 whisper 模型的提示詞 |
| `llamaInstruction` | 給 Qwen3-ASR 系列的指示句（system prompt 第一行） |
| `llamaMode` | `chat`（預設，會帶詞彙提示）或 `transcriptions` |
| `llamaContext`、`llamaMaxTokens` | llama-server 的 context 長度（預設 8192）與單次輸出上限（預設 2048） |
| `modelsDir`、`engineDir`、`dataDir`、`tmpDir` | 相對路徑＝相對於本資料夾 |

執行中只有閘道會寫這個檔；要手改請先從選單「結束」。`/control/*` 只接受本機呼叫。`gateway.log` 只記時間、來源 IP、耗時，不記辨識內容。

## 其他

- 自架模式沒有「即時轉錄預覽」，OpenWhispr 內的自訂字典也不會送出（用上面的詞彙提示代替）。
- 僅支援 Windows；自啟動是「登入時」。
- `src\GatewayTray.cs` 是系統匣程式（C# 5）。重編前先 `schtasks /Change /TN WhisprGateway /DISABLE` 並結束 exe，編完再 `/ENABLE`、`/Run`。
- `tools\`：測試與觀察用的腳本。

## 致謝

[OpenWhispr](https://github.com/OpenWhispr/openwhispr)、[llama.cpp](https://github.com/ggml-org/llama.cpp)、[whisper.cpp](https://github.com/ggml-org/whisper.cpp)、[Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR)、[JacobLinCool 的 TEA-ASR](https://huggingface.co/JacobLinCool/TEA-ASR-1.1)、[mradermacher 的 GGUF 量化](https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF)、[MediaTek Research Breeze-ASR-25](https://huggingface.co/MediaTek-Research/Breeze-ASR-25)、[shdennlin 的 ggml 轉檔](https://huggingface.co/shdennlin/breeze-asr-25-ggml)、[OpenCC](https://github.com/BYVoid/OpenCC)。
