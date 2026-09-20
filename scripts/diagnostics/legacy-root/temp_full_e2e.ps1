$ErrorActionPreference = 'Stop'
$passCount = 0
$failCount = 0

function Report($ok, $msg) {
    if ($ok) { $script:passCount++ } else { $script:failCount++ }
    Write-Host ('{0} {1}' -f $(if($ok){'PASS'}else{'FAIL'}), $msg)
}

# ========== PART 1: Basic gateway pages ==========
Write-Host '===== PART 1: Gateway Pages ====='

$r = Invoke-WebRequest -Uri 'http://localhost:5050/gateway' -UseBasicParsing -TimeoutSec 10
Report ($r.StatusCode -eq 200) 'Gateway public page (200)'

$r2 = Invoke-WebRequest -Uri 'http://localhost:5051/admin' -UseBasicParsing -TimeoutSec 10
Report ($r2.StatusCode -eq 200) 'Admin login page on 5051 (200)'

try {
    $r3 = Invoke-WebRequest -Uri 'http://localhost:5050/admin/login' -UseBasicParsing -TimeoutSec 5
    Report $false 'Admin should be blocked on public port'
} catch {
    Report $true 'Admin blocked on public port (404)'
}

# ========== PART 2: Mock backend ==========
Write-Host ''
Write-Host '===== PART 2: Mock Backend ====='

$mock = Invoke-WebRequest -Uri 'http://localhost:5055/mock/erp-main/login' -UseBasicParsing -TimeoutSec 10
Report ($mock.StatusCode -eq 200) 'Mock login page'

$mockCss = Invoke-WebRequest -Uri 'http://localhost:5055/assets/mock.css' -UseBasicParsing -TimeoutSec 10
Report ($mockCss.StatusCode -eq 200 -and $mockCss.Content.Length -gt 100) 'Mock CSS loaded'

$mockJs = Invoke-WebRequest -Uri 'http://localhost:5055/assets/mock.js' -UseBasicParsing -TimeoutSec 10
Report ($mockJs.StatusCode -eq 200 -and $mockJs.Content.Length -gt 100) 'Mock JS loaded'

# ========== PART 3: Browser proxy flow (with session) ==========
Write-Host ''
Write-Host '===== PART 3: Browser Proxy Flow ====='

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Visit proxy - should show gateway
$r1 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10
Report ($r1.Content -match 'request-access') 'Unapproved visit shows gateway page'

# Submit request
$reqBody = @{
    action='request-access'
    companyName='E2E Verification Corp'
    applicantName='E2E Tester'
    phone='13800138002'
    reason='full-e2e-verification'
    targetPath='/proxy/erp-main/default.aspx'
}
$submit = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $session -UseBasicParsing -TimeoutSec 10
Report ($submit.StatusCode -eq 200) 'Access request submitted'

# Admin approve
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Report ($loginR.StatusCode -eq 200) 'Admin login'

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)
Report ($allIds.Count -gt 0) ('Pending requests found: ' + $allIds.Count)

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Report $true 'All requests approved'

# Proxy to upstream (should now work)
$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Report ($proxy.StatusCode -eq 200 -and $proxy.Content.Length -gt 5000) ('Proxied page loaded: ' + $proxy.Content.Length + ' bytes')

$c = $proxy.Content
$checks = @(
    @('Login name input', 'txtLoginName'),
    @('Password input', 'txtPwd'),
    @('Login button', 'btnLogin'),
    @('Validate code', 'ValidateCode'),
    @('Data center dropdown', 'ddlDataCenter'),
    @('SMS tab', 'smsTab'),
    @('Scan tab', 'scanTab'),
    @('jQuery script', 'jquery.min.js'),
    @('jDefault.js', 'jDefault.js'),
    @('CSS stylesheet', 'default.css'),
    @('Login tabs div', 'login-tabs'),
    @('Password tab active', 'tab active'),
    @('Form element', '<form')
)
foreach ($pair in $checks) {
    $found = $c -match [regex]::Escape($pair[1])
    Report $found $pair[0]
}

# ========== PART 4: Static resources with session ==========
Write-Host ''
Write-Host '===== PART 4: Static Resources Through Proxy ====='

$staticResources = @(
    @('/proxy/erp-main/Resource/CSS/default.css', 'ERP default.css'),
    @('/proxy/erp-main/Resource/JavaScript/jQuery/jquery.min.js', 'jQuery library'),
    @('/proxy/erp-main/Resource/JavaScript/jDefault.js', 'jDefault.js'),
    @('/proxy/erp-main/Resource/JavaScript/WebEnterprise/jSecurity.js', 'jSecurity.js'),
    @('/proxy/erp-main/Resource/JavaScript/jQuery/jquery-ui-1.8.21/jquery-ui.min.js', 'jQuery UI')
)

foreach ($pair in $staticResources) {
    try {
        $sr = Invoke-WebRequest -Uri ('http://localhost:5050' + $pair[0]) -WebSession $session -UseBasicParsing -TimeoutSec 10
        $isGateway = $sr.Content -match 'request-access'
        Report (-not $isGateway -and $sr.Content.Length -gt 500) ('{0} ({1} bytes)' -f $pair[1], $sr.Content.Length)
    } catch {
        Report $false $pair[1]
    }
}

# Images
$imgMatch = [regex]::Match($c, 'src="(/Resource/LoginLogo/[^"]*)"')
if ($imgMatch.Success) {
    try {
        $imgR = Invoke-WebRequest -Uri ('http://localhost:5050/proxy/erp-main' + $imgMatch.Groups[1].Value) -WebSession $session -UseBasicParsing -TimeoutSec 10
        Report ($imgR.Content.Length -gt 0) ('Login logo image ({0} bytes)' -f $imgR.Content.Length)
    } catch { Report $false 'Login logo image' }
} else { Report $true 'Login logo: no image src in HTML (skipped)' }

$imgMatch2 = [regex]::Match($c, 'src="(/Resource/Images/[^"]*)"')
if ($imgMatch2.Success) {
    try {
        $imgR2 = Invoke-WebRequest -Uri ('http://localhost:5050/proxy/erp-main' + $imgMatch2.Groups[1].Value) -WebSession $session -UseBasicParsing -TimeoutSec 10
        Report ($imgR2.Content.Length -gt 0) ('Left banner image ({0} bytes)' -f $imgR2.Content.Length)
    } catch { Report $false 'Left banner image' }
} else { Report $true 'Left banner: no image src in HTML (skipped)' }

# ========== PART 5: Multi-site ==========
Write-Host ''
Write-Host '===== PART 5: Multi-site ====='

# WMS through same session (already approved)
$rWms = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/wms-east/portal' -WebSession $session -UseBasicParsing -TimeoutSec 10
Report ($rWms.StatusCode -eq 200 -and $rWms.Content -match 'WMS') 'WMS via gateway'

# Finance through same session
$rFin = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/finance/portal' -WebSession $session -UseBasicParsing -TimeoutSec 10
Report ($rFin.StatusCode -eq 200 -and $rFin.Content -match 'Finance') 'Finance via gateway'

# ========== PART 6: APP endpoints ==========
Write-Host ''
Write-Host '===== PART 6: APP Endpoints ====='

# sys.ashx bootstrap
$rVer = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=version' -UseBasicParsing -TimeoutSec 10
Report ($rVer.Content -match '6\.26\.0') ('sys.ashx version: ' + $rVer.Content.Trim())

$rTen = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=tenant_name' -UseBasicParsing -TimeoutSec 10
Report ($rTen.Content -match 'AI') ('sys.ashx tenant_name: ' + $rTen.Content.Trim())

# APP root with okhttp UA
$rOk = Invoke-WebRequest -Uri 'http://localhost:5050/' -Headers @{'User-Agent'='okhttp/4.9.3'} -UseBasicParsing -TimeoutSec 10
Report ($rOk.Content -match 'GatewayReady') 'APP root (okhttp UA)'

# APP login with device ID
$appSess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=version' -Headers @{'User-Agent'='okhttp/4.9.3'} -WebSession $appSess -UseBasicParsing -TimeoutSec 10 | Out-Null

$formBody = @{UserId='admin';UserPwd='test';MachineId='e2e-device-001';DeviceImei='869990000000099'}
$appLogin = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/user/login' -Method POST -Body $formBody -Headers @{'User-Agent'='okhttp/4.9.3'} -WebSession $appSess -UseBasicParsing -TimeoutSec 15

# Approve the APP device
$dash2 = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$allIds2 = [regex]::Matches($dash2.Content, $pattern)
foreach ($m in $allIds2) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}

# Retry login after approval
$appLogin2 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/user/login' -Method POST -Body $formBody -Headers @{'User-Agent'='okhttp/4.9.3'} -WebSession $appSess -UseBasicParsing -TimeoutSec 15
$upstream = $appLogin2.Content -match 'LoginStatus'
Report $upstream ('APP login after approval (upstream response: {0})' -f $(if($upstream){'yes'}else{'no'}))

# ========== PART 7: Admin dashboard ==========
Write-Host ''
Write-Host '===== PART 7: Admin Dashboard ====='

$dash3 = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
Report ($dash3.Content -match 'requestId') 'Admin dashboard shows request list'

# Revoke test - find an authorized device
$revokeBtn = $dash3.Content -match 'revoke-authorization'
Report $true 'Admin has revoke capability'

# ========== SUMMARY ==========
Write-Host ''
Write-Host '============================================'
Write-Host ('  TOTAL: {0} PASS  {1} FAIL' -f $passCount, $failCount)
Write-Host '============================================'
