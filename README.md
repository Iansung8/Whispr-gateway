# WhisprGateway

**OpenWhispr 用的隨需載入語音辨識閘道** — 讓 [OpenWhispr](https://github.com/OpenWhispr/openwhispr) 用你自選的本機模型做聽寫：**Qwen3-ASR 系列**（推薦台灣版 TEA-ASR-1.1，原生繁體、自帶標點）或 whisper.cpp 模型（Breeze-ASR-25…）。模型**平時不佔 GPU**，可從系統匣切換模型／GPU／CPU，也能給區域網路或 VPN 上的其他電腦用。

> *English summary:* a tiny Windows tray app + Node gateway that exposes an OpenAI-compatible `/v1/audio/transcriptions` endpoint for OpenWhispr's **self-hosted** mode. It runs either llama.cpp's `llama-server` (Qwen3-ASR family GGUF + mmproj) or whisper.cpp's `whisper-server` (ggml), spawns the server only when a request arrives and unloads it after N idle minutes (0 VRAM when idle), lets you pick the model and GPU / CPU from the tray, converts Simplified output to Taiwan Traditional without rewriting vocabulary, adds punctuation for whisper models from segment boundaries, and can serve other PCs on your LAN/VPN. Nothing large is bundled. UI and docs are in Traditional Chinese.

非官方專案，與 OpenWhispr、阿里 Qwen、MediaTek 無關。開發與測試環境：Windows 11、OpenWhispr 1.9.1、Node 26.7、RTX 5080。

## 為什麼需要它

OpenWhispr 1.9.1 的本機模式：只能用內建的 6 個 Whisper 模型、一啟動就把模型常駐在 GPU 直到關閉（沒有閒置卸載）、模型也不能給別台電腦用；而且選「中文（繁體）」時會用 OpenCC「台灣用語」表偷偷改寫你的用詞（`數據→資料`、`參數→引數`、`擴展→擴充套件`…，見 [docs/openwhispr-notes.md](docs/openwhispr-notes.md)）。這個閘道補上這些，OpenWhispr 本身完全不用改——只要把轉錄模式切到「自架主機」。

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
| **Qwen3-ASR-1.7B**（預設推薦）／0.6B | llama.cpp | 2.17 GB／0.80 GB ＋ mmproj 0.36／0.21 GB | 阿里官方（2026-01，Apache-2.0），30 語言＋22 種漢語方言口音，不需台灣微調就能處理台灣口音與中英夾雜，自帶標點；輸出簡體，閘道自動轉台灣繁體字形（只轉字、不換詞）。0.6B 小很多、更快，合成語音測試與 1.7B 結果相同，真人語音略遜 |
| TEA-ASR-1.1 | llama.cpp | Q6_K 1.42 GB ＋ mmproj 0.36 GB | Qwen3-ASR-1.7B 的台灣微調（MIT）：原生繁體＋台灣用字。作者公布 CommonVoice zh-TW 錯誤率 3.58%（Qwen3-ASR 3.90%、Breeze 8.03%）。在 llama.cpp 下的小怪癖：新行程的第一句偶爾回空白（閘道會自動重試一次）、時間會寫成「3:00」、逗號較少 |
| Breeze-ASR-25 | whisper.cpp | q8_0 1.66 GB | MediaTek 台灣華語微調；不太輸出標點，由閘道依停頓補上；只需要 OpenWhispr 的引擎 |

下載指令與 sha256 見 [models/README.md](models/README.md)。**更換模型＝把檔案放進 `models\`，再從系統匣「模型」選它。**

三個 Qwen 系模型都會把台灣人說的「網路」寫成「網絡」這類大陸用詞。閘道用 `data\taiwan-lexicon.txt` 改回來——預設只有十來個台灣人不會說出口的詞（網絡→網路、軟件→軟體、服務器→伺服器…），可自行增刪或清空；這和 OpenWhispr 那種整表改寫是兩回事。

實測（RTX 5080、13 秒合成音檔）：TEA-ASR-1.1 Q6_K 見下方「效能」；Breeze q8_0 冷啟動首句約 2.2 秒、之後約 0.75 秒，載入時 VRAM 約 +2.3 GB；純 CPU 時 TEA-ASR 約 4.7 秒、Breeze 約 8.5–10 秒。

## 相依項目（**不內附**，請自行準備）

這個 app 本身只有幾十 KB；大的東西（引擎、ffmpeg、模型）不隨附、也不轉散布，啟動時依下列順序自動尋找。缺少時，系統匣選單會以紅字顯示缺什麼。

| 相依 | 需求 | 尋找順序 | 怎麼取得 |
|---|---|---|---|
| **Node.js** | 18.2 以上（實測 26.7） | `runtime\node.exe` → PATH | <https://nodejs.org> |
| **llama.cpp**（GGUF 模型用） | `llama-server`，b11178 以上（Qwen3-ASR 支援於 2026-04 併入） | `engine\llama-cuda\` → `engine\llama-vulkan\` → OpenWhispr 的 `%APPDATA%\open-whispr\bin\llama-vulkan\` → `engine\llama-cpu\` → OpenWhispr 內附的 CPU 版 | 從 <https://github.com/ggml-org/llama.cpp/releases> 下載 `llama-bNNNN-bin-win-vulkan-x64.zip`（約 32 MB，任何廠牌 GPU）解壓到 `engine\llama-vulkan\`；或 `-cuda-12.4-x64.zip`＋`cudart-llama-bin-win-cuda-12.4-x64.zip` 一起解壓到 `engine\llama-cuda\`。**只裝了 OpenWhispr 也能跑（用它內附的 CPU 版），只是慢。** |
| **whisper.cpp**（ggml 模型用） | `whisper-server`；CUDA 版需 NVIDIA 運算能力 6.1+ | `engine\whisper-cuda\` → `%APPDATA%\open-whispr\bin\whisper-cuda\`（Vulkan、CPU 類推） | **安裝 OpenWhispr → 設定 → 語音轉文字 → 本機 →「啟用 GPU」**（約 770 MB），閘道直接借用；或從 <https://github.com/OpenWhispr/whisper.cpp/releases> 自取 |
| **ffmpeg** | 4.0 以上、含 opus 解碼（實測 6.1.1）。OpenWhispr 送 webm/opus，兩個引擎都只讀 wav | `engine\ffmpeg\ffmpeg.exe` → PATH → OpenWhispr 內附的 ffmpeg-static | 裝了 OpenWhispr 就有；或 `winget install Gyan.FFmpeg` |
| **模型** | 見上表 | `models\` | [models/README.md](models/README.md) |
| OpenCC 字典（簡→繁用） | 3 個文字檔約 1 MB（Apache-2.0） | `data\opencc\` | 第一次需要時閘道自動從 OpenCC 的 GitHub 抓；離線時簡體輸出就不轉換並在選單提示 |

> 借用 OpenWhispr 的引擎時，若在 OpenWhispr 裡刪除 GPU 引擎或解除安裝 OpenWhispr，閘道會退回 CPU 或無法辨識。

## 安裝

1. 準備上表的相依（Node.js、OpenWhispr、llama.cpp 解壓到 `engine\`、模型下載到 `models\`）。
2. 取得程式：從 **Releases** 下載 `WhisprGateway-vX.Y.Z.zip` 解壓即可——那個 zip 是 GitHub Actions 從該版本標籤的原始碼自動編譯、打包的（發佈說明附 sha256）。也可以自己編譯：clone 之後執行 `build.ps1`（用 Windows 內建的 C# 編譯器，不需要安裝 SDK）。
3. 執行 `WhisprGateway.exe`。系統匣出現「語」圖示（Windows 11 預設收在「^」裡，可拖到工作列）；有缺東西會以紅字提示。第一次執行會由 `gateway.config.default.json` 產生 `gateway.config.json`；預設模型是 Breeze，到「模型」選單改成你下載的即可。
4. 點圖示 →「OpenWhispr 要填什麼…」，照著在 OpenWhispr 填：
   - 設定 → 語音轉文字 → **自架主機** → 端點 URL `http://127.0.0.1:8790/v1`（API Key、模型名稱留空）。
   - 偏好設定 → 轉錄語言 → **自動**；中文書寫（語音輸入）→ **保持轉錄原樣**。**不要選「中文（繁體）」**——那會讓 OpenWhispr 用「台灣用語」表改寫你的詞（語言由閘道的 `language` 決定，選自動不影響辨識；繁體由模型或閘道負責）。
5. 要跟著系統啟動，就在選單勾「開機（登入）時自動啟動」。資料夾放哪裡都可以（設定用相對路徑）；搬動後執行一次 exe，自啟動會自動改指新位置。

### 給其他電腦用

勾選「允許其他電腦連線（區域網路／VPN）」後，閘道會綁定這台電腦**所有私有網段的位址**（區域網路、ZeroTier、Tailscale…），並只放行來自同一個網段的來源；沒勾時只聽 `127.0.0.1`。「OpenWhispr 要填什麼…」會列出每個位址和它的介面名稱——在另一台電腦的 OpenWhispr 填它連得到的那一個（`http://<位址>:8790/v1`）。

- 第一次開啟時 Windows 可能詢問是否允許 Node.js 通過防火牆，請只允許**私人網路**。
- **沒有 API Key**：安全性來自「只綁私有位址＋只放行同網段」。不要把這個埠轉發到網際網路。想再收緊，可在設定檔用 `remoteInterfaces`（介面名稱的正規表示式，例如 `zerotier`）限定只走某個介面。
- OpenWhispr 的自架模式只允許對私有位址用純 http，這正好符合。

## 系統匣選單

| 項目 | 作用 |
|---|---|
| 立即載入／立即卸載 | 手動控制模型是否佔用資源 |
| 閒置自動卸載 | 5／15／30／60 分鐘／不自動卸載（選這個時，閘道啟動 30 秒後會先把模型載好，重開機後第一句不用等冷載入） |
| 模型 | 列出 `models\` 內的 `*.bin` 與 `*.gguf`（mmproj 不列，自動配對） |
| 運算裝置 | 每張 NVIDIA GPU 各一項（CUDA 版引擎）、Vulkan（有該引擎才出現）、純 CPU。指定的 GPU 不在時（例如筆電省電模式移除獨顯）自動退到其他 GPU → Vulkan → CPU，並標示「目前實際使用」 |
| VRAM 不足時自動讓出 GPU（例如開遊戲時） | 見下節「讓出 GPU」；預設關閉。狀態列會顯示目前 VRAM 用量 |
| 自動補標點（依停頓切句，whisper 模型） | 見下節；預設開啟。Qwen3-ASR 系列自帶標點，不受影響 |
| 簡體輸出轉台灣繁體（Qwen3-ASR 系列） | 只對看起來是簡體的輸出做 OpenCC s2tw（字形轉換，**不做**台灣用語替換）；預設開啟。TEA-ASR 等原生繁體的輸出不會被動到 |
| 詞彙（Qwen3-ASR 系列） | 用記事本開啟 `data\vocabulary.txt`（詞彙提示，見下節）或 `data\taiwan-lexicon.txt`（用詞替換表）；存檔即生效 |
| 允許其他電腦連線（區域網路／VPN） | 見上節；預設關閉 |
| OpenWhispr 要填什麼… | 顯示端點網址，可一鍵複製 |
| 開機（登入）時自動啟動 | 建立／刪除工作排程器的 `WhisprGateway` 工作（登入後延遲 20 秒啟動，不需要系統管理員），並附一個每 5 分鐘的看門狗：程式被關掉或當掉時自動拉回來（已在執行時什麼都不做）。可用 `schtasks /Run /TN WhisprGateway` 當場測試；資料夾搬動後，下次執行 exe 會自動把工作指到新位置 |

圖示顏色：綠＝模型在 GPU 上、**藍＝目前用 CPU 辨識**（已讓出 GPU 給遊戲，或裝置選了 CPU）、橘＝載入中、灰＝未載入、紅＝閘道沒回應。

## 效能

RTX 5080、13 秒合成音檔、模型已載入時的每句耗時（含 webm→wav 轉檔）：

| 模型 | 載入（冷啟動，Vulkan） | 每句（已載入） | VRAM |
|---|---|---|---|
| Qwen3-ASR-1.7B Q8_0 | 約 3 秒 | 0.3–0.4 秒 | +3.2 GB |
| Qwen3-ASR-0.6B Q8_0 | 約 2 秒 | 0.25 秒 | +1.9 GB |
| TEA-ASR-1.1 Q6_K | 約 3 秒（本機第一次用 Vulkan 時 14 秒） | 0.3–0.8 秒 | +2.8 GB |
| Breeze-ASR-25 q8_0（whisper CUDA） | 約 2 秒 | 0.75 秒 | +2.3 GB |
| 純 CPU 參考 | — | Qwen3-ASR-0.6B 約 2 秒、TEA-ASR 約 4.7 秒、Breeze 約 8.5–10 秒 | 0 |

第一句要加上載入模型的時間（幾秒；重開機後第一次會更久，磁碟快取的關係）。

CUDA 版 llama.cpp 也測過（同一台、同一個模型）：每句耗時與 Vulkan 相同（0.15–0.30 秒），第一次載入反而要 22 秒。NVIDIA 卡用 Vulkan 版就夠，不必多下載 650 MB 的 CUDA 版。

## 標點符號（whisper 模型）

Breeze-ASR-25 本身幾乎不輸出標點（試過多種 prompt，最多換來零星的半形逗號）。但它的時間軸對齊很好：開啟時間軸後，**分段剛好切在子句邊界**（實測一段含 8 個標點的話切出 8 段、位置完全一致）。所以標點由閘道補：

- 分段結尾是 `嗎／呢／是不是／有沒有／好不好…` → `？`
- 下一段以 `另外／此外／對了／首先／其次／再來／最後／總之／接下來／順便／話說` 開頭、或兩段之間停頓 ≥ 0.7 秒、或最後一段 → `。`
- 其餘 → `，`；並把半形 `, ? !` 轉全形、去掉中文與數字間多餘的空格。純英文的分段不動。

規則刻意保守（錯的逗號比錯的句號好讀），在 `whisper-gateway.js` 的 `QUESTION_END`／`SENTENCE_OPENER`／`LONG_PAUSE_SECONDS`，歡迎調整。Qwen3-ASR 系列自己會輸出標點，走的是另一條路徑。

## 讓出 GPU（VRAM 不足時自動退避）

開遊戲、LM Studio 或 Stable Diffusion 這類吃 VRAM 的程式時，閘道可以自動把模型從 GPU 卸下，讓給它們：

- 判斷依據是「**有沒有別的程式，自己一個就佔了一大塊 VRAM**」：每 10 秒用 Windows 內建的 `typeperf` 讀「GPU Process Memory」計數器（約 1 秒、幾乎不耗 CPU）。任何單一程式 ≥ 2 GB（`vramGuardProcessMiB`）且連續兩次都在 → 卸載模型、讓出 GPU，系統匣會顯示是誰（例如「Endfield 佔用 7.9 GB」）。遊戲通常 4–8 GB；桌面上的程式各自都不到 1 GB。`dwm`（桌面合成，計數器本身也不準）和 NVIDIA 的 Overlay 預設忽略——Overlay 掛進遊戲時自己會漲到 2.5 GB，實測《崩壞：星穹鐵道》本身只用 1.5 GB、還剩 9.6 GB，卻因為 Overlay 被當成大戶；其他不該算的程式用 `vramGuardIgnore`（正規表示式）追加。
- 另一個觸發條件：剩餘 VRAM < 1 GB（`vramGuardMinFreeMiB`）持續 10 秒。
- 讓出期間聽寫**改用 CPU**（不碰 VRAM），而且改用較小的模型（`vramGuardCpuModel`，預設 `Qwen3-ASR-0.6B-Q8_0.gguf`，檔案在才用）；設「不自動卸載」時，讓出 90 秒後（遊戲載入完）先把 CPU 模型預載好。實戰量到：遊戲載入中用 1.7B 跑 CPU，第一句要 39 秒。
- 那個大戶不在了、剩餘 VRAM 也夠放回模型，持續 60 秒（`vramGuardResumeSeconds`）→ 回到 GPU；若設「不自動卸載」會自動重新載入。

實測《明日方舟：終末地》（RTX 5080）：遊戲行程最高吃到 7.9 GB、總用量 10.5 GB，閘道在遊戲期間完全沒搶 VRAM。

不需要遊戲清單：看的是 VRAM 的實際持有者，所以 LM Studio、Stable Diffusion 這類非遊戲程式也算。早期版本看的是「總用量漲了多少」，實際使用時兩頭都出錯——螢幕喚醒後桌面程式一起把 VRAM 要回去（+1.9 GB）就誤判成被搶，遊戲關掉後留下的 Overlay 又讓它回不來——所以改成看單一程式。

想看它實際怎麼動作，開一個 Git Bash 跑 `bash tools/watch-guard.sh`：狀態一變就印一行（誰是大戶、何時讓出、何時回來、每句聽寫花多久），並附上當下佔 VRAM 最多的幾個程式。

## 詞彙提示（Qwen3-ASR 系列）：讓台灣腔的英文寫回英文

台灣人講「CUDA」，模型常聽成中文音節寫成「酷達」。Qwen3-ASR 訓練時就會參考「上下文詞彙」，所以閘道把 `data\vocabulary.txt` 裡的詞連同一句指示（`gateway.config.json` 的 `llamaInstruction`）當 system prompt 送給模型——實測「酷達」就變回「CUDA」，純中文的句子不受影響。系統匣「詞彙 → 編輯詞彙提示…」會用記事本打開這個檔，一行一個詞，存檔後下一句就生效。這等於補上 OpenWhispr 自訂字典在自架模式送不出來的功能。

技術細節：llama-server 的轉錄端點沒有提示欄位，所以閘道改走 `/v1/chat/completions`（音訊＋system prompt）。微調版（如 TEA-ASR）對 chat 端點常回空白，閘道偵測到就自動改回轉錄端點。目前提示救不回的：多音節的音譯（「拉馬點西批批」→ llama.cpp）、型號數字（RTX 5080 有時寫成「五千零八十」）。

兩個實測出來的用法：

- **詞彙表要短、重要的放前面**。12 個詞加到 20 個時新詞都生效（A770、mATX、Overlay），加到 28 個時原本救得回來的「CUDA」又變回「酷達」——詞越多，每個詞分到的注意力越少。只留你真的會講的，二十個上下為宜。
- **型號裡的數字交給替換表**。模型常把數字寫成國字，提示改不動它，就在 `data\taiwan-lexicon.txt` 加一行「模型寫法、Tab、改成」，例如 `DLSS 五`→`DLSS 5`、`RTX 五千零八十`→`RTX 5080`（替換後的文字可以含空白）。

## 自架模式的取捨

沒有「即時轉錄預覽」；OpenWhispr 內的自訂字典不會送出——Qwen3-ASR 系列用上面的詞彙提示代替，whisper 模型則可把專有名詞加在 `gateway.config.json` 的 `prompt`。

## 設定檔 `gateway.config.json`

| 欄位 | 說明 |
|---|---|
| `port` / `backendPort` | 對外 8790／內部 8791（刻意避開 OpenWhispr 自己的 8178–8199） |
| `listen` | `"auto"`＝127.0.0.1＋（允許外部連線時）本機所有私有 IPv4 位址；也可寫成位址陣列 |
| `remoteInterfaces` | 介面名稱的正規表示式，空字串＝全部（例：`zerotier|tailscale`） |
| `allowCidrs` | `"auto"`＝上述介面所在網段；也可寫成 CIDR 陣列 |
| `allowRemote`、`idleMinutes`、`device`、`modelFile`、`punctuation`、`convertSimplified`、`vramGuard` | 由系統匣選單寫入 |
| `vramGuardProcessMiB`、`vramGuardMinFreeMiB`、`vramGuardResumeSeconds` | 讓出 GPU 的門檻：單一程式佔用多少 MiB 算大戶（預設 2000）、剩餘低於多少 MiB（預設 1024）、大戶離開後等幾秒才回到 GPU（預設 60） |
| `vramGuardIgnore` | **額外**不算大戶的程式名稱（正規表示式，不含 .exe，例如 `^(chrome|obs64)$`）。內建名單 `dwm`、`csrss`、`NVIDIA Overlay`、`NVIDIA Share`、`NVIDIA app`、`nvcontainer` 一律忽略 |
| `vramGuardCpuModel` | 讓出 GPU 期間在 CPU 上用的模型檔（預設 `Qwen3-ASR-0.6B-Q8_0.gguf`，不存在就用原本的模型；空字串＝一律用原本的） |
| `language` | 辨識語言：whisper 用代碼（`zh`），Qwen3-ASR 自動換成名稱（`Chinese`）；`auto` 為自動偵測 |
| `prompt` | 只給 whisper 模型的提示詞 |
| `llamaInstruction` | 給 Qwen3-ASR 系列的指示句（system prompt 第一行）；預設「以下是台灣人講的中文，夾雜英文術語時請保留英文原文；數字與型號請用阿拉伯數字。」 |
| `llamaMode` | `chat`（預設，會帶詞彙提示）或 `transcriptions`（不帶提示的純轉錄端點） |
| `llamaContext`、`llamaMaxTokens` | llama-server 的 context 長度（預設 8192，約 8 分鐘音訊）與單次輸出上限（預設 2048） |
| `modelsDir`、`engineDir`、`dataDir`、`tmpDir` | 相對路徑＝相對於本資料夾 |
| `data\vocabulary.txt`、`data\taiwan-lexicon.txt` | 詞彙提示、用詞替換表（非設定檔欄位；第一次執行自動產生，可從系統匣「詞彙」編輯） |

**執行中只有閘道會寫這個檔**；要手改請先從選單「結束」，改完再開。`/control/*`（系統匣用的控制介面）只接受本機呼叫。`gateway.log` 只記時間、來源 IP、耗時，不記辨識內容。

## 其他

- [docs/openwhispr-notes.md](docs/openwhispr-notes.md)：做這個專案時整理的 OpenWhispr 1.9.1 行為筆記（固定模型槽位、GPU 失敗旗標、Vulkan 裝置釘選、自架模式的請求格式、OpenCC twp 改詞…）。
- `tools\`：離線測試腳本（用 Windows 內建 TTS 合成測試音檔）。
- `src\GatewayTray.cs`：系統匣程式原始碼（C# 5 語法）。重編前要先結束 exe；開著自動啟動時，看門狗會在 5 分鐘內把它拉回來而鎖住檔案，所以先 `schtasks /Change /TN WhisprGateway /DISABLE`，編完再 `/ENABLE` 和 `/Run`。`WhisprGateway.exe --render-info out.png` 可把資訊視窗畫成圖檢查版面，`--render-icons out.png` 則把五種狀態的圖示畫成一張圖。
- 已知限制：僅 Windows；自啟動是「登入時」而非開機未登入時；重開機後第一次載入模型較慢；whisper-server 的 `--prompt` 在 Windows 會被 ANSI code page 弄壞，所以中文 prompt 由閘道以 UTF-8 multipart 欄位注入；Qwen3-ASR 回覆開頭的 `language Chinese<asr_text>` 標記由閘道去掉。

## 致謝

[OpenWhispr](https://github.com/OpenWhispr/openwhispr)、[llama.cpp](https://github.com/ggml-org/llama.cpp)、[whisper.cpp](https://github.com/ggml-org/whisper.cpp)、[Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR)、[JacobLinCool 的 TEA-ASR](https://huggingface.co/JacobLinCool/TEA-ASR-1.1)、[mradermacher 的 GGUF 量化](https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF)、[MediaTek Research Breeze-ASR-25](https://huggingface.co/MediaTek-Research/Breeze-ASR-25)、[shdennlin 的 ggml 轉檔](https://huggingface.co/shdennlin/breeze-asr-25-ggml)、[OpenCC](https://github.com/BYVoid/OpenCC)。
