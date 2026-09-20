$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$code = '
[DllImport("wininet.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern bool InternetSetCookie(string lpszUrlName, string lpszCookieName, string lpszCookieData);
'
$wininet = Add-Type -MemberDefinition $code -Name WininetFinal -Namespace Win32Final -PassThru

$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'

# Step 1: Get approved session cookie via HTTP
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
$allIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $allIds) {
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Verify
$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host "Proxy OK: $($proxy.Content.Length) bytes, login=$($proxy.Content -match 'txtLoginName')"

$cookieValue = ($session.Cookies.GetCookies('http://localhost:5050') | Where-Object { $_.Name -eq 'gw_device_credential' }).Value
Write-Host "Cookie: $cookieValue"

# Step 2: Set cookie via InternetSetCookie with NULL name (the working approach!)
$expires = (Get-Date).AddDays(1).ToString("R")
$cookieData = "gw_device_credential=$cookieValue; path=/; expires=$expires"
$r = $wininet::InternetSetCookie('http://localhost:5050', $null, $cookieData)
$err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
Write-Host "SetCookie (null name): r=$r err=$err"

# Verify cookie was set
$code2 = '
[DllImport("wininet.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern bool InternetGetCookie(string lpszUrlName, string lpszCookieName, System.Text.StringBuilder lpszCookieData, ref int lpdwSize);
'
$wininet2 = Add-Type -MemberDefinition $code2 -Name WininetFinal2 -Namespace Win32Final2 -PassThru
$size = 4096
$sb = New-Object System.Text.StringBuilder $size
$gr = $wininet2::InternetGetCookie('http://localhost:5050', $null, $sb, [ref]$size)
Write-Host "Verify cookie: r=$gr data='$($sb.ToString())'"

# Step 3: Create WebBrowser and navigate
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

# Navigate directly to proxy page
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$waited = 0
while ($browser.ReadyState -ne 'Complete' -and $waited -lt 300) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
    $waited++
}
Start-Sleep -Seconds 3
Write-Host "Loaded: $($browser.DocumentTitle), size: $($browser.DocumentText.Length)"

$isLogin = $browser.DocumentText -match 'txtLoginName'
Write-Host "Has ERP login: $isLogin"

$outPath = Join-Path $screenshotDir '05-erp-login-proxied.png'
$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Screenshot: $outPath (isLogin=$isLogin, size=$((Get-Item $outPath).Length))"

$form.Close()
Write-Host "Done!"
