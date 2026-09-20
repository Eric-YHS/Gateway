$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appUA = "okhttp/4.9.3"

# Fixed headers to ensure consistent signal hash
$fixedHeaders = @{
    "User-Agent" = $appUA
    "Accept-Language" = "zh-CN,zh;q=0.9"
}

# Step 1: First APP login (creates device with fixed headers)
Write-Host "=== Step 1: First login (fixed headers) ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers $fixedHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
$cookie = ($appSession.Cookies.GetCookies($baseUrl) | Where-Object { $_.Name -eq 'gw_device_credential' }).Value
Write-Host "  Cookie: $cookie"
Write-Host "  Blocked: $($r1.Content -match 'GatewayBlocked')"

# Step 2: Admin approve
Write-Host "`n=== Step 2: Admin approve ==="
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "  Pending: $($reqIds.Count)"
# Approve ALL pending requests
foreach ($m in $reqIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "  Approved: $rid"
}

# Step 3: Retry with SAME fixed headers
Write-Host "`n=== Step 3: Retry (same fixed headers) ==="
$r3 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers $fixedHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 15
Write-Host "  Cookie sent: $(($appSession.Cookies.GetCookies($baseUrl) | Where-Object { $_.Name -eq 'gw_device_credential' }).Value)"
Write-Host "  Status: $($r3.StatusCode)"
$blocked = $r3.Content -match 'GatewayBlocked'
Write-Host "  GatewayBlocked: $blocked"
Write-Host "  Content: $($r3.Content.Substring(0, [Math]::Min(300, $r3.Content.Length)))"

# Step 4: Also try the mock backend API
Write-Host "`n=== Step 4: sys.ashx (same headers) ==="
$r4 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers $fixedHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Version: $($r4.Content)"

Write-Host "`n=== RESULT ==="
if ($blocked) {
    Write-Host "FAIL: Still blocked after approval"
} else {
    Write-Host "PASS: Proxy works after approval"
}
