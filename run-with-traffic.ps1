<#
.SYNOPSIS
  Launch Sentinel (the TUI) together with the OTLP load generator — one command, no second
  terminal to open by hand. The generator runs in its own window so its output doesn't paint
  over the TUI, and it is stopped automatically when you quit Sentinel.

.EXAMPLE
  ./run-with-traffic.ps1
  ./run-with-traffic.ps1 -Count 20
#>
[CmdletBinding()]
param(
    [int] $Count = 0  # stop the generator after N traces (0 = keep streaming until Sentinel exits)
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tfm = 'net10.0'

Write-Host 'Building solution...' -ForegroundColor Cyan
dotnet build (Join-Path $root 'Sentinel.CLI.sln') -v q --nologo

$generator = Join-Path $root "tools\Sentinel.LoadGenerator\bin\Debug\$tfm\sentinel-load.exe"
$sentinel = Join-Path $root "src\Sentinel.CLI\bin\Debug\$tfm\sentinel.exe"

Write-Host 'Starting load generator (separate window)...' -ForegroundColor Cyan
$startParams = @{ FilePath = $generator; PassThru = $true }
if ($Count -gt 0) { $startParams['ArgumentList'] = "--count=$Count" }
$gen = Start-Process @startParams

try {
    # The TUI runs in this (interactive) terminal and blocks until you press q.
    & $sentinel
}
finally {
    if ($gen -and -not $gen.HasExited) {
        Write-Host 'Stopping load generator...' -ForegroundColor Cyan
        Stop-Process -Id $gen.Id -Force
    }
}
