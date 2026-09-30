# Top processes by dedicated GPU memory (Windows "GPU Process Memory" counters). ASCII only.
$s = (Get-Counter '\GPU Process Memory(*)\Dedicated Usage' -ErrorAction SilentlyContinue).CounterSamples | Where-Object { $_.CookedValue -gt 150MB }
$rows = foreach ($g in ($s | Group-Object { [regex]::Match($_.InstanceName, 'pid_(\d+)').Groups[1].Value })) {
  $name = (Get-Process -Id ([int]$g.Name) -ErrorAction SilentlyContinue).ProcessName
  if (-not $name) { $name = "pid" + $g.Name }
  [pscustomobject]@{ n = $name; gb = ($g.Group | Measure-Object CookedValue -Sum).Sum / 1GB }
}
($rows | Sort-Object gb -Descending | Select-Object -First 4 | ForEach-Object { '{0} {1:N1}GB' -f $_.n, $_.gb }) -join ', '
