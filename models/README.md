# models

把模型放在這個資料夾，再從系統匣選單的「模型」選它。模型檔不進版控。兩種格式：

| 檔案 | 引擎 | 說明 |
|---|---|---|
| `*.bin`（whisper.cpp ggml） | `whisper-server` | Breeze-ASR-25、原版 Whisper 等 |
| `*.gguf` ＋ 同名 `mmproj-*.gguf` | `llama-server` | Qwen3-ASR 系列（Qwen3-ASR、TEA-ASR…）。**mmproj 是音訊編碼器，一定要一起放**；閘道依檔名自動配對（`Qwen3-ASR-1.7B-Q8_0.gguf` ↔ `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf`，或 `TEA-ASR-1.1.Q6_K.gguf` ↔ `TEA-ASR-1.1.mmproj-Q8_0.gguf`） |

下載後請比對 sha256（`(Get-FileHash <檔名> -Algorithm SHA256).Hash.ToLower()`）。

## 推薦：TEA-ASR-1.1（台灣華語，原生繁體＋台灣用字，MIT）

[JacobLinCool/TEA-ASR-1.1](https://huggingface.co/JacobLinCool/TEA-ASR-1.1)：Qwen3-ASR-1.7B 以不到 10 小時的台灣公開語料微調（CommonVoice zh-TW、ASCEND、NTUML2021、TaiMECS），保留原本的多語能力。作者公布的錯誤率（%）：CommonVoice zh-TW **3.58**（Qwen3-ASR-1.7B 3.90、Breeze-ASR-25 8.03、Whisper-large-v3 10.17）、ASCEND 中英夾雜 **9.60**（10.57／17.53／19.61）、NTUML2021 課堂 **6.67**（10.12／7.50／9.68）。GGUF 由 mradermacher 量化：

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

## Qwen3-ASR（阿里官方，未微調，輸出簡體 → 閘道自動轉台灣繁體）

[ggml-org/Qwen3-ASR-1.7B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF)、[ggml-org/Qwen3-ASR-0.6B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF)（Apache-2.0；30 種語言＋22 種漢語方言／口音）。0.6B 小很多、速度快，準確度略低。

| 檔案 | bytes | sha256 |
|---|---|---|
| `Qwen3-ASR-1.7B-Q8_0.gguf` | 2,165,034,944 | `58e22d0532d4eacaf034cfac17a6fed159f37c41390c710186783be439d1fc57` |
| `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf` | 355,709,344 | `46c1d533af3f354ceb37ce855dbceff7da7fa7cf1e6a523df3b13440bd164c0d` |
| `Qwen3-ASR-0.6B-Q8_0.gguf` | 804,749,248 | `bca259818b50ca7c4c05e9bdb35a5dc04fa039653a6d6f3f0f331f96f6aa1971` |
| `mmproj-Qwen3-ASR-0.6B-Q8_0.gguf` | 214,392,480 | `41a342b5e4c514e968cb756de6cd1b7be39eff43c44c57a2ef5fc6522e36603d` |

網址格式：`https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/<檔名>`。

## Breeze-ASR-25 q8_0（whisper.cpp，台灣華語＋中英夾雜，Apache-2.0）

只需要 OpenWhispr 的 GPU 引擎、不需要 llama.cpp。標點由閘道依停頓補上。

```powershell
curl.exe -L --fail --retry 5 -C - -o ggml-breeze-asr-25-q8_0.bin "https://huggingface.co/shdennlin/breeze-asr-25-ggml/resolve/main/ggml-breeze-asr-25-q8_0.bin"
# sha256 d4b187c40ffbf1f620734b77821e2ca8a97c8ecf5754d02b2a175069348cafcf （1,656,129,691 bytes）
```

較小的量化（同一個來源）：`q6_k` 1.28 GB、`q5_k` 1.08 GB。原版 Whisper 的 ggml 模型（<https://huggingface.co/ggerganov/whisper.cpp>）也能用。
