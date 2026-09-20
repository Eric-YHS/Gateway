$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'

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

# Step 1: Visit proxy to get intercepted by gateway (creates device with WebBrowser fingerprint)
Write-Host "Step 1: Visit proxy..."
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 10000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser.ReadyState -eq 'Complete') { break }
}
Write-Host "Page: $($browser.DocumentTitle), ready=$($browser.ReadyState)"

# Step 2: Fill and submit access request form
Write-Host "Step 2: Submit access request..."
Start-Sleep -Milliseconds 500
$doc = $browser.Document

$inputs = $doc.GetElementsByTagName('input')
foreach ($inp in $inputs) {
    $name = $inp.GetAttribute('name')
    $id = $inp.GetAttribute('id')
    if ($name -eq 'companyName' -or $id -eq 'companyName') { $inp.SetAttribute('value', 'ScreenDemo') }
    if ($name -eq 'applicantName' -or $id -eq 'applicantName') { $inp.SetAttribute('value', 'Admin') }
    if ($name -eq 'phone' -or $id -eq 'phone') { $inp.SetAttribute('value', '13800138000') }
}

$textareas = $doc.GetElementsByTagName('textarea')
foreach ($ta in $textareas) {
    $name = $ta.GetAttribute('name')
    $id = $ta.GetAttribute('id')
    if ($name -eq 'reason' -or $id -eq 'reason') { $ta.InnerText = 'screenshot' }
}

# Click submit
$buttons = $doc.GetElementsByTagName('button')
foreach ($btn in $buttons) {
    if ($btn.GetAttribute('type') -eq 'submit') {
        $btn.InvokeMember('click')
        Write-Host "Clicked submit"
        break
    }
}

$sw.Restart()
while ($sw.ElapsedMilliseconds -lt 8000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser.ReadyState -eq 'Complete') { break }
}
Write-Host "After submit: $($browser.DocumentTitle)"

# Step 3: Approve via HTTP
Write-Host "Step 3: Admin approve..."
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$allIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $allIds) {
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Step 4: Navigate to proxy again
Write-Host "Step 4: Revisit proxy..."
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')

# Wait up to 15 seconds, but don't require Complete - ERP page resources may stall
$sw.Restart()
while ($sw.ElapsedMilliseconds -lt 15000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    # Check if we got the ERP login page
    if ($browser.DocumentText -match 'txtLoginName') {
        Write-Host "Found ERP login! (at $($sw.ElapsedMilliseconds)ms)"
        # Wait a bit more for rendering
        Start-Sleep -Seconds 2
        [System.Windows.Forms.Application]::DoEvents()
        break
    }
    if ($browser.ReadyState -eq 'Complete') { break }
}

$size = 0
if ($browser.DocumentText) { $size = $browser.DocumentText.Length }
Write-Host "Final: $($browser.DocumentTitle), size: $size"
$isLogin = $false
if ($browser.DocumentText) { $isLogin = $browser.DocumentText -match 'txtLoginName' }
Write-Host "Has ERP login: $isLogin"

# Take screenshot
$outPath = Join-Path $screenshotDir '05-erp-login-proxied.png'
$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

if (Test-Path $outPath) {
    $fi = Get-Item $outPath
    Write-Host "Screenshot: $outPath ($($fi.Length) bytes, isLogin=$isLogin)"
}

$form.Close()
Write-Host "Done!"
