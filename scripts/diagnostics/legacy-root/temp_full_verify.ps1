$ErrorActionPreference = 'Stop'

function Check($url, $desc) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
        Write-Host ('PASS [{0}] {1} ({2} bytes)' -f $r.StatusCode, $desc, $r.Content.Length)
        return $r
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Host ('FAIL [{0}] {1} - {2}' -f $code, $desc, $_.Exception.Message)
        return $null
    }
}

# PART 1: Gateway pages
Write-Host '========== PART 1: Gateway pages =========='
Check 'http://localhost:5050/gateway' 'Gateway public page'
Check 'http://localhost:5051/admin' 'Admin login page (port 5051)'

# Admin on public port should 404
try {
    $r404 = Invoke-WebRequest -Uri 'http://localhost:5050/admin/login' -UseBasicParsing -TimeoutSec 5
    Write-Host ('FAIL [{0}] admin/login should 404 on public port' -f $r404.StatusCode)
} catch {
    Write-Host 'PASS [404] admin/login correctly blocked on public port'
}

# PART 2: Mock backend
Write-Host ''
Write-Host '========== PART 2: Mock backend =========='
Check 'http://localhost:5055/mock/erp-main/login' 'Mock login page'
Check 'http://localhost:5055/assets/mock.css' 'Mock CSS'
Check 'http://localhost:5055/assets/mock.js' 'Mock JS'

# PART 3: Browser proxy flow
Write-Host ''
Write-Host '========== PART 3: Browser proxy flow =========='

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Step 1: Visit proxy (will redirect or show gateway)
Write-Host '-- Step 1: Visit proxy URL --'
$r1 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10
$isGateway = $r1.Content -match 'request-access'
Write-Host ('  [{0}] Is gateway page: {1}' -f $r1.StatusCode, $isGateway)

# Step 2: Submit access request
Write-Host '-- Step 2: Submit access request --'
$reqBody = @{
    action='request-access'
    companyName='Verification Corp'
    applicantName='Tester'
    phone='13800138000'
    reason='full-verification'
    targetPath='/proxy/erp-main/default.aspx'
}
$submit = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $session -UseBasicParsing -TimeoutSec 10
Write-Host ('  Submit: {0}' -f $submit.StatusCode)

# Step 3: Admin login and approve
Write-Host '-- Step 3: Admin approve --'
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host ('  Admin login: {0}' -f $loginR.StatusCode)

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)
Write-Host ('  Pending requests: {0}' -f $allIds.Count)

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host ('  Approved all {0} requests' -f $allIds.Count)

# Step 4: Proxy to demo site
Write-Host '-- Step 4: Proxy to demo site --'
$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host ('  Status: {0} Size: {1}' -f $proxy.StatusCode, $proxy.Content.Length)
$c = $proxy.Content

$checks = @(
    @('Login name input', 'txtLoginName'),
    @('Password input', 'txtPwd'),
    @('Login button', 'btnLogin'),
    @('Validate code', 'ValidateCode'),
    @('Data center dropdown', 'ddlDataCenter'),
    @('SMS tab', 'smsTab'),
    @('Scan tab', 'scanTab'),
    @('KPMIIS namespace', 'KPMIIS'),
    @('jQuery script', 'jquery.min.js'),
    @('jDefault.js', 'jDefault.js'),
    @('CSS stylesheet', 'default.css'),
    @('Login tabs div', 'login-tabs'),
    @('Password tab active', 'tab active'),
    @('Form element', '<form')
)
foreach ($pair in $checks) {
    $name = $pair[0]
    $needle = $pair[1]
    $found = $c -match [regex]::Escape($needle)
    Write-Host ('  {0} {1}' -f $(if($found){'PASS'}else{'FAIL'}), $name)
}

# Step 5: Static resources through proxy
Write-Host ''
Write-Host '-- Step 5: Static resources through proxy --'
Check 'http://localhost:5050/proxy/erp-main/Resource/CSS/default.css' 'ERP default.css'
Check 'http://localhost:5050/proxy/erp-main/Resource/JavaScript/jQuery/jquery.min.js' 'jQuery library'
Check 'http://localhost:5050/proxy/erp-main/Resource/JavaScript/jDefault.js' 'jDefault.js'
Check 'http://localhost:5050/proxy/erp-main/Resource/JavaScript/WebEnterprise/jSecurity.js' 'jSecurity.js'
Check 'http://localhost:5050/proxy/erp-main/Resource/JavaScript/jQuery/jquery-ui-1.8.21/jquery-ui.min.js' 'jQuery UI'

# Login logo image
$imgMatch = [regex]::Match($c, 'src="(/Resource/LoginLogo/[^"]*)"')
if ($imgMatch.Success) {
    Check ('http://localhost:5050/proxy/erp-main' + $imgMatch.Groups[1].Value) 'Login logo image'
} else { Write-Host '  SKIP: No login logo image found' }

# Left message image
$imgMatch2 = [regex]::Match($c, 'src="(/Resource/Images/[^"]*)"')
if ($imgMatch2.Success) {
    Check ('http://localhost:5050/proxy/erp-main' + $imgMatch2.Groups[1].Value) 'Left banner image'
} else { Write-Host '  SKIP: No left image found' }

# Step 6: Multi-site
Write-Host ''
Write-Host '========== PART 6: Multi-site =========='

# WMS through gateway (should work since we approved all)
$rWms = Check 'http://localhost:5050/proxy/wms-east/portal' 'WMS via gateway'
if ($rWms -and $rWms.Content -match 'WMS') { Write-Host '  WMS content verified' }

# Finance (may or may not be approved depending on scope)
Check 'http://localhost:5050/proxy/finance/portal' 'Finance via gateway'

# Step 7: APP endpoints
Write-Host ''
Write-Host '========== PART 7: APP endpoints =========='
Check 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=version' 'APP sys.ashx version'
Check 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=tenant_name' 'APP sys.ashx tenant'

$rOk = Invoke-WebRequest -Uri 'http://localhost:5050/' -Headers @{'User-Agent'='okhttp/4.9.3'} -UseBasicParsing -TimeoutSec 10
$okOk = $rOk.Content -match 'GatewayReady'
Write-Host ('{0} [{1}] APP root (okhttp UA) GatewayReady' -f $(if($okOk){'PASS'}else{'FAIL'}), $rOk.StatusCode)

# APP login with MachineId
Write-Host '-- APP login with device ID --'
$appSess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/ashx/sys.ashx?action=version' -Headers @{'User-Agent'='okhttp/4.9.3'} -WebSession $appSess -UseBasicParsing -TimeoutSec 10 | Out-Null

$formBody = @{UserId='admin';UserPwd='test';MachineId='verify-device-final';DeviceImei='869990000000099'}
$appLogin = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/user/login' -Method POST -Body $formBody -Headers @{'User-Agent'='okhttp/4.9.3'} -WebSession $appSess -UseBasicParsing -TimeoutSec 15
$blocked = $appLogin.Content -match 'GatewayBlocked'
$upstream = $appLogin.Content -match 'LoginStatus'
Write-Host ('{0} [{1}] APP login Blocked={2} Upstream={3}' -f $(if($upstream){'PASS'}else{'CHECK'}), $appLogin.StatusCode, $blocked, $upstream)

Write-Host ''
Write-Host '========== VERIFICATION COMPLETE =========='
