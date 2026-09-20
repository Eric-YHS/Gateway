$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'session-lab-deploy.log'
try {
Import-Module WebAdministration
$root = Join-Path $PSScriptRoot 'session-lab'
foreach($name in @('SessionLabWorker1','SessionLabWorker2')) {
  if (Get-Website -Name $name -ErrorAction SilentlyContinue) { Stop-Website -Name $name -ErrorAction SilentlyContinue; Remove-Website -Name $name }
}
foreach($pool in @('SessionLabWorker1Pool','SessionLabWorker2Pool')) {
  if (Test-Path "IIS:\AppPools\$pool") { Stop-WebAppPool -Name $pool -ErrorAction SilentlyContinue; Remove-WebAppPool -Name $pool }
  New-WebAppPool -Name $pool | Out-Null
  Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value 'v4.0'
  Set-ItemProperty "IIS:\AppPools\$pool" -Name managedPipelineMode -Value 'Integrated'
  Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.maxProcesses -Value 1
  Set-ItemProperty "IIS:\AppPools\$pool" -Name recycling.disallowOverlappingRotation -Value $true
}
New-Website -Name 'SessionLabWorker1' -Port 53661 -IPAddress '127.0.0.1' -PhysicalPath "$root\worker1" -ApplicationPool 'SessionLabWorker1Pool' | Out-Null
New-Website -Name 'SessionLabWorker2' -Port 53662 -IPAddress '127.0.0.1' -PhysicalPath "$root\worker2" -ApplicationPool 'SessionLabWorker2Pool' | Out-Null
Start-Website -Name 'SessionLabWorker1'
Start-Website -Name 'SessionLabWorker2'
Start-WebAppPool -Name 'SessionLabWorker1Pool'
Start-WebAppPool -Name 'SessionLabWorker2Pool'
Write-Output 'session-lab deployed'
Get-Website -Name 'SessionLabWorker1','SessionLabWorker2' | Select-Object Name,State,PhysicalPath,Bindings
} catch {
  $_ | Out-File -LiteralPath $log -Encoding utf8
  throw
}
