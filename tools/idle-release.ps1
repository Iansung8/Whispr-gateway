# OpenWhispr idle GPU release watchdog (ASCII-only on purpose: PowerShell 5.1 reads BOM-less files as ANSI).
#
# OpenWhispr keeps its whisper-server (and the model in VRAM) resident for the app's whole lifetime.
# This loop stops the server after it has been idle for -IdleMinutes. OpenWhispr restarts it by itself
# on the next dictation (one cold start, a few seconds). Verified against OpenWhispr 1.9.1 sources:
# an externally killed IDLE server is a clean state; a server that dies DURING a request is treated as
# a GPU crash (CPU fallback + WHISPER_GPU_FAILED in %APPDATA%\open-whispr\.env). Hence the guards below.
#
#   .\idle-release.ps1 -DryRun            # log what it would do, never kill
#   .\idle-release.ps1 -IdleMinutes 10    # run for real (foreground; Ctrl+C to stop)
param(
  [int]$IdleMinutes = 10,
  [int]$PollSeconds = 30,
  [double]$BusyCpuSeconds = 0.15,
  [switch]$DryRun,
  [string]$LogPath = (Join-Path $env:LOCALAPPDATA "openwhispr-idle-release\watchdog.log")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force (Split-Path $LogPath) | Out-Null

function Write-Log([string]$msg) {
  $line = "{0} {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
  Add-Content -Path $LogPath -Value $line -Encoding utf8
  if ((Get-Item $LogPath).Length -gt 1MB) { Get-Content $LogPath -Tail 200 | Set-Content $LogPath -Encoding utf8 }
}

function Get-WhisperServers {
  Get-Process -Name "whisper-server-win32-x64*" -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ($_.Path -like "*\OpenWhispr\*" -or $_.Path -like "*\open-whispr\*")
  }
}

function Test-InFlight($proc) {
  # An in-flight transcription is an established TCP connection to the server's port.
  $conns = @(Get-NetTCPConnection -OwningProcess $proc.Id -State Established -ErrorAction SilentlyContinue)
  return $conns.Count -gt 0
}

$state = @{}   # pid -> @{ Cpu = <seconds>; IdleSince = <datetime> }
Write-Log "watchdog started (idle=$IdleMinutes min, poll=$PollSeconds s, dryRun=$($DryRun.IsPresent))"

while ($true) {
  try {
    $now = Get-Date
    $procs = @(Get-WhisperServers)
    $live = @{}
    foreach ($p in $procs) {
      $live[$p.Id] = $true
      $cpu = $p.TotalProcessorTime.TotalSeconds
      if (-not $state.ContainsKey($p.Id)) {
        $state[$p.Id] = @{ Cpu = $cpu; IdleSince = $now }
        continue
      }
      $s = $state[$p.Id]
      $delta = $cpu - $s.Cpu
      $s.Cpu = $cpu
      if ($delta -ge $BusyCpuSeconds -or (Test-InFlight $p)) { $s.IdleSince = $now; continue }

      if (($now - $s.IdleSince).TotalMinutes -ge $IdleMinutes) {
        # Last-second guard: re-sample CPU and connections right before stopping.
        Start-Sleep -Milliseconds 1500
        $p.Refresh()
        if ($p.HasExited) { continue }
        $guardDelta = $p.TotalProcessorTime.TotalSeconds - $cpu
        if ($guardDelta -gt 0.05 -or (Test-InFlight $p)) { $s.IdleSince = Get-Date; $s.Cpu = $p.TotalProcessorTime.TotalSeconds; continue }

        $name = $p.ProcessName
        if ($DryRun) {
          Write-Log "DRYRUN would stop $name pid=$($p.Id) after $IdleMinutes idle minutes"
          $s.IdleSince = Get-Date
        } else {
          Stop-Process -Id $p.Id -Force -Confirm:$false
          Write-Log "stopped $name pid=$($p.Id) after $IdleMinutes idle minutes"
        }
      }
    }
    foreach ($id in @($state.Keys)) { if (-not $live.ContainsKey($id)) { $state.Remove($id) } }
  } catch {
    Write-Log "error: $($_.Exception.Message)"
  }
  Start-Sleep -Seconds $PollSeconds
}
