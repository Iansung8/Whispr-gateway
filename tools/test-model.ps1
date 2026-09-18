# Offline sanity test: run OpenWhispr's bundled whisper-server against a ggml model
# with a locally synthesized zh-TW clip. Usage: .\test-model.ps1 -Model <path-to-ggml.bin> [-Port 8917]
param(
  [Parameter(Mandatory = $true)][string]$Model,
  [int]$Port = 8917,
  [string]$Server = "C:\Program Files\OpenWhispr\resources\bin\whisper-server-win32-x64.exe"
)

$wav = Join-Path $PSScriptRoot "test-zh-tw.wav"
if (-not (Test-Path $wav)) {
  Add-Type -AssemblyName System.Speech
  $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
  $synth.SelectVoice("Microsoft Hanhan Desktop")
  $fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
  $synth.SetOutputToWaveFile($wav, $fmt)
  $synth.Speak("今天下午三點要跟客戶開會，記得先把報價單的檔案寄給我。另外實驗室的網路設定我已經更新了，軟體也重新安裝完成。")
  $synth.Dispose()
}

$proc = Start-Process -FilePath $Server -ArgumentList @("--model", "`"$Model`"", "--host", "127.0.0.1", "--port", "$Port", "--language", "zh", "--no-timestamps") -PassThru -WindowStyle Hidden
try {
  $ready = $false
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 120 -and -not $proc.HasExited) {
    try {
      $c = New-Object Net.Sockets.TcpClient
      $c.Connect("127.0.0.1", $Port); $c.Close(); $ready = $true; break
    } catch { Start-Sleep -Milliseconds 500 }
  }
  if (-not $ready) { throw "whisper-server did not come up (exited=$($proc.HasExited))" }
  "server ready after {0:N1}s" -f $sw.Elapsed.TotalSeconds

  $sw.Restart()
  $out = & curl.exe -s "http://127.0.0.1:$Port/inference" -F "file=@$wav" -F "language=zh" -F "response_format=json" -F "prompt=以下是繁體中文。語言、學習、軟體、網路。"
  "inference took {0:N1}s" -f $sw.Elapsed.TotalSeconds
  $out
} finally {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -Confirm:$false }
}
