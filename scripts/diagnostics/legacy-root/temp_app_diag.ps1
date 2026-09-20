$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appUA = "okhttp/4.9.3"

# Step 1: APP login (no cookie, no device ID)
Write-Host "=== Step 1: First APP login ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r1.StatusCode)"
Write-Host "  Content: $($r1.Content.Substring(0, [Math]::Min(200, $r1.Content.Length)))"
$cookies1 = $appSession.Cookies.GetCookies($baseUrl)
Write-Host "  Cookies after step 1:"
foreach ($c in $cookies1) { Write-Host "    $($c.Name)=$($c.Value)" }

# Check response headers for Set-Cookie
Write-Host "  Response headers:"
foreach ($h in $r1.Headers.Keys) {
    if ($h -match 'cookie' -or $h -match 'Cookie') {
        Write-Host "    $h = $($r1.Headers[$h])"
    }
}

# Step 2: Admin approve
Write-Host "`n=== Step 2: Admin approve ==="
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "  Pending: $($reqIds.Count)"
if ($reqIds.Count -gt 0) {
    $latestId = $reqIds[$reqIds.Count - 1].Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$latestId} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "  Approved: $latestId"
}

# Step 3: Retry with same session
Write-Host "`n=== Step 3: Retry ==="
$cookies3 = $appSession.Cookies.GetCookies($baseUrl)
Write-Host "  Cookies before retry:"
foreach ($c in $cookies3) { Write-Host "    $($c.Name)=$($c.Value)" }

$r3 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 15
Write-Host "  Status: $($r3.StatusCode)"
Write-Host "  GatewayBlocked: $($r3.Content -match 'GatewayBlocked')"
Write-Host "  Content: $($r3.Content.Substring(0, [Math]::Min(300, $r3.Content.Length)))"

$cookiesAfter = $appSession.Cookies.GetCookies($baseUrl)
Write-Host "  Cookies after retry:"
foreach ($c in $cookiesAfter) { Write-Host "    $($c.Name)=$($c.Value)" }
