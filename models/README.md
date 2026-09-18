# models

把 whisper.cpp 的 ggml 模型（`*.bin`）放在這個資料夾，再從系統匣選單的「模型」選它。模型檔不進版控。

預設設定使用 **Breeze-ASR-25 q8_0**（台灣華語＋中英夾雜，Apache-2.0）：

```powershell
curl.exe -L --fail --retry 5 -C - -o ggml-breeze-asr-25-q8_0.bin "https://huggingface.co/shdennlin/breeze-asr-25-ggml/resolve/main/ggml-breeze-asr-25-q8_0.bin"
(Get-FileHash ggml-breeze-asr-25-q8_0.bin -Algorithm SHA256).Hash.ToLower()
# 應為 d4b187c40ffbf1f620734b77821e2ca8a97c8ecf5754d02b2a175069348cafcf （1,656,129,691 bytes）
```

較小的量化（同一個來源）：`q6_k` 1.28 GB、`q5_k` 1.08 GB。原版 Whisper 的 ggml 模型（<https://huggingface.co/ggerganov/whisper.cpp>）也能用。
