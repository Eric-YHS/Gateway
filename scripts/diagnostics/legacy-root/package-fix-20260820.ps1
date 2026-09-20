$ErrorActionPreference = 'Stop'
$root = Join-Path (Get-Location).Path '_package_stage_20260820_01'
Get-ChildItem -LiteralPath $root -Recurse -Directory |
    Where-Object { $_.Name -eq 'obj' } |
    Remove-Item -Recurse -Force
$zipTemp = Join-Path (Get-Location).Path 'deliverables\GatewayDemo.Legacy-net472.next.zip'
if (Test-Path -LiteralPath $zipTemp) { Remove-Item -LiteralPath $zipTemp -Force }
Compress-Archive -Path (Join-Path $root '*') -DestinationPath $zipTemp -CompressionLevel Optimal
Get-Item -LiteralPath $zipTemp | Select-Object FullName,Length,LastWriteTimeUtc
Get-FileHash -LiteralPath $zipTemp -Algorithm SHA256
