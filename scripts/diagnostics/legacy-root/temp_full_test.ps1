$ErrorActionPreference = "Stop"

Write-Host "=== Step 1: Submit access request ==="
$gwSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$gwPage = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -Method GET -WebSession $gwSession -UseBasicParsing -TimeoutSec 10
Write-Host "Gateway page status: $($gwPage.StatusCode)"

$reqBody = @{
    action = 'request-access'
    companyName = 'Demo Corp'
    applicantName = 'Test Admin'
    phone = '13800138000'
    reason = 'Local demo test'
    targetPath = '/proxy/erp-main/default.aspx'
}
# siteKeys needs multiple values - use GetValues approach
$submitResp = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $gwSession -UseBasicParsing -TimeoutSec 10
Write-Host "Submit status: $($submitResp.StatusCode)"

Write-Host ""
Write-Host "=== Step 2: Login to admin ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{
    action = 'login'
    username = 'gateway-admin'
    password = 'GatewayDemo!2026'
}
$loginResp = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
Write-Host "Login status: $($loginResp.StatusCode)"

Write-Host ""
Write-Host "=== Step 3: Find pending requests ==="
$dashboard = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$content = $dashboard.Content

$requestIds = [regex]::Matches($content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "Found $($requestIds.Count) pending request(s)"

if ($requestIds.Count -gt 0) {
    foreach ($match in $requestIds) {
        $reqId = $match.Groups[1].Value
        Write-Host "Approving request: $reqId"
        $approveBody = @{
            action = 'approve-request'
            requestId = $reqId
            note = 'Auto-approved'
        }
        $approveResp = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $approveBody -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
        Write-Host "Approve status: $($approveResp.StatusCode)"
    }
} else {
    Write-Host "No pending requests found in HTML."
    $deviceIds = [regex]::Matches($content, 'DEV-\d+-[A-F0-9]+')
    Write-Host "Device IDs found: $($deviceIds.Count)"
    foreach ($d in $deviceIds) {
        Write-Host "  Device: $($d.Value)"
    }
}

Write-Host ""
Write-Host "=== Step 4: Test proxy access ==="
try {
    $proxyResp = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -Method GET -WebSession $gwSession -UseBasicParsing -TimeoutSec 15
    Write-Host "Proxy status: $($proxyResp.StatusCode)"
    Write-Host "Content length: $($proxyResp.Content.Length)"
    $titleMatch = [regex]::Match($proxyResp.Content, '<title>(.*?)</title>')
    if ($titleMatch.Success) {
        Write-Host "Page title: $($titleMatch.Groups[1].Value)"
    }
} catch {
    Write-Host "Proxy error: $($_.Exception.Message)"
}
