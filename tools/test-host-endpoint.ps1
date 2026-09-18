# Localhost feasibility test for "OpenWhispr self-hosted mode -> whisper-server on this PC".
# Starts the CUDA whisper-server as an OpenAI-compatible endpoint on 127.0.0.1 only, posts a webm clip
# exactly the way OpenWhispr's self-hosted route does (file=audio.webm, model, language), then stops it.
# ASCII-only on purpose (PowerShell 5.1 + BOM-less files).
param(
  [int]$Port = 8190,
  [string]$Model = (Join-Path $PSScriptRoot "..\models\ggml-breeze-asr-25-q8_0.bin"),
  [string]$Server = (Join-Path $env:APPDATA "open-whispr\bin\whisper-cuda\whisper-server-win32-x64-cuda.exe"),
  [string]$FfmpegDir = "C:\Program Files\OpenWhispr\resources\app.asar.unpacked\node_modules\ffmpeg-static"
)

$wav = Join-Path $PSScriptRoot "test-zh-tw.wav"
$webm = Join-Path $PSScriptRoot "test-zh-tw.webm"
if (-not (Test-Path $wav)) { throw "missing $wav - run test-model.ps1 once first" }
if (-not (Test-Path $webm)) { & (Join-Path $FfmpegDir "ffmpeg.exe") -y -loglevel error -i $wav -c:a libopus -b:a 32k $webm }

$tmp = Join-Path $env:TEMP "openwhispr-host-tmp"
New-Item -ItemType Directory -Force $tmp | Out-Null
$env:PATH = "$FfmpegDir;$env:PATH"   # whisper-server --convert shells out to "ffmpeg"

$argList = @("--model", "`"$Model`"", "--host", "127.0.0.1", "--port", "$Port",
  "--inference-path", "/v1/audio/transcriptions", "--convert", "--tmp-dir", "`"$tmp`"",
  "--language", "zh", "--no-timestamps")
$proc = Start-Process -FilePath $Server -ArgumentList $argList -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $Server)
try {
  $sw = [Diagnostics.Stopwatch]::StartNew(); $ready = $false
  while ($sw.Elapsed.TotalSeconds -lt 120 -and -not $proc.HasExited) {
    try { $c = New-Object Net.Sockets.TcpClient; $c.Connect("127.0.0.1", $Port); $c.Close(); $ready = $true; break } catch { Start-Sleep -Milliseconds 300 }
  }
  if (-not $ready) { throw "server did not come up (exited=$($proc.HasExited))" }
  "server ready after {0:N1}s (cold start incl. model load to GPU)" -f $sw.Elapsed.TotalSeconds

  foreach ($i in 1..3) {
    $sw.Restart()
    $out = & curl.exe -s "http://127.0.0.1:$Port/v1/audio/transcriptions" -F "file=@$webm;type=audio/webm;filename=audio.webm" -F "model=breeze-asr-25" -F "language=zh"
    "run $i : {0:N2}s  {1}" -f $sw.Elapsed.TotalSeconds, $out
  }
} finally {
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -Confirm:$false }
}
