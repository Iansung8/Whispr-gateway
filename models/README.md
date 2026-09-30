# models

把模型放在這個資料夾，再從系統匣選單的「模型」選它。模型檔不進版控。兩種格式：

| 檔案 | 引擎 | 說明 |
|---|---|---|
| `*.bin`（whisper.cpp ggml） | `whisper-server` | Breeze-ASR-25、原版 Whisper 等 |
| `*.gguf` ＋ 同名 `mmproj-*.gguf` | `llama-server` | Qwen3-ASR 系列（Qwen3-ASR、TEA-ASR…）。**mmproj 是音訊編碼器，一定要一起放**；閘道依檔名自動配對（`Qwen3-ASR-1.7B-Q8_0.gguf` ↔ `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf`，或 `TEA-ASR-1.1.Q6_K.gguf` ↔ `TEA-ASR-1.1.mmproj-Q8_0.gguf`） |

下載後請比對 sha256（`(Get-FileHash <檔名> -Algorithm SHA256).Hash.ToLower()`）。

## 推薦：Qwen3-ASR-1.7B（阿里官方，輸出簡體 → 閘道自動轉台灣繁體）

[ggml-org/Qwen3-ASR-1.7B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF)（Apache-2.0；30 種語言＋22 種漢語方言／口音）。用 203 段真實的台灣腔聽寫錄音實測，它是比過的模型裡最準的（見 [docs/asr-model-comparison.md](../docs/asr-model-comparison.md)）。建議放兩個檔：**Q8 給 GPU**、**Q4_K_M 給 CPU 備援**（讓出 GPU 時自動改用；4-bit 幾乎不掉準確度，CPU 上快 1.5 倍）。兩者共用同一個 mmproj。

```powershell
curl.exe -L --fail --retry 5 -C - -o Qwen3-ASR-1.7B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B-Q8_0.gguf"
curl.exe -L --fail --retry 5 -C - -o mmproj-Qwen3-ASR-1.7B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/mmproj-Qwen3-ASR-1.7B-Q8_0.gguf"
curl.exe -L --fail --retry 5 -C - -o Qwen3-ASR-1.7B.Q4_K_M.gguf "https://huggingface.co/mradermacher/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B.Q4_K_M.gguf"
```

| 檔案 | bytes | sha256 |
|---|---|---|
| `Qwen3-ASR-1.7B-Q8_0.gguf` | 2,165,034,944 | `58e22d0532d4eacaf034cfac17a6fed159f37c41390c710186783be439d1fc57` |
| `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf`（必要） | 355,709,344 | `46c1d533af3f354ceb37ce855dbceff7da7fa7cf1e6a523df3b13440bd164c0d` |
| `Qwen3-ASR-1.7B.Q4_K_M.gguf`（CPU 備援；mradermacher 量化） | 1,282,435,552 | `3893b8926065bbff3da7586d21d8711a9b4fa4fa8f12cd0cefad58e31b2660b6` |

Qwen3-ASR-0.6B（[ggml-org/Qwen3-ASR-0.6B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF)）小很多、CPU 上最快，但真人語音的錯字約是 1.7B 的兩倍；只在你要最快的 CPU 備援時才需要：

| 檔案 | bytes | sha256 |
|---|---|---|
| `Qwen3-ASR-0.6B-Q8_0.gguf` | 804,749,248 | `bca259818b50ca7c4c05e9bdb35a5dc04fa039653a6d6f3f0f331f96f6aa1971` |
| `mmproj-Qwen3-ASR-0.6B-Q8_0.gguf` | 214,392,480 | `41a342b5e4c514e968cb756de6cd1b7be39eff43c44c57a2ef5fc6522e36603d` |

## TEA-ASR-1.1（台灣華語，原生繁體＋台灣用字，MIT）

[JacobLinCool/TEA-ASR-1.1](https://huggingface.co/JacobLinCool/TEA-ASR-1.1)：Qwen3-ASR-1.7B 以不到 10 小時的台灣公開語料微調（CommonVoice zh-TW、ASCEND、NTUML2021、TaiMECS），保留原本的多語能力。作者公布的錯誤率（%）：CommonVoice zh-TW **3.58**（Qwen3-ASR-1.7B 3.90、Breeze-ASR-25 8.03、Whisper-large-v3 10.17）、ASCEND 中英夾雜 **9.60**（10.57／17.53／19.61）、NTUML2021 課堂 **6.67**（10.12／7.50／9.68）。我們自己的實測：課堂錄音上它明顯比原版 Qwen3-ASR-1.7B 準（錯字率 2.8% 對 4.4%），個人聽寫上兩者相當（盲測 14 比 20）。GGUF 由 mradermacher 量化：

```powershell
curl.exe -L --fail --retry 5 -C - -o TEA-ASR-1.1.Q6_K.gguf "https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF/resolve/main/TEA-ASR-1.1.Q6_K.gguf"
curl.exe -L --fail --retry 5 -C - -o TEA-ASR-1.1.mmproj-Q8_0.gguf "https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF/resolve/main/TEA-ASR-1.1.mmproj-Q8_0.gguf"
```

| 檔案 | bytes | sha256 |
|---|---|---|
| `TEA-ASR-1.1.Q6_K.gguf` | 1,417,484,704 | `524fb1ecf59c44295a7f09a5bba08afe53731c1f25c9a9cdcbc87f7e68c84e42` |
| `TEA-ASR-1.1.Q5_K_M.gguf`（較小） | 1,257,609,632 | `6468008c56bf5253a333c2c0ba962e4c770b20e69b59ccdf308d9eb2db3fe623` |
| `TEA-ASR-1.1.Q8_0.gguf`（最完整） | 1,834,156,448 | `243c56e6cae05c047d42f1c7136cfa3e5b500b86b7d5d7fa44b3ca43cabfe35e` |
| `TEA-ASR-1.1.mmproj-Q8_0.gguf`（必要） | 355,709,856 | `76b4e355d7057b2799a512bebce9a0c1b1309fcd6b580400a7bddc268e4fd40a` |

## Breeze-ASR-25 q8_0（whisper.cpp，台灣華語＋中英夾雜，Apache-2.0）

只需要 OpenWhispr 的 GPU 引擎、不需要 llama.cpp。標點由閘道依停頓補上。

```powershell
curl.exe -L --fail --retry 5 -C - -o ggml-breeze-asr-25-q8_0.bin "https://huggingface.co/shdennlin/breeze-asr-25-ggml/resolve/main/ggml-breeze-asr-25-q8_0.bin"
# sha256 d4b187c40ffbf1f620734b77821e2ca8a97c8ecf5754d02b2a175069348cafcf （1,656,129,691 bytes）
```

較小的量化（同一個來源）：`q6_k` 1.28 GB、`q5_k` 1.08 GB。原版 Whisper 的 ggml 模型（<https://huggingface.co/ggerganov/whisper.cpp>）也能用。
