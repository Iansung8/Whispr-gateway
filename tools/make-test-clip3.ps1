# Code-switching test clip: Mandarin with English technical terms (zh-TW TTS voice). Keep UTF-8 with BOM.
Add-Type -AssemblyName System.Speech
$wav = Join-Path $PSScriptRoot "test3-codeswitch.wav"
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice("Microsoft Hanhan Desktop")
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$s.SetOutputToWaveFile($wav, $fmt)
$s.Speak("我現在用 CUDA 跑 llama.cpp，GPU 是 RTX 5080，如果 Vulkan 比較慢就換回 CUDA。另外 OpenWhispr 的 API 要填 localhost。")
$s.Dispose()
"wrote $wav"
