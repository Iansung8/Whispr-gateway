# Synthesizes a second, more conversational zh-TW test clip (statement + question + code-switch words).
# Contains CJK text: keep this file UTF-8 *with BOM* for PowerShell 5.1.
Add-Type -AssemblyName System.Speech
$wav = Join-Path $PSScriptRoot "test2-zh-tw.wav"
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice("Microsoft Hanhan Desktop")
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$s.SetOutputToWaveFile($wav, $fmt)
$s.Speak("我剛剛把論文的第三章改完了，你有空可以幫我看一下嗎？如果沒問題的話，我明天早上就寄給老師。對了，上次說的那個實驗數據，我覺得還需要再跑一次，因為結果跟預期的差很多。")
$s.Dispose()
"wrote $wav"
