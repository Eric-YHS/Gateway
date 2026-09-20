Import-Module WebAdministration

New-WebAppPool -Name 'KpDemoPool' -Force
Set-ItemProperty -Path 'IIS:\AppPools\KpDemoPool' -Name managedRuntimeVersion -Value 'v4.0'
Set-ItemProperty -Path 'IIS:\AppPools\KpDemoPool' -Name managedPipelineMode -Value 'Integrated'

$webPath = (Get-Item 'E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330').FullName
New-Website -Name 'KpDemo' -PhysicalPath $webPath -Port 8080 -ApplicationPool 'KpDemoPool' -Force

$s = Get-Website -Name 'KpDemo'
Write-Host "State: $($s.State)"
Write-Host "Path: $($s.PhysicalPath)"
