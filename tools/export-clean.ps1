# Copies ONLY the public source files into a clean folder for publishing (whitelist, so models, logs,
# the live config, private notes and binaries can never slip in), then scans the result for personal
# traces. ASCII-only on purpose (PowerShell 5.1 + BOM-less files).
#   .\tools\export-clean.ps1                       # -> ..\whispr-gateway-release
#   .\tools\export-clean.ps1 -Target D:\somewhere
param(
  [string]$Target = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) "whispr-gateway-release"),
  [string[]]$TracePatterns = @('[A-Za-z]:\\Users\\[^\\%]+\\', '@gmail\.com')
)
$root = Split-Path $PSScriptRoot
$public = @(
  "README.md", "LICENSE", ".gitignore", "build.ps1", ".github\workflows\release.yml",
  "whisper-gateway.js", "gateway.config.default.json",
  "src\GatewayTray.cs",
  "docs\openwhispr-notes.md",
  "models\README.md",
  "tools\export-clean.ps1", "tools\test-model.ps1", "tools\test-host-endpoint.ps1",
  "tools\make-test-clip2.ps1", "tools\make-test-clip3.ps1", "tools\asr-matrix.py", "tools\idle-release.ps1"
)

New-Item -ItemType Directory -Force $Target | Out-Null
# refresh only what we own in the target; never touch its .git folder
Get-ChildItem $Target -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force -Confirm:$false
foreach ($rel in $public) {
  $src = Join-Path $root $rel
  if (-not (Test-Path $src)) { Write-Warning "missing (skipped): $rel"; continue }
  $dst = Join-Path $Target $rel
  New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
  Copy-Item $src $dst
}

"exported to $Target"
Get-ChildItem $Target -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\\.git\\' } |
  Select-Object @{n='File';e={$_.FullName.Substring($Target.Length + 1)}}, @{n='KB';e={[math]::Round($_.Length / 1KB, 1)}} | Format-Table -AutoSize

$hits = Get-ChildItem $Target -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\\.git\\' -and $_.Name -ne 'export-clean.ps1' } |
  Select-String -Pattern $TracePatterns -CaseSensitive:$false
if ($hits) { Write-Warning "personal traces found:"; $hits | ForEach-Object { "  {0}:{1}  {2}" -f $_.Path.Substring($Target.Length + 1), $_.LineNumber, $_.Line.Trim() } }
else { "trace scan: clean" }
