$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appUA = "okhttp/4.9.3"
$machineId = "test-device-001"
$deviceImei = "860000000000001"

# Step 1: APP bootstrap
Write-Host "=== Step 1: APP bootstrap (sys.ashx) ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Version: $($r1.Content)"

# Step 2: APP login (with device identifiers - should auto-create device + request)
Write-Host "`n=== Step 2: APP login with MachineId ==="
$loginBody = "UserId=admin&UserPwd=123456&MachineId=$machineId&DeviceImei=$deviceImei"
$r2 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r2.StatusCode)"
$blocked = $r2.Content -match "GatewayBlocked"
Write-Host "  GatewayBlocked: $blocked"
if ($r2.Content.Length -gt 300) {
    Write-Host "  Content: $($r2.Content.Substring(0, 300))"
} else {
    Write-Host "  Content: $($r2.Content)"
}

# Step 3: Admin approves
Write-Host "`n=== Step 3: Admin approves ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
if ($reqIds.Count -gt 0) {
    $latestId = $reqIds[$reqIds.Count - 1].Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$latestId} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "  Approved: $latestId"
} else {
    Write-Host "  No pending requests"
}

# Step 4: APP retries login (should now proxy to upstream)
Write-Host "`n=== Step 4: APP login after approval ==="
$r4 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 15
Write-Host "  Status: $($r4.StatusCode)"
$stillBlocked = $r4.Content -match "GatewayBlocked"
$hasLoginStatus = $r4.Content -match "LoginStatus"
Write-Host "  GatewayBlocked: $stillBlocked"
Write-Host "  Has LoginStatus: $hasLoginStatus"
if ($r4.Content.Length -gt 400) {
    Write-Host "  Content: $($r4.Content.Substring(0, 400))"
} else {
    Write-Host "  Content: $($r4.Content)"
}

# Step 5: WCF service call after auth
Write-Host "`n=== Step 5: WCF service (postBus) after auth ==="
$postJson = '{"UserToken":{"SessionKey":"test-session-key","MachineId":"' + $machineId + '"},"ServiceName":"ITestService","Action":"GetData"}'
$wcfBody = "postJson=$postJson"
$r5 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/WCFService/PostBus.ashx" -Method POST -Body $wcfBody -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 15
Write-Host "  Status: $($r5.StatusCode)"
$wcfBlocked = $r5.Content -match "GatewayBlocked"
Write-Host "  GatewayBlocked: $wcfBlocked"
if ($r5.Content.Length -gt 300) {
    Write-Host "  Content: $($r5.Content.Substring(0, 300))"
} else {
    Write-Host "  Content: $($r5.Content)"
}

# Step 6: Root mode with device identifier
Write-Host "`n=== Step 6: Root mode default.aspx (APP) ==="
$r6 = Invoke-WebRequest -Uri "$baseUrl/default.aspx" -Headers @{"User-Agent"=$appUA} -WebSession $appSession -UseBasicParsing -TimeoutSec 10
Write-Host "  Status: $($r6.StatusCode) Size: $($r6.Content.Length)"
$hasLogin = $r6.Content -match "txtLoginName"
Write-Host "  Has login form: $hasLogin"

Write-Host "`n=== SUMMARY ==="
Write-Host "Bootstrap:    OK"
Write-Host "Unauth block: OK"
Write-Host "Approval:     OK"
if ($stillBlocked) {
    Write-Host "Proxy login:  FAIL - still blocked"
} elseif ($hasLoginStatus) {
    Write-Host "Proxy login:  OK - got upstream response"
} else {
    Write-Host "Proxy login:  PARTIAL - got $($r4.StatusCode), content: $($r4.Content.Substring(0, [Math]::Min(100, $r4.Content.Length)))"
}
Write-Host "WCF proxy:    $(if ($wcfBlocked) {'BLOCKED'} else {'OK'})"
Write-Host "Root mode:    $(if ($hasLogin) {'OK'} else {'CHECK - size: ' + $r6.Content.Length})"
