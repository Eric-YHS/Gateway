$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appUA = "okhttp/4.9.3"
$machineId = "e2e-precise-test"
$deviceImei = "869990000000002"

# Step 1: Bootstrap
Write-Host "=== Step 1: Bootstrap ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Version: $($r1.Content)"

# Step 2: Login (creates device + auto-request)
Write-Host "`n=== Step 2: Login (blocked) ==="
$loginBody = "UserId=admin&UserPwd=123456&MachineId=$machineId&DeviceImei=$deviceImei"
$r2 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Blocked: $($r2.Content -match 'GatewayBlocked')"

# Use the gw admin API to approve - approve ALL pending requests (brute force for test)
Write-Host "`n=== Step 3: Admin login ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Login: $($loginR.StatusCode)"

# Get ALL request IDs from dashboard and approve every single one
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$allReqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "  Found $($allReqIds.Count) pending requests"

# Approve ALL of them to make sure our device is covered
foreach ($match in $allReqIds) {
    $rid = $match.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "  Approved all $($allReqIds.Count) requests"

# Step 4: APP login retry
Write-Host "`n=== Step 4: Login after ALL approvals ==="
$r4 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 15
$blocked = $r4.Content -match "GatewayBlocked"
$hasUpstream = $r4.Content -match "LoginStatus"
Write-Host "  Status: $($r4.StatusCode) Blocked: $blocked HasLoginStatus: $hasUpstream"
Write-Host "  Content: $($r4.Content.Substring(0, [Math]::Min(300, $r4.Content.Length)))"

# Step 5: WCF
Write-Host "`n=== Step 5: WCF PostBus ==="
$postJson = '{"UserToken":{"SessionKey":"test-session","MachineId":"' + $machineId + '"}}'
$r5 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/WCFService/PostBus.ashx" -Method POST -Body "postJson=$postJson" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 15
$wcfBlocked = $r5.Content -match "GatewayBlocked"
Write-Host "  Status: $($r5.StatusCode) Blocked: $wcfBlocked"
Write-Host "  Content: $($r5.Content.Substring(0, [Math]::Min(300, $r5.Content.Length)))"

# Step 6: Root mode
Write-Host "`n=== Step 6: Root mode default.aspx ==="
$r6 = Invoke-WebRequest -Uri "$baseUrl/default.aspx" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
$hasLogin = $r6.Content -match "txtLoginName"
Write-Host "  Status: $($r6.StatusCode) Size: $($r6.Content.Length) HasLogin: $hasLogin"

# Summary
Write-Host "`n========== SUMMARY =========="
Write-Host "Login proxy:  $(if (-not $blocked -and $hasUpstream) {'OK'} else {'BLOCKED'})"
Write-Host "WCF proxy:    $(if (-not $wcfBlocked) {'OK'} else {'BLOCKED'})"
Write-Host "Root mode:    $(if ($hasLogin) {'OK'} else {'CHECK'})"
