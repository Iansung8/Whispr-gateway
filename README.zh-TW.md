[English](README.md) | 繁體中文

# WhisprGateway

**OpenWhispr 用的隨需載入語音辨識閘道**：讓 [OpenWhispr](https://github.com/OpenWhispr/openwhispr) 用你自選的本機模型做聽寫，可以是 Qwen3-ASR 系列（GGUF）或 whisper.cpp 模型。有聽寫才載入模型，閒置 15 分鐘（可調）後卸載，不會整天佔著 VRAM。可以從系統匣切換模型、顯示卡或 CPU；可選擇在遊戲需要 VRAM 時自動讓出 GPU；也能給區域網路或 VPN 上的其他電腦用。介面有繁體中文與英文。

非官方專案，與 OpenWhispr、阿里 Qwen、MediaTek 無關。開發與測試環境：Windows 11、OpenWhispr 1.9.1、Node 26.7；顯示卡為 NVIDIA RTX 5080，以及 RTX 5070 Ti Laptop 搭 Intel 內顯的筆電。AMD 與 Intel Arc 顯示卡應可透過 Vulkan 使用，但沒有實測。

## 為什麼需要它

OpenWhispr 1.9.1 的本機模式只能用內建的 6 個 Whisper 模型。App 一啟動，模型就常駐在 GPU，也不能給別台電腦用。選「中文（繁體）」時，它還會用 OpenCC 的「台灣用語」表改寫你的用詞（`數據→資料`、`參數→引數`…，見 [docs/openwhispr-notes.md](docs/openwhispr-notes.md)）。這個閘道補上這些，OpenWhispr 本身不用改，只要把轉錄模式切到「自架主機」並指向這裡。

```
OpenWhispr（模式＝自架主機；同一台或別台電腦）
   └─ POST http://<這台的位址>:8790/v1/audio/transcriptions
        └─ whisper-gateway.js（Node，常駐、幾乎不耗資源）
             └─ 有請求才啟動辨識伺服器（127.0.0.1:8791）＋模型，閒置後結束
                  *.gguf → llama.cpp llama-server（Qwen3-ASR / TEA-ASR）
                  *.bin  → whisper.cpp whisper-server（Breeze-ASR-25 / Whisper）
WhisprGateway.exe = 系統匣圖示：看管閘道 + 手動控制
```

## 快速開始

最短的路徑：推薦的模型，透過 Vulkan 跑在任何廠牌的 GPU 上。

1. 從 <https://nodejs.org> 安裝 **Node.js 20 LTS 以上**。
2. 從 **Releases** 下載 `WhisprGateway-vX.Y.Z.zip`，解壓到你有寫入權限的資料夾，例如 `%LOCALAPPDATA%\WhisprGateway`。不要放在 `Program Files`：設定檔和記錄檔都寫在 exe 旁邊。
3. 從 <https://github.com/ggml-org/llama.cpp/releases> 下載 llama.cpp 的 Vulkan 版 `llama-bNNNN-bin-win-vulkan-x64.zip`（b11178 以上），解壓後要讓 `engine\llama-vulkan\llama-server.exe` 存在。
4. 在 `models` 資料夾開 PowerShell，執行 [models/README.zh-TW.md](models/README.zh-TW.md) 的前兩行 `curl.exe`（模型和它的音訊編碼器，合計 2.5 GB）。
5. ffmpeg：裝了 OpenWhispr 就有；這台沒有 OpenWhispr 時執行 `winget install Gyan.FFmpeg`。
6. 執行 `WhisprGateway.exe`。exe 沒有數位簽章，SmartScreen 可能先擋一下：按「其他資訊」→「仍要執行」（或自己編譯，見「從原始碼編譯」）。系統匣會出現一個寫著「語」的圓形圖示；Windows 11 可能把它收在 ^ 箭頭裡。
7. 點圖示 →「OpenWhispr 要填什麼…」，照著在 OpenWhispr 填：
   - 設定 → 語音轉文字 → **自架主機** → 端點 URL `http://127.0.0.1:8790/v1`（API 金鑰、模型名稱留空）。
   - 偏好設定 → 轉錄語言 → **自動**。說中文的話，中文書寫（語音輸入）也選 **保持轉錄原樣**；選「中文（繁體）」會讓 OpenWhispr 改寫你的用詞。
8. 開始聽寫。第一句要等模型載入（幾秒）；剛裝好時的第一句可能要 20 秒以上，因為顯示卡驅動程式要先編譯 shader。

要跟著系統啟動，就在選單勾「開機（登入）時自動啟動」。介面語言跟隨 Windows 顯示語言，選單的「語言 / Language」可以切換。

做完以上步驟，資料夾會長這樣：

```
WhisprGateway\
  WhisprGateway.exe、whisper-gateway.js、gateway.config.default.json、README.md …
  gateway.config.json、gateway.log            第一次執行時產生
  engine\llama-vulkan\llama-server.exe        ＋那個 zip 裡的其他檔案
  models\Qwen3-ASR-1.7B-Q8_0.gguf
  models\mmproj-Qwen3-ASR-1.7B-Q8_0.gguf
  data\                                       第一次執行時產生（詞彙、字典）
```

## 模型

| 模型 | 引擎 | 大小 | 特點 |
|---|---|---|---|
| **Qwen3-ASR-1.7B**（推薦） | llama.cpp | Q8 2.17 GB 或 Q4_K_M 1.28 GB ＋ mmproj 0.36 GB | 30 種語言、自動判斷語言、自帶標點；台灣口音與中英夾雜都不需微調。中文輸出簡體，由閘道轉成台灣繁體字形（只轉字、不換詞） |
| Qwen3-ASR-0.6B | llama.cpp | 0.80 GB ＋ mmproj 0.21 GB | CPU 上最快，錯字約是 1.7B 的兩倍 |
| TEA-ASR-1.1 | llama.cpp | Q6_K 1.42 GB ＋ mmproj 0.36 GB | Qwen3-ASR-1.7B 的台灣華語微調，原生繁體＋台灣用字 |
| Breeze-ASR-25 | whisper.cpp | q8_0 1.66 GB | MediaTek 以 Whisper 微調的台灣華語模型；標點由閘道依停頓補上；只需要 OpenWhispr 的引擎 |

GGUF（`*.gguf`）是 llama.cpp 的模型格式，ggml（`*.bin`）是 whisper.cpp 的。Qwen3-ASR 系列一定要把 **mmproj** 檔（音訊編碼器）放在旁邊。Q8、Q6_K、Q4_K_M 是量化等級：數字越大，檔案越大，也越接近原始準確度。下載指令與 sha256 見 [models/README.zh-TW.md](models/README.zh-TW.md)；各模型的實測比較見 [docs/asr-model-comparison.md](docs/asr-model-comparison.md)。要換模型，就把檔案放進 `models\`，再從系統匣的「模型」選它。

## 相依項目詳細說明

大型檔案一律不隨附，程式本身約 140 KB。啟動時依下列順序尋找各項相依；路徑都是相對於 `WhisprGateway.exe` 所在的資料夾。

| 相依 | 需求 | 尋找順序 | 怎麼取得 |
|---|---|---|---|
| **Node.js** | 20 以上 | `runtime\node.exe` → PATH | <https://nodejs.org> |
| **llama.cpp**（GGUF 模型用） | `llama-server`，b11178 以上 | `engine\llama-cuda\`（有的話）→ `engine\llama-vulkan\` → OpenWhispr 的 `%APPDATA%\open-whispr\bin\llama-vulkan\` → `engine\llama-cpu\` → OpenWhispr 內附的 CPU 版 | Vulkan 版 `llama-bNNNN-bin-win-vulkan-x64.zip` 適用任何廠牌的 GPU。NVIDIA 要用 CUDA：把 `llama-bNNNN-bin-win-cuda-12.4-x64.zip` 與 `cudart-llama-bin-win-cuda-12.4-x64.zip` 解壓到 `engine\llama-cuda\`。只裝了 OpenWhispr 也能跑，但只能用 CPU，比較慢 |
| **whisper.cpp**（ggml 模型用） | `whisper-server-win32-x64-cuda.exe`／`-vulkan.exe`／`whisper-server-win32-x64.exe` | GPU 版：`engine\whisper-cuda\` 或 `engine\whisper-vulkan\` → OpenWhispr 的 `%APPDATA%\open-whispr\bin\whisper-cuda\` 或 `whisper-vulkan\`。CPU 版：`engine\whisper-cpu\` → OpenWhispr 的安裝資料夾 | OpenWhispr → 設定 → 語音轉文字 → 本機 →「啟用 GPU」，在 NVIDIA 上會裝 CUDA 版、其他廠牌裝 Vulkan 版，閘道直接借用；或從 <https://github.com/OpenWhispr/whisper.cpp/releases> 自取 |
| **ffmpeg** | 4.0 以上、含 opus 解碼 | `engine\ffmpeg\ffmpeg.exe` → PATH → OpenWhispr 內附的 ffmpeg-static | 裝了 OpenWhispr 就有；或 `winget install Gyan.FFmpeg` |
| **模型** | 見上表 | `models\` | [models/README.zh-TW.md](models/README.zh-TW.md) |
| OpenCC 字典（簡→繁用） | 3 個文字檔約 1 MB | `data\opencc\` | 第一次需要時自動從 OpenCC 的 GitHub 下載 |

> 借用 OpenWhispr 的引擎時，若在 OpenWhispr 裡刪除 GPU 引擎或解除安裝 OpenWhispr，閘道會退回 CPU 或無法辨識。

### 給其他電腦用

勾選「允許其他電腦連線（區域網路／VPN）」後，閘道會綁定這台電腦所有私有網段的位址，並只放行同網段的來源；沒勾時只聽 `127.0.0.1`。在另一台電腦的 OpenWhispr 填 `http://<位址>:8790/v1`。

- 第一次開啟時，Windows 防火牆會詢問是否允許 **Node.js**，請只允許**私人網路**。
- **沒有 API 金鑰**：同網段的任何人都能用，包括同一個咖啡店 Wi-Fi 上的人，以及同一個 Tailscale 或 ZeroTier 網路裡的所有裝置。在不信任的網路上請關掉這個選項，也不要把這個埠轉發到網際網路；可用 `remoteInterfaces` 限定只走某個介面。

## 系統匣選單

| 項目 | 作用 |
|---|---|
| 立即載入／立即卸載 | 手動控制模型是否佔用資源 |
| 閒置自動卸載 | 5／15（預設）／30／60 分鐘／不自動卸載；選「不自動卸載」時，閘道啟動約 30 秒後先把模型載好 |
| 模型 | 列出 `models\` 內的 `*.bin` 與 `*.gguf`（mmproj 自動配對） |
| 運算裝置 | 各張 NVIDIA GPU（CUDA）、Vulkan 自動選卡、Vulkan 指定某一張卡、純 CPU。見「指定顯示卡」 |
| 讓出 GPU 給其他程式 | 開關，以及「VRAM 用量、白名單與門檻」視窗。見「讓出 GPU」；預設關閉 |
| 自動補標點（依停頓切句，whisper 模型） | 見「標點符號」；預設開啟 |
| 簡體輸出轉台灣繁體（Qwen3-ASR 系列） | OpenCC s2tw：只轉字形，不做用語替換；預設開啟 |
| 詞彙（Qwen3-ASR 系列） | 開啟 `data\vocabulary.txt`（詞彙提示）或 `data\taiwan-lexicon.txt`（替換表）；存檔即生效 |
| 允許其他電腦連線 | 見上節；預設關閉 |
| OpenWhispr 要填什麼… | 顯示端點網址，可一鍵複製 |
| 開機（登入）時自動啟動 | 建立工作排程器的 `WhisprGateway` 工作（登入後 20 秒啟動，每 5 分鐘檢查一次是否還在執行） |
| 語言 / Language | 自動（跟隨 Windows 顯示語言）、繁體中文、English |
| 開啟記錄檔／重新啟動閘道／結束 | 用記事本開 `gateway.log`／重新啟動 Node 閘道／全部停止並釋放 GPU |

圖示顏色：綠＝模型在 GPU 上、藍＝目前用 CPU 辨識、橘＝載入中、灰＝未載入、紅＝閘道沒回應。

## 排除問題

缺東西時，系統匣選單會在狀態列下方用紅字顯示，一次一項；點它會開啟這份 README。修好一項，下一項（如果有）才會出現。

| 訊息或狀況 | 怎麼處理 |
|---|---|
| 找不到模型：models\… | 把那個檔案放進 `models\`，或從「模型」選一個你有的 |
| 缺少 … 的音訊編碼器 | 從模型的 Hugging Face 頁面下載 `mmproj-*.gguf` 放進 `models\` |
| 找不到 llama.cpp 引擎／找不到辨識引擎 | 照快速開始第 3 步（whisper 模型則安裝 OpenWhispr 並啟用 GPU 引擎） |
| 沒有 GPU 引擎（…），暫時改用 CPU | 仍可聽寫，只是慢；照第 3 步裝 Vulkan 版 |
| GPU 引擎啟動失敗，暫時改用 CPU | 更新顯示卡驅動程式，看「開啟記錄檔」，再重選運算裝置或「重新啟動閘道」 |
| 找不到指定的顯示卡（…），暫時自動選卡 | 「運算裝置」選的那張卡不在了（例如外接顯卡拔掉）；改選另一張 |
| 找不到 ffmpeg | 照快速開始第 5 步 |
| 簡繁字典下載失敗 | 第一次需要連網；或關掉「簡體輸出轉台灣繁體」 |
| 讀不到各程式的 VRAM 用量（typeperf） | 這時讓出只會在 VRAM 快滿時動作。GPU 計數器需要 Windows 10 1709 以上；計數器壞掉時，在系統管理員命令提示字元執行 `lodctr /r` 可以重建 |
| 圖示紅色、「閘道未回應」 | 沒裝 Node.js（會跳通知），或 8790 埠被別的程式佔用；看「開啟記錄檔」 |
| 啟動時跳出「無法在這個資料夾建立設定檔」 | 資料夾沒有寫入權限；把程式移出 `Program Files` |

## 效能

RTX 5080 實測。「載入」是從第一個請求到模型就緒的時間，「每句」是模型已載入後的時間。

| 模型 | 載入 | 每句 | VRAM |
|---|---|---|---|
| Qwen3-ASR-1.7B Q8_0 | 約 3 秒 | 0.3–0.4 秒 | 約 3 GB |
| Qwen3-ASR-0.6B Q8_0 | 約 2 秒 | 0.25 秒 | 約 1.9 GB |
| TEA-ASR-1.1 Q6_K | 約 3 秒 | 0.3–0.8 秒 | 約 2.8 GB |
| Breeze-ASR-25 q8_0 | 約 2 秒 | 0.75 秒 | 約 2.3 GB |

CPU（i5-13600K，10 執行緒）上，Qwen3-ASR-1.7B Q4_K_M 處理 17 秒的一句話約 2.8 秒。閘道本身（系統匣＋Node）約佔 60 MB RAM。

## 功能說明

### 讓出 GPU

開遊戲、LM Studio 這類吃 VRAM 的程式時，閘道可以卸下模型，聽寫改用 CPU；那個程式結束後再回到 GPU。這項功能預設關閉，在「讓出 GPU 給其他程式」裡開啟。

閘道每 10 秒讀一次 Windows 的 GPU 記憶體計數器，任何廠牌的顯示卡都有這些計數器。它只看模型所在的那張卡，所以另一張卡上的遊戲不算。符合下面任一條件就讓出：

- 有單一程式的 VRAM 佔用超過門檻，連續兩次取樣都是，大約 10–20 秒。預設 2 GB。
- 模型在 GPU 上時，剩餘 VRAM 低於門檻超過 10 秒。預設 1 GB；設成 0 就不用這個條件。

兩個門檻都可以寫成 GB，或寫成佔整張卡的百分比（例如 15%）；顯示卡大小不同時，百分比比較好設。讓出期間聽寫改用 CPU，並換成較小的檔案（`vramGuardCpuModel`）：預設先找 `Qwen3-ASR-1.7B.Q4_K_M.gguf`，再找 `Qwen3-ASR-0.6B-Q8_0.gguf`，都沒有就用原本的模型。佔用的程式結束後，等 60 秒（可調）才回到 GPU。

系統匣 →「讓出 GPU 給其他程式」→「VRAM 用量、白名單與門檻…」會開一個視窗，每 3 秒列出這張卡上每個程式的 VRAM 用量與佔比，並標出哪個程式超過門檻、哪個觸發了讓出。兩個門檻和等待秒數也在這個視窗設定。

**白名單**：在清單上勾選一個程式，它佔再多 VRAM 也不會觸發讓出，適合常開的 OBS、瀏覽器這類程式。沒在執行的程式也可以用名稱加入。白名單預設是空的，由你自己決定。Windows 自己的桌面程式（dwm、csrss）一律不計入，因為它們的計數器會隨螢幕數量與解析度虛增。

模型放在內顯上時不會讓出：內顯用的是系統記憶體，不會跟遊戲搶 VRAM。閘道判斷內顯的依據是 Windows 回報這張卡的專用記憶體不到 1 GB，Intel 內顯符合；保留超過 1 GB 的 AMD APU 會被當成獨顯。

### 指定顯示卡

「運算裝置」列出 Vulkan 引擎看得到的每一張卡，包括內顯。有兩張獨顯時，llama.cpp 預設會把模型拆到兩張卡上；指定一張就只用那一張。也可以把模型放在內顯，讓獨顯完全留給遊戲。

Vulkan 的卡號會隨顯示卡當下的狀態改變：在測試用的筆電上，Intel 與 NVIDIA 兩張卡在幾分鐘內互換了編號。所以設定檔記的是卡名（`vulkan:<卡名>`），每次啟動模型前才重新對應到當下的卡號。兩張同名的卡無法區分，會用第一張。這項功能只適用 Qwen3-ASR 系列（llama.cpp）；whisper 模型的 Vulkan 由 whisper.cpp 自己選卡。

### 詞彙提示（Qwen3-ASR 系列）

閘道把一份詞彙表當 system prompt 送給模型，讓英文術語寫回英文（例如「酷達」→ CUDA）。

- 內建 35 個詞：AI 工具、GPU 與開發用語，例如 VRAM、NVIDIA、repo、GitHub、Claude（清單在 `whisper-gateway.js` 的 `BUILTIN_VOCABULARY`；`builtinVocabulary: false` 可關）。
- `data\vocabulary.txt` 放你自己常講、而模型常寫錯的詞，一行一個。
- 不要放模型本來就寫得對的短詞（GPU、AI、API）：列出來的詞會把發音相近的字吸過去。總數上限 100。

### 縮寫與數字（Qwen3-ASR 系列）

- 被拆開的縮寫會合併：`G P U`→`GPU`、`N V I D I A`→`NVIDIA`
- 國字數字在確定是數字時轉成阿拉伯數字：`RTX 五零八零`→`RTX 5080`、`DLSS 五`→`DLSS 5`、`十六 GB`→`16 GB`、`三十秒`→`30秒`、`九月十一號`→`9月11號`、`百分之二十`→`20%`
- 不動的：`一個`、`兩顆`、`一點`、`萬一`、`十分`、`一二三`、`十幾個`

個別的詞可以用替換表 `data\taiwan-lexicon.txt` 修（每行「模型寫法、Tab、改成」），預設內容是把「網絡、軟件、服務器」這類用詞改回台灣說法。

### 用英文或其他語言聽寫

Qwen3-ASR 系列會自己判斷每段錄音的語言；2026-10-08 的測試中，Qwen3-ASR-1.7B 在預設設定下正確轉寫了一段英文錄音。只用英文聽寫時，建議在 `gateway.config.json` 設定（先從選單「結束」）：`"language": "auto"`（OpenWhispr 沒送語言時會用預設的 `zh`，影響 whisper 模型與 TEA-ASR）、`"llamaInstruction": ""`（預設的指示句告訴 Qwen3-ASR 要聽台灣中文）、`"prompt": ""`（給 whisper 模型的中文提示）。要簡體中文的話，關掉「簡體輸出轉台灣繁體」。其餘的文字整理只動中文，唯一例外是逐字母念的縮寫會合併（`G P U`→`GPU`）。

### 標點符號（whisper 模型）

Breeze-ASR-25 幾乎不輸出標點，但它的分段剛好切在子句邊界，所以由閘道補：疑問結尾（嗎／呢／是不是…）→「？」；下一段以「另外／首先／最後…」開頭、停頓 ≥ 0.7 秒或最後一段 →「。」；其餘 →「，」。Qwen3-ASR 系列自帶標點。

## 設定檔 `gateway.config.json`

| 欄位 | 說明 |
|---|---|
| `port` / `backendPort` | 對外 8790／內部 8791 |
| `listen` | `"auto"`＝127.0.0.1＋（允許外部連線時）本機所有私有 IPv4 位址；也可寫成位址陣列 |
| `remoteInterfaces` | 介面名稱的正規表示式，空字串＝全部（例：`zerotier|tailscale`） |
| `allowCidrs` | `"auto"`＝上述介面所在網段；也可寫成 CIDR 陣列 |
| `device` | `gpu:N`（CUDA 第 N 張 NVIDIA 卡）、`vulkan`（自動選卡）、`vulkan:<卡名>`、`cpu`。預設 `gpu:0`：裝了 CUDA 版就用第一張 NVIDIA 卡，否則用 Vulkan，再沒有就用 CPU |
| `allowRemote`、`idleMinutes`、`modelFile`、`punctuation`、`convertSimplified`、`uiLanguage`、`vramGuard` | 由系統匣選單寫入 |
| `vramGuardProcess`、`vramGuardMinFree` | 讓出 GPU 的兩個門檻：數字＝MiB，字串 `"15%"`＝佔整張卡的比例（預設 2048、1024） |
| `vramGuardResumeSeconds` | 佔用的程式結束後，等幾秒回到 GPU（預設 60） |
| `vramGuardWhitelist` | 不觸發讓出的程式名稱（不含 .exe），由 VRAM 視窗寫入；預設空 |
| `vramGuardIgnore` | 進階：用正規表示式排除程式，與白名單並用 |
| `vramGuardCpuModel` | 讓出 GPU 期間在 CPU 上用的模型檔；空字串＝用原本的模型 |
| `builtinVocabulary` | `false`＝不送內建詞彙 |
| `language` | OpenWhispr 沒有指定語言時的辨識語言（`zh`、`en`、`auto`…） |
| `prompt` | 只給 whisper 模型的提示詞 |
| `llamaInstruction` | 給 Qwen3-ASR 系列的指示句（system prompt 第一行） |
| `llamaMode` | `chat`（預設，會帶詞彙提示）或 `transcriptions` |
| `llamaContext`、`llamaMaxTokens` | llama-server 的 context 長度（預設 8192）與單次輸出上限（預設 2048） |
| `modelsDir`、`engineDir`、`dataDir`、`tmpDir` | 相對路徑＝相對於本資料夾 |

執行中只有閘道會寫這個檔。要手改請先從選單「結束」；開了「開機（登入）時自動啟動」的話，工作排程器會在 5 分鐘內把程式叫回來，請在那之前改完，或先關掉這個選項。`/control/*`（系統匣用的本機控制介面）只接受本機呼叫。`gateway.log` 只記時間、來源 IP、耗時，不記辨識內容。

## 從原始碼編譯

- `powershell -ExecutionPolicy Bypass -File build.ps1` 會用 Windows 內建的 C# 編譯器（.NET Framework 4，不需要 SDK）把 `src\GatewayTray.cs` 編成 `WhisprGateway.exe`；`whisper-gateway.js` 直接執行，不用編譯。
- 程式由「開機（登入）時自動啟動」執行時，重編前先 `schtasks /Change /TN WhisprGateway /DISABLE` 並結束 exe，編完再 `/ENABLE`、`/Run`。
- `WhisprGateway.exe --render-guard 檔名.png --sample --lang zh-TW` 可以把 VRAM 視窗畫成圖檔檢查版面。
- `tools\`（只在原始碼裡，Release 的 zip 沒有）放測試與觀察用的腳本；`bash tools/watch-guard.sh` 可即時觀察讓出的動作，需要 Git Bash、Python 與 curl。

## 其他

- 自架模式沒有「即時轉錄預覽」，OpenWhispr 內的自訂字典也不會送出（用上面的詞彙提示代替）。
- 僅支援 Windows；自啟動是「登入時」。

## 致謝

[OpenWhispr](https://github.com/OpenWhispr/openwhispr)、[llama.cpp](https://github.com/ggml-org/llama.cpp)、[whisper.cpp](https://github.com/ggml-org/whisper.cpp)、[Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR)、[JacobLinCool 的 TEA-ASR](https://huggingface.co/JacobLinCool/TEA-ASR-1.1)、[mradermacher 的 GGUF 量化](https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF)、[MediaTek Research Breeze-ASR-25](https://huggingface.co/MediaTek-Research/Breeze-ASR-25)、[shdennlin 的 ggml 轉檔](https://huggingface.co/shdennlin/breeze-asr-25-ggml)、[OpenCC](https://github.com/BYVoid/OpenCC)。
