# 語音模型實測：台灣腔中英夾雜聽寫

> **English summary.** Measured on 2026-09-30 (RTX 5080, i5-13600K, Windows 11, no vocabulary hints) with two test sets: 203 real dictation clips from one Taiwanese speaker mixing Chinese and English (about 80 minutes, not published), and 126 lecture clips from the public NTU ML2021 set. On the GPU, Qwen3-ASR-1.7B Q8 made the fewest errors on dictation (1.8% character error rate) and won blind A/B comparisons against Breeze-ASR-25 (42 : 12) and TEA-ASR-1.1 (20 : 14); Breeze-ASR-25 and TEA-ASR-1.1 did best on the lectures, which share their training data. On the CPU, Qwen3-ASR-1.7B Q4_K_M kept almost all of the accuracy (2.2%) and needed 2.8 s for a 17-second sentence, which is why it is the default CPU fallback. A curated list of 35 tech terms raised the share of English terms written correctly from 83% to 88%; longer lists did not help and pulled similar-sounding words toward the listed ones. The tables below give all models and numbers.


測試日期 2026-09-30，RTX 5080＋i5-13600K，Windows 11。所有模型都不給詞彙提示。

## 考題與指標

| 考題 | 內容 | 標準答案 |
|---|---|---|
| 個人聽寫 | 一位台灣使用者的真實聽寫錄音，203 段、約 80 分鐘，中英夾雜（不公開） | 9 個模型的輸出對齊後多數決，分歧處逐一裁決 |
| 課堂錄音 | [ky552/ML2021_ASR_ST](https://huggingface.co/datasets/ky552/ML2021_ASR_ST) dev，接成 12–25 秒，126 段 | 資料集的人工逐字稿 |

- **錯字率**：字元錯誤率；計分前統一繁體與數字寫法，去掉標點、空白、語助詞與口吃重複。
- **術語**：標準答案裡的英文詞有多少出現在輸出裡。

個人聽寫的答案來自模型輸出，會偏向 Qwen3-ASR 家族，所以另做了盲測。課堂錄音與 TEA-ASR、Breeze-ASR-25 的訓練資料同源，對它們偏樂觀。

## GPU 上的模型

| 模型 | 大小 | 個人聽寫 錯字率 | 個人聽寫 術語 | 課堂 錯字率 | 課堂 術語 |
|---|---|---|---|---|---|
| **Qwen3-ASR-1.7B** Q8 | 2.17 GB | **1.8%** | 83% | 4.4% | 88% |
| Confucius4-R2T2 Q8 | 1.83 GB | 2.1% | 83% | 4.5% | 87% |
| TEA-ASR-1.1 Q6_K | 1.42 GB | 2.4% | 81% | 2.8% | 96% |
| Breeze-ASR-25 | 3.1 GB | 4.4% | 81% | **2.7%** | 99% |
| Whisper large-v3 | 3.1 GB | 4.8% | 83% | 4.7% | 91% |
| Whisper large-v3-turbo | 1.6 GB | 9.0% | 80% | 5.7% | 91% |
| GLM-ASR-Nano-2512 | 4.5 GB | 11.4% | 71% | 4.5% | 85% |
| Gemma 4 E4B Q4 | 4.6 GB | 15.0% | 72% | 6.8% | 89% |

盲測（兩個模型寫法不同之處打亂成 A／B，不看名稱判斷哪個對）：Qwen3-ASR-1.7B 對 Breeze-ASR-25 為 42 : 12，對 TEA-ASR-1.1 為 20 : 14，各有 36 處判不出來。

英文術語沒有哪個模型特別強，各自錯的詞不同；五個大模型任一個寫對就算對，也只有 93%。

## CPU 上的模型

10 執行緒、機器閒置時的速度。

| 模型 | 個人聽寫 錯字率 | 術語 | 課堂 錯字率 | 17 秒的一句話 | RAM |
|---|---|---|---|---|---|
| Qwen3-ASR-1.7B Q8 | 1.8% | 83% | 4.4% | 4.2 秒 | 4.1 GB |
| Qwen3-ASR-1.7B Q5_K_M | 2.0% | 83% | 4.4% | 3.6 秒 | 3.5 GB |
| **Qwen3-ASR-1.7B Q4_K_M** | 2.2% | 82% | 4.4% | 2.8 秒 | 4.0 GB |
| Qwen3-ASR-0.6B Q8 | 3.7% | 75% | 5.7% | 1.8 秒 | 2.8 GB |
| TEA-ASR-1.1-mini Q8 | 4.2% | 75% | 4.2% | 1.8 秒 | 2.8 GB |
| X-ASR zipformer（sherpa-onnx int8） | 4.0% | 76% | 5.4% | 0.5 秒 | 0.6 GB |
| SenseVoice Small（sherpa-onnx int8） | 6.1% | 50% | 9.4% | 0.3 秒 | 0.5 GB |
| Fun-ASR-Nano（sherpa-onnx int8） | 12.4% | 69% | 7.1% | 3.7 秒 | — |
| FireRedASR2-AED（sherpa-onnx int8） | — | — | 7.4% | 9 秒 | — |

## 詞彙提示（Qwen3-ASR-1.7B，個人聽寫）

| 提示內容 | 術語 |
|---|---|
| 不給 | 83% |
| 50 個通用科技詞 | 87% |
| 100 個 | 87% |
| 300 個 | 87%（4 段短錄音變成空白） |
| 35 個精選（閘道內建） | 88% |

列出的詞會吸走發音相近的字（列了 `AI`，「RL」變成「AI」），課堂錄音的錯字率因此由 4.4% 升到 4.9%。

## 未列入

- Voxtral Mini 3B、Gemma 4 E2B：中文聽寫不可用。
- Qwen2.5-Omni-7B：比 Qwen3-ASR 慢十倍以上且較不準，中止。
- NVIDIA Canary／Parakeet、IBM Granite Speech、Moonshine：不支援中文（依模型卡，未測）。
- FireRedASR2-LLM、MiMo-V2.5-ASR、Kimi-Audio、Step-Audio-2-mini、Phi-4-multimodal：超過 6 GB（未測）。
- Cohere Transcribe：需登入同意條款（未測）。

## 限制

- 個人聽寫只有一位講者；標準答案不是人工逐字聽打。
- Whisper／Breeze／GLM 用 transformers 執行，其餘用 llama.cpp 或 sherpa-onnx，GPU 速度不能跨引擎比較。
- sherpa-onnx 的模型是 int8 量化版。
