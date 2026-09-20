$ErrorActionPreference = "Stop"

# Use one session throughout
$gwSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Step 1: Get gateway page (will set device cookie)
Write-Host "=== Step 1: Get device cookie ==="
$gwPage = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -Method GET -WebSession $gwSession -UseBasicParsing -TimeoutSec 10
Write-Host "Status: $($gwPage.StatusCode), Size: $($gwPage.Content.Length)"

# Step 2: Submit access request
Write-Host "`n=== Step 2: Submit request ==="
$reqBody = @{
    action = 'request-access'
    companyName = 'Demo Corp'
    applicantName = 'Admin'
    phone = '13800138000'
    reason = 'test'
    targetPath = '/proxy/erp-main/default.aspx'
}
$submitResp = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $gwSession -UseBasicParsing -TimeoutSec 10
Write-Host "Submit: $($submitResp.StatusCode)"

# Step 3: Admin login + approve
Write-Host "`n=== Step 3: Admin approve ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{ action='login'; username='gateway-admin'; password='GatewayDemo!2026' }
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "Pending: $($reqIds.Count)"

$latestId = $reqIds[$reqIds.Count - 1].Groups[1].Value
Write-Host "Approving latest: $latestId"
$appr = @{ action='approve-request'; requestId=$latestId }
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $appr -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
Write-Host "Approved."

# Step 4: Test proxy with the SAME session
Write-Host "`n=== Step 4: Test proxy ==="
$proxyResp = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -Method GET -WebSession $gwSession -UseBasicParsing -TimeoutSec 15
Write-Host "Status: $($proxyResp.StatusCode), Size: $($proxyResp.Content.Length)"

$titleMatch = [regex]::Match($proxyResp.Content, '<title>(.*?)</title>')
if ($titleMatch.Success) {
    Write-Host "Title: $($titleMatch.Groups[1].Value)"
}

if ($proxyResp.Content -match 'txtLoginName') {
    Write-Host "CHECK: Found login name field - this is the demo site!"
}
if ($proxyResp.Content -match 'btnLogin') {
    Write-Host "CHECK: Found login button"
}
if ($proxyResp.Content -match 'ValidateCode') {
    Write-Host "CHECK: Found validate code"
}
if ($proxyResp.Content -match 'KPMIIS') {
    Write-Host "CHECK: Found KPMIIS namespace"
}
if ($proxyResp.Content -match 'ddlDataCenter') {
    Write-Host "CHECK: Found data center dropdown"
}
