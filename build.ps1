# Builds WhisprGateway.exe with the C# compiler that ships with Windows (.NET Framework 4.x) - no SDK needed.
# ASCII-only on purpose (PowerShell 5.1 + BOM-less files). The .cs file itself is UTF-8 (CJK UI text).
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
$out = Join-Path $PSScriptRoot "WhisprGateway.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 /utf8output `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
  "/out:$out" (Join-Path $PSScriptRoot "src\GatewayTray.cs")
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Get-Item $out | Select-Object Name, Length, LastWriteTime
