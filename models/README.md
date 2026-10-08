English | [繁體中文](README.zh-TW.md)

# models

Put models into this folder and pick one from the tray's "Model" menu. Model files are not under version control. Two formats:

| File | Engine | Notes |
|---|---|---|
| `*.bin` (whisper.cpp ggml) | `whisper-server` | Breeze-ASR-25, the original Whisper models, etc. |
| `*.gguf` + matching `mmproj-*.gguf` | `llama-server` | Qwen3-ASR family (Qwen3-ASR, TEA-ASR…). **The mmproj file is the audio encoder and must be there too.** The gateway pairs them by file name (`Qwen3-ASR-1.7B-Q8_0.gguf` ↔ `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf`, or `TEA-ASR-1.1.Q6_K.gguf` ↔ `TEA-ASR-1.1.mmproj-Q8_0.gguf`) |

Run the `curl.exe` lines below in PowerShell from this `models` folder, so the files land here. Check the sha256 after downloading: `(Get-FileHash <file> -Algorithm SHA256).Hash.ToLower()`.

## Recommended: Qwen3-ASR-1.7B (official Alibaba release)

[ggml-org/Qwen3-ASR-1.7B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF) (Apache-2.0; 30 languages plus 22 Chinese dialects and accents). Chinese comes out in Simplified characters, which the gateway converts to Taiwan Traditional. Two files are worth having: **Q8 for the GPU** and **Q4_K_M as the CPU fallback** (used automatically while the GPU is yielded); both share one mmproj. The measured comparison is in [docs/asr-model-comparison.md](../docs/asr-model-comparison.md).

```powershell
curl.exe -L --fail --retry 5 -C - -o Qwen3-ASR-1.7B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B-Q8_0.gguf"
curl.exe -L --fail --retry 5 -C - -o mmproj-Qwen3-ASR-1.7B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/mmproj-Qwen3-ASR-1.7B-Q8_0.gguf"
curl.exe -L --fail --retry 5 -C - -o Qwen3-ASR-1.7B.Q4_K_M.gguf "https://huggingface.co/mradermacher/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B.Q4_K_M.gguf"
```

| File | bytes | sha256 |
|---|---|---|
| `Qwen3-ASR-1.7B-Q8_0.gguf` | 2,165,034,944 | `58e22d0532d4eacaf034cfac17a6fed159f37c41390c710186783be439d1fc57` |
| `mmproj-Qwen3-ASR-1.7B-Q8_0.gguf` (required) | 355,709,344 | `46c1d533af3f354ceb37ce855dbceff7da7fa7cf1e6a523df3b13440bd164c0d` |
| `Qwen3-ASR-1.7B.Q4_K_M.gguf` (CPU fallback; quantized by mradermacher) | 1,282,435,552 | `3893b8926065bbff3da7586d21d8711a9b4fa4fa8f12cd0cefad58e31b2660b6` |

Qwen3-ASR-0.6B ([ggml-org/Qwen3-ASR-0.6B-GGUF](https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF)) is much smaller and the fastest on the CPU, but makes about twice as many errors on real speech as the 1.7B. It is only worth it as the fastest possible CPU fallback:

```powershell
curl.exe -L --fail --retry 5 -C - -o Qwen3-ASR-0.6B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF/resolve/main/Qwen3-ASR-0.6B-Q8_0.gguf"
curl.exe -L --fail --retry 5 -C - -o mmproj-Qwen3-ASR-0.6B-Q8_0.gguf "https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF/resolve/main/mmproj-Qwen3-ASR-0.6B-Q8_0.gguf"
```

| File | bytes | sha256 |
|---|---|---|
| `Qwen3-ASR-0.6B-Q8_0.gguf` | 804,749,248 | `bca259818b50ca7c4c05e9bdb35a5dc04fa039653a6d6f3f0f331f96f6aa1971` |
| `mmproj-Qwen3-ASR-0.6B-Q8_0.gguf` | 214,392,480 | `41a342b5e4c514e968cb756de6cd1b7be39eff43c44c57a2ef5fc6522e36603d` |

## TEA-ASR-1.1 (Taiwanese Mandarin, Traditional characters natively, MIT)

[JacobLinCool/TEA-ASR-1.1](https://huggingface.co/JacobLinCool/TEA-ASR-1.1) is Qwen3-ASR-1.7B fine-tuned on under 10 hours of public Taiwanese speech (CommonVoice zh-TW, ASCEND, NTUML2021, TaiMECS) and keeps the original multilingual ability. The author's error rates (%): CommonVoice zh-TW **3.58** (Qwen3-ASR-1.7B 3.90, Breeze-ASR-25 8.03, Whisper-large-v3 10.17), ASCEND mixed Chinese-English **9.60** (10.57 / 17.53 / 19.61), NTUML2021 lectures **6.67** (10.12 / 7.50 / 9.68). GGUF quantizations by mradermacher:

```powershell
curl.exe -L --fail --retry 5 -C - -o TEA-ASR-1.1.Q6_K.gguf "https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF/resolve/main/TEA-ASR-1.1.Q6_K.gguf"
curl.exe -L --fail --retry 5 -C - -o TEA-ASR-1.1.mmproj-Q8_0.gguf "https://huggingface.co/mradermacher/TEA-ASR-1.1-GGUF/resolve/main/TEA-ASR-1.1.mmproj-Q8_0.gguf"
```

| File | bytes | sha256 |
|---|---|---|
| `TEA-ASR-1.1.Q6_K.gguf` | 1,417,484,704 | `524fb1ecf59c44295a7f09a5bba08afe53731c1f25c9a9cdcbc87f7e68c84e42` |
| `TEA-ASR-1.1.Q5_K_M.gguf` (smaller) | 1,257,609,632 | `6468008c56bf5253a333c2c0ba962e4c770b20e69b59ccdf308d9eb2db3fe623` |
| `TEA-ASR-1.1.Q8_0.gguf` (most accurate) | 1,834,156,448 | `243c56e6cae05c047d42f1c7136cfa3e5b500b86b7d5d7fa44b3ca43cabfe35e` |
| `TEA-ASR-1.1.mmproj-Q8_0.gguf` (required) | 355,709,856 | `76b4e355d7057b2799a512bebce9a0c1b1309fcd6b580400a7bddc268e4fd40a` |

## Breeze-ASR-25 q8_0 (whisper.cpp, Taiwanese Mandarin and mixed Chinese-English, Apache-2.0)

Needs only OpenWhispr's GPU engine, not llama.cpp. The gateway adds punctuation from pauses.

```powershell
curl.exe -L --fail --retry 5 -C - -o ggml-breeze-asr-25-q8_0.bin "https://huggingface.co/shdennlin/breeze-asr-25-ggml/resolve/main/ggml-breeze-asr-25-q8_0.bin"
# sha256 d4b187c40ffbf1f620734b77821e2ca8a97c8ecf5754d02b2a175069348cafcf (1,656,129,691 bytes)
```

Smaller quantizations from the same source: `q6_k` 1.28 GB, `q5_k` 1.08 GB. The original Whisper ggml models (<https://huggingface.co/ggerganov/whisper.cpp>) work too; for English with them, set `language` to `auto` or `en` in `gateway.config.json`.
