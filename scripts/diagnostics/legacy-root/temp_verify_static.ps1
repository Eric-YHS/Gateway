$ErrorActionPreference = 'Stop'

# This test reuses the same session cookie to verify static resource proxy
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Step 1: Visit proxy page (establishes device cookie)
Write-Host 'Step 1: Visit proxy page (no session yet)...'
$r1 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10
$isGateway = $r1.Content -match 'request-access'
Write-Host ('  Status: {0}  Size: {1}  IsGateway: {2}' -f $r1.StatusCode, $r1.Content.Length, $isGateway)

# Show cookies we have
Write-Host ''
Write-Host 'Cookies after first request:'
foreach ($c in $session.Cookies.GetCookies('http://localhost:5050')) {
    Write-Host ('  {0} = {1} (Domain: {2})' -f $c.Name, $c.Value.Substring(0, [Math]::Min(20, $c.Value.Length)), $c.Domain)
}

# Step 2: Submit access request
Write-Host ''
Write-Host 'Step 2: Submit access request...'
$reqBody = @{
    action='request-access'
    companyName='Static Test Corp'
    applicantName='Static Tester'
    phone='13800138001'
    reason='static-resource-test'
    targetPath='/proxy/erp-main/default.aspx'
}
$submit = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $session -UseBasicParsing -TimeoutSec 10
Write-Host ('  Submit: {0}' -f $submit.StatusCode)

# Step 3: Admin approve
Write-Host ''
Write-Host 'Step 3: Admin approve all...'
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host ('  Admin login: {0}' -f $loginR.StatusCode)

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)
foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host ('  Approved {0} requests' -f $allIds.Count)

# Step 4: Re-visit proxy page (should now get upstream content)
Write-Host ''
Write-Host 'Step 4: Re-visit proxy page (should be approved now)...'
$r2 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host ('  Status: {0}  Size: {1}' -f $r2.StatusCode, $r2.Content.Length)

# Step 5: Static resources WITH session cookie
Write-Host ''
Write-Host 'Step 5: Static resources with session cookie...'

$staticResources = @(
    '/proxy/erp-main/Resource/CSS/default.css',
    '/proxy/erp-main/Resource/JavaScript/jQuery/jquery.min.js',
    '/proxy/erp-main/Resource/JavaScript/jDefault.js',
    '/proxy/erp-main/Resource/JavaScript/WebEnterprise/jSecurity.js',
    '/proxy/erp-main/Resource/JavaScript/jQuery/jquery-ui-1.8.21/jquery-ui.min.js'
)

foreach ($res in $staticResources) {
    try {
        $sr = Invoke-WebRequest -Uri ('http://localhost:5050' + $res) -WebSession $session -UseBasicParsing -TimeoutSec 10
        $isHtml = $sr.Content -match 'request-access'
        $desc = $res.Split('/')[-1]
        if ($isHtml) {
            Write-Host ('  FAIL [{0}] {1} - got gateway HTML ({2} bytes)' -f $sr.StatusCode, $desc, $sr.Content.Length)
        } else {
            Write-Host ('  PASS [{0}] {1} ({2} bytes)' -f $sr.StatusCode, $desc, $sr.Content.Length)
        }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Host ('  FAIL [{0}] {1} - {2}' -f $code, $res.Split('/')[-1], $_.Exception.Message)
    }
}

Write-Host ''
Write-Host 'Done.'
