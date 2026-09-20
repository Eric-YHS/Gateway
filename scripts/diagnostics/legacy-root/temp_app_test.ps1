$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5050"
$appHeaders = @{"User-Agent"="okhttp/4.9.3"; "Accept-Language"="zh-CN,zh;q=0.9"}
$passCount = 0
$failCount = 0

function Write-Result($name, $pass, $status, $detail) {
    $tag = "PASS"
    if (-not $pass) { $tag = "FAIL"; $script:failCount++ } else { $script:passCount++ }
    Write-Host "$tag [$status] $name"
    Write-Host "  $detail"
}

# Setup A: create and approve APP device
Write-Host "=== Setup A: Create and approve APP device ==="
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/default.aspx" -Method GET -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10 | Out-Null
} catch {}

$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method GET -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $reqIds) {
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($reqIds.Count) request(s)"

# Test 1: sys.ashx version (proxy mode, bootstrap - no auth needed)
Write-Host "`n=== Test 1: sys.ashx version (proxy mode) ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -UseBasicParsing -TimeoutSec 10
    Write-Result "sys.ashx version" ($r.Content -match "6.26.0") $r.StatusCode $r.Content
} catch { Write-Result "sys.ashx version" $false "ERR" $_.Exception.Message }

# Test 2: sys.ashx tenant_name (proxy mode)
Write-Host "`n=== Test 2: sys.ashx tenant_name (proxy mode) ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=tenant_name" -UseBasicParsing -TimeoutSec 10
    Write-Result "sys.ashx tenant_name" ($r.StatusCode -eq 200) $r.StatusCode $r.Content
} catch { Write-Result "sys.ashx tenant_name" $false "ERR" $_.Exception.Message }

# Test 3: sys.ashx fileversion (proxy mode)
Write-Host "`n=== Test 3: sys.ashx fileversion (proxy mode) ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=fileversion" -UseBasicParsing -TimeoutSec 10
    Write-Result "sys.ashx fileversion" ($r.Content -match "6.26.0") $r.StatusCode $r.Content
} catch { Write-Result "sys.ashx fileversion" $false "ERR" $_.Exception.Message }

# Test 4: Root path with APP UA (no auth, returns status JSON)
Write-Host "`n=== Test 4: Root path with APP UA ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/" -Headers $appHeaders -UseBasicParsing -TimeoutSec 10
    Write-Result "root APP UA" ($r.Content -match "GatewayReady") $r.StatusCode ($r.Content.Substring(0, [Math]::Min(80, $r.Content.Length)))
} catch { Write-Result "root APP UA" $false "ERR" $_.Exception.Message }

# Test 5: APP login unauthorized (no cookie, should be blocked)
Write-Host "`n=== Test 5: APP login unauthorized ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=test&UserPwd=test" -Headers $appHeaders -UseBasicParsing -TimeoutSec 10
    Write-Result "APP login unauthorized" ($r.Content -match "GatewayBlocked") $r.StatusCode ($r.Content.Substring(0, [Math]::Min(100, $r.Content.Length)))
} catch { Write-Result "APP login unauthorized" $false "ERR" $_.Exception.Message }

# Test 6: sys.ashx root mode (ExposeLegacyAppAtRoot)
Write-Host "`n=== Test 6: sys.ashx root mode ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/ashx/sys.ashx?action=version" -Headers $appHeaders -UseBasicParsing -TimeoutSec 10
    Write-Result "sys.ashx root" ($r.Content -match "6.26.0") $r.StatusCode $r.Content
} catch { Write-Result "sys.ashx root" $false "ERR" $_.Exception.Message }

# Test 7: WCF root mode (unauthorized, should be blocked)
Write-Host "`n=== Test 7: WCF root mode ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/WCFService/PostBus.ashx" -Method POST -Body "postJson={""test"":1}" -Headers $appHeaders -UseBasicParsing -TimeoutSec 10
    $blocked = $r.Content -match "GatewayBlocked"
    Write-Result "WCF root" $blocked $r.StatusCode ($r.Content.Substring(0, [Math]::Min(80, $r.Content.Length)))
} catch { Write-Result "WCF root" $false "ERR" $_.Exception.Message }

# Test 8: APP proxy with authorized session (APP headers + approved cookie)
Write-Host "`n=== Test 8: APP proxy authorized ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body "UserId=admin&UserPwd=123456" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 15
    $proxyOk = ($r.Content -match "LoginStatus") -and -not ($r.Content -match "GatewayBlocked")
    Write-Result "APP proxy authorized" $proxyOk $r.StatusCode ($r.Content.Substring(0, [Math]::Min(100, $r.Content.Length)))
} catch { Write-Result "APP proxy authorized" $false "ERR" $_.Exception.Message }

# Test 9: default.aspx root mode APP (authorized)
Write-Host "`n=== Test 9: default.aspx root mode APP ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/default.aspx" -Headers $appHeaders -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    $hasLogin = $r.Content -match "txtLoginName"
    Write-Result "root mode APP" $hasLogin $r.StatusCode "has login form: $hasLogin, size: $($r.Content.Length)"
} catch { Write-Result "root mode APP" $false "ERR" $_.Exception.Message }

Write-Host "`n=== RESULTS: $passCount PASS / $failCount FAIL ==="
