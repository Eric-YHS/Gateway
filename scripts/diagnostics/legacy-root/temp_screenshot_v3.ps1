$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Import Win32 InternetSetCookie
$code = '
[DllImport("wininet.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern bool InternetSetCookie(string lpszUrlName, string lpszCookieName, string lpszCookieData);
'
$wininet = Add-Type -MemberDefinition $code -Name WininetHelper -Namespace Win32API -PassThru

$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'

# Step 1: Create approved session via HTTP
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$r1 = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 10

$reqBody = @{
    action='request-access'
    companyName='ScreenDemo'
    applicantName='Admin'
    phone='13800138000'
    reason='screenshot'
    targetPath='/proxy/erp-main/default.aspx'
}
$submit = Invoke-WebRequest -Uri 'http://localhost:5050/Gateway/Default.aspx' -Method POST -Body $reqBody -WebSession $session -UseBasicParsing -TimeoutSec 10

$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Verify proxy works via HTTP
$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host "Proxy: $($proxy.Content.Length) bytes, has login: $($proxy.Content -match 'txtLoginName')"

$cookieValue = ($session.Cookies.GetCookies('http://localhost:5050') | Where-Object { $_.Name -eq 'gw_device_credential' }).Value
Write-Host "Cookie: $cookieValue"

# Step 2: Set cookie via Win32 InternetSetCookie
$result = $wininet::InternetSetCookie('http://localhost:5050', 'gw_device_credential', "$cookieValue; path=/")
Write-Host "InternetSetCookie: $result (err=$([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))"

# Step 3: WebBrowser screenshot
$form = New-Object System.Windows.Forms.Form
$form.Size = New-Object System.Drawing.Size(1480, 960)
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(-3000, 0)
$form.ShowInTaskbar = $false
$form.Opacity = 0

$browser = New-Object System.Windows.Forms.WebBrowser
$browser.Size = New-Object System.Drawing.Size(1440, 900)
$browser.ScriptErrorsSuppressed = $true
$form.Controls.Add($browser)
$form.Show()

$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$waited = 0
while ($browser.ReadyState -ne 'Complete' -and $waited -lt 200) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
    $waited++
}
Start-Sleep -Seconds 2
Write-Host "Loaded: $($browser.DocumentTitle), size: $($browser.DocumentText.Length)"

$isLogin = $browser.DocumentText -match 'txtLoginName'
Write-Host "Has ERP login: $isLogin"

$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap.Save("$screenshotDir\05-erp-login-proxied.png")
$bitmap.Dispose()
Write-Host "Screenshot saved: 05-erp-login-proxied.png (isLogin=$isLogin)"

# Step 4: Admin dashboard - set admin session cookie via Win32 API
$adminCookies = $admin.Cookies.GetCookies('http://localhost:5051')
foreach ($c in $adminCookies) {
    $wininet::InternetSetCookie('http://localhost:5051', $c.Name, "$($c.Value); path=/") | Out-Null
    Write-Host "Set admin cookie: $($c.Name)=$($c.Value)"
}

$form.Controls.Remove($browser)
$browser2 = New-Object System.Windows.Forms.WebBrowser
$browser2.Size = New-Object System.Drawing.Size(1440, 900)
$browser2.ScriptErrorsSuppressed = $true
$form.Controls.Add($browser2)

$browser2.Navigate('http://localhost:5051/Admin/Default.aspx')
$waited = 0
while ($browser2.ReadyState -ne 'Complete' -and $waited -lt 200) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
    $waited++
}
Start-Sleep -Seconds 2
Write-Host "Admin page: $($browser2.DocumentTitle), size: $($browser2.DocumentText.Length)"

$hasDashboard = $browser2.DocumentText -match ''
Write-Host "Has dashboard: $hasDashboard"

$bitmap2 = New-Object System.Drawing.Bitmap(1440, 900)
$browser2.DrawToBitmap($bitmap2, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap2.Save("$screenshotDir\06-admin-dashboard.png")
$bitmap2.Dispose()
Write-Host "Screenshot saved: 06-admin-dashboard.png"

$form.Close()
Write-Host "Done!"
