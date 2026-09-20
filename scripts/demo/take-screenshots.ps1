$ErrorActionPreference = 'Stop'
$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edge)) { $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
if (-not (Test-Path $edge)) { Write-Host 'ERROR: Edge not found'; exit 1 }

Write-Host "Edge: $edge"

function Snap($url, $name) {
    $out = Join-Path $screenshotDir "$name.png"
    $argList = "--headless --disable-gpu --screenshot=`"$out`" --window-size=1440,900 --default-background-color=0xFFFFFFFF `$url"
    Write-Host "  $name -> $url"
    $p = Start-Process -FilePath $edge -ArgumentList @('--headless','--disable-gpu',"--screenshot=$out",'--window-size=1440,900', $url) -PassThru -NoNewWindow
    $p.WaitForExit(15000) | Out-Null
    if (-not $p.HasExited) { $p.Kill() }
    if (Test-Path $out) {
        $sz = (Get-Item $out).Length
        Write-Host "    -> $out ($sz bytes)"
    } else {
        Write-Host "    -> FAILED (file not created)"
    }
}

# 01: 网关公开页
Write-Host '=== 01: Gateway public page ==='
Snap 'http://localhost:5050/gateway' '01-gateway-public'

# 02: 管理后台登录
Write-Host '=== 02: Admin login ==='
Snap 'http://localhost:5051/Admin/Default.aspx' '02-admin-login'

# 03: 管理端口隔离（公网访问/admin）
Write-Host '=== 03: Admin blocked on public ==='
Snap 'http://localhost:5050/admin' '03-admin-blocked-on-public'

# 准备：审批设备
Write-Host '`n=== Setup ==='
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try { Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10 | Out-Null } catch {}
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $reqIds) {
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "  Approved: $($reqIds.Count)"

$wininet = Add-Type -MemberDefinition '[DllImport("wininet.dll")] public static extern bool InternetSetCookie(string a, string b, string c);' -Name WininetEdge2 -Namespace Win32Edge2 -PassThru
$cookies = $session.Cookies.GetCookies('http://localhost:5050')
foreach ($c in $cookies) {
    $wininet::InternetSetCookie('http://localhost:5050', $null, "$($c.Name)=$($c.Value); path=/") | Out-Null
}
$adminCookies = $admin.Cookies.GetCookies('http://localhost:5051')
foreach ($c in $adminCookies) {
    $wininet::InternetSetCookie('http://localhost:5051', $null, "$($c.Name)=$($c.Value); path=/") | Out-Null
}
Write-Host "  Cookies: $($cookies.Count) + $($adminCookies.Count)"

# 04: 管理后台仪表盘
Write-Host '`n=== 04: Admin dashboard ==='
Snap 'http://localhost:5051/Admin/Default.aspx' '04-admin-dashboard'

# 05: ERP登录页（审批后代理）
Write-Host '`n=== 05: ERP login proxied ==='
Snap 'http://localhost:5050/proxy/erp-main/default.aspx' '05-erp-login-proxied'

# 06: 网关首页（已授权状态）
Write-Host '`n=== 06: Gateway authorized ==='
Snap 'http://localhost:5050/gateway' '06-gateway-authorized'

# 07: 撤销后再访问
Write-Host '`n=== 07: After revoke ==='
$dash2 = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$authIds = [regex]::Matches($dash2.Content, 'name="authorizationId"\s+value="([0-9a-f-]+)"')
if ($authIds.Count -gt 0) {
    $revokeId = $authIds[$authIds.Count - 1].Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='revoke-authorization';authorizationId=$revokeId} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
    Write-Host "  Revoked: $revokeId"
}
Snap 'http://localhost:5050/proxy/erp-main/default.aspx' '07-after-revoke-blocked'

# APP JSON 响应
Write-Host "`n=== 08: APP blocked JSON ==="
$r8 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/user/login' -Method POST -Body @{UserId='test';UserPwd='test';MachineId='scr-device';DeviceImei='scr-device'} -Headers @{'User-Agent'='okhttp/4.9.3';'Accept-Language'='zh-CN,zh;q=0.9'} -UseBasicParsing -TimeoutSec 10
$r8.Content | Out-File (Join-Path $screenshotDir '08-app-blocked-json.txt') -Encoding utf8
Write-Host "  Saved"

Write-Host "`n=== DONE ==="
Get-ChildItem $screenshotDir -Filter *.png | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)  ($([Math]::Round($_.Length/1024)) KB)" }
