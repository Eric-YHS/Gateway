$ErrorActionPreference = 'Stop'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$r1 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10
Write-Host ('Visit: ' + $r1.StatusCode)

$reqBody = @{
    action='request-access'
    companyName='ScreenDemo'
    applicantName='Admin'
    phone='13800138000'
    reason='screenshot'
    targetPath='/proxy/erp-main/default.aspx'
}
$submit = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $session -UseBasicParsing -TimeoutSec 10
Write-Host ('Submit: ' + $submit.StatusCode)

$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host ('Admin login: ' + $loginR.StatusCode)

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)
Write-Host ('Pending: ' + $allIds.Count)

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host ('Approved: ' + $allIds.Count)

$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host ('Proxy: ' + $proxy.Content.Length + ' bytes')

$cookies = $session.Cookies.GetCookies('http://localhost:5050')
foreach ($c in $cookies) {
    Write-Host ('Cookie: ' + $c.Name + '=' + $c.Value)
}
