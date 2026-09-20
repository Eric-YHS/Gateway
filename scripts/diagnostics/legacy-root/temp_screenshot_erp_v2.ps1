$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'
if (-not (Test-Path $screenshotDir)) { New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null }

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
$pattern = 'name="requestId"\s+value="([0-9a-f-]+)"'
$allIds = [regex]::Matches($dash.Content, $pattern)

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Step 2: Save the proxied HTML with full URL base
$proxy = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -WebSession $session -UseBasicParsing -TimeoutSec 15
Write-Host "Proxy HTML: $($proxy.Content.Length) bytes"

$cookieValue = ($session.Cookies.GetCookies('http://localhost:5050') | Where-Object { $_.Name -eq 'gw_device_credential' }).Value
Write-Host "Cookie: $cookieValue"

# Save HTML to temp file with base tag for proper rendering
$html = $proxy.Content
$tempHtml = "$env:TEMP\erp-login-capture.html"

# Insert base tag after <head> or at beginning
if ($html -match '<head[^>]*>') {
    $html = $html -replace '(<head[^>]*>)', "`$1<base href='http://localhost:5050/proxy/erp-main/default.aspx'>"
} else {
    $html = "<base href='http://localhost:5050/proxy/erp-main/default.aspx'>" + $html
}

$html | Out-File -FilePath $tempHtml -Encoding utf8 -Force
Write-Host "HTML saved to: $tempHtml"

# Step 3: Use WebBrowser to render and capture
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

# Navigate to gateway first to set cookie
$browser.Navigate('http://localhost:5050/gateway')
while ($browser.ReadyState -ne 'Complete') { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
Write-Host "Gateway loaded: $($browser.DocumentTitle)"

# Set cookie
$browser.Document.Cookie = "gw_device_credential=$cookieValue"

# Navigate to proxy page
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$maxWait = 30
$waited = 0
while ($browser.ReadyState -ne 'Complete' -and $waited -lt $maxWait) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    $waited += 0.1
}
Write-Host "Proxy loaded: $($browser.DocumentTitle) size: $($browser.DocumentText.Length)"

$isLogin = $browser.DocumentText -match 'txtLoginName'
Write-Host "Has ERP login form: $isLogin"

if ($isLogin) {
    $bitmap = New-Object System.Drawing.Bitmap(1440, 900)
    $browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
    $outPath = "$screenshotDir\05-erp-login-proxied.png"
    $bitmap.Save($outPath)
    $bitmap.Dispose()
    Write-Host "SUCCESS: Screenshot saved to $outPath"
} else {
    Write-Host "FAILED: Still showing gateway, trying local HTML approach..."
    $browser.Navigate("file:///$tempHtml")
    while ($browser.ReadyState -ne 'Complete') { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
    Start-Sleep -Seconds 2

    $bitmap = New-Object System.Drawing.Bitmap(1440, 900)
    $browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
    $outPath = "$screenshotDir\05-erp-login-proxied.png"
    $bitmap.Save($outPath)
    $bitmap.Dispose()
    Write-Host "Screenshot saved (local HTML): $outPath"
}

# Also capture admin dashboard
$browser2 = New-Object System.Windows.Forms.WebBrowser
$browser2.Size = New-Object System.Drawing.Size(1440, 900)
$browser2.ScriptErrorsSuppressed = $true
$form.Controls.Remove($browser)
$form.Controls.Add($browser2)

$browser2.Navigate('http://localhost:5051/Admin/Default.aspx')
while ($browser2.ReadyState -ne 'Complete') { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
Write-Host "Admin login page: $($browser2.DocumentTitle)"

# Try to login via DOM
$doc = $browser2.Document
$inputs = $doc.GetElementsByTagName('input')
foreach ($inp in $inputs) {
    if ($inp.Name -eq 'username') { $inp.SetAttribute('value', 'gateway-admin') }
    if ($inp.Name -eq 'password') { $inp.SetAttribute('value', 'GatewayDemo!2026') }
}

# Find and click submit
$buttons = $doc.GetElementsByTagName('button')
foreach ($btn in $buttons) {
    if ($btn.InnerText -match '') {
        $btn.InvokeMember('click')
        break
    }
}

Start-Sleep -Seconds 3
[System.Windows.Forms.Application]::DoEvents()
Write-Host "After login attempt: $($browser2.DocumentTitle)"

$bitmap2 = New-Object System.Drawing.Bitmap(1440, 900)
$browser2.DrawToBitmap($bitmap2, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap2.Save("$screenshotDir\06-admin-dashboard.png")
$bitmap2.Dispose()
Write-Host "Admin screenshot saved: 06-admin-dashboard.png"

$form.Close()
