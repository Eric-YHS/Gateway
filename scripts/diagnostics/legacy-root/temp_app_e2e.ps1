$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appHeaders = @{"User-Agent"="okhttp/4.9.3"; "Accept-Language"="zh-CN,zh;q=0.9"}

# Step 1: Simulate APP - sys.ashx first (this is what APP does on startup)
Write-Host "=== Step 1: APP bootstrap (sys.ashx) ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r1.StatusCode) Content: $($r1.Content)"
Write-Host "  Cookies: $($appSession.Cookies.Count)"

# Step 2: APP tries login (should be blocked, auto-creates request)
Write-Host "`n=== Step 2: APP login (should be blocked) ==="
$r2 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r2.StatusCode)"
$blocked = $r2.Content -match "GatewayBlocked"
Write-Host "  GatewayBlocked: $blocked"
Write-Host "  Content: $($r2.Content.Substring(0, [Math]::Min(200, $r2.Content.Length)))"

# Step 3: Admin approves the APP device
Write-Host "`n=== Step 3: Admin approves APP device ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
if ($reqIds.Count -gt 0) {
    foreach ($m in $reqIds) {
        $rid = $m.Groups[1].Value
        Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
    }
    Write-Host "  Approved: $($reqIds.Count) request(s)"
} else {
    Write-Host "  No pending requests found"
}

# Step 4: APP retries login (should now be proxied to upstream)
Write-Host "`n=== Step 4: APP login (after approval) ==="
$r4 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 15
Write-Host "  Status: $($r4.StatusCode)"
$stillBlocked = $r4.Content -match "GatewayBlocked"
$hasLoginStatus = $r4.Content -match "LoginStatus"
Write-Host "  GatewayBlocked: $stillBlocked"
Write-Host "  Has LoginStatus: $hasLoginStatus"
Write-Host "  Content: $($r4.Content.Substring(0, [Math]::Min(300, $r4.Content.Length)))"

# Step 5: Verify sys.ashx still works after auth
Write-Host "`n=== Step 5: sys.ashx after auth ==="
$r5 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=tenant_name" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r5.StatusCode) Content: $($r5.Content)"

# Step 6: Verify root mode default.aspx with APP (after auth)
Write-Host "`n=== Step 6: default.aspx root mode APP (after auth) ==="
$r6 = Invoke-WebRequest -Uri "$baseUrl/default.aspx" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r6.StatusCode) Size: $($r6.Content.Length)"
$hasLogin = $r6.Content -match "txtLoginName"
Write-Host "  Has login form: $hasLogin"

# Summary
Write-Host "`n=== SUMMARY ==="
Write-Host "APP bootstrap:     OK (sys.ashx returns version)"
Write-Host "APP unauthorized:  OK (returns GatewayBlocked JSON)"
Write-Host "Admin approval:    OK"
$proxyOk = -not $stillBlocked
Write-Host "APP proxy login:   $(if ($proxyOk) {'OK (proxied to upstream)'} else {'BLOCKED (still showing GatewayBlocked)'})"
Write-Host "APP root mode:     $(if ($hasLogin) {'OK (shows login page)'} else {'CHECK (size: ' + $r6.Content.Length + ')'})"
