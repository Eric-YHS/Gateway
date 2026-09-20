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

# Step 1: Navigate to proxy page (will be intercepted by gateway)
Write-Host "Step 1: Navigate to proxy..."
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$waited = 0
while ($browser.ReadyState -ne 'Complete' -and $waited -lt 200) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
    $waited++
}
Start-Sleep -Seconds 1
Write-Host "Page: $($browser.DocumentTitle), size: $($browser.DocumentText.Length)"

# Step 2: Fill in the access request form and submit
$doc = $browser.Document
$inputs = $doc.GetElementsByTagName('input')
$textarea = $doc.GetElementsByTagName('textarea')

foreach ($inp in $inputs) {
    $name = $inp.GetAttribute('name')
    $id = $inp.GetAttribute('id')
    if ($name -eq 'companyName' -or $id -eq 'companyName') {
        $inp.SetAttribute('value', 'ScreenDemo')
        Write-Host "Set companyName"
    }
    if ($name -eq 'applicantName' -or $id -eq 'applicantName') {
        $inp.SetAttribute('value', 'Admin')
        Write-Host "Set applicantName"
    }
    if ($name -eq 'phone' -or $id -eq 'phone') {
        $inp.SetAttribute('value', '13800138000')
        Write-Host "Set phone"
    }
}

foreach ($ta in $textarea) {
    $name = $ta.GetAttribute('name')
    $id = $ta.GetAttribute('id')
    if ($name -eq 'reason' -or $id -eq 'reason') {
        $ta.InnerText = 'screenshot'
        Write-Host "Set reason"
    }
}

# Find and click submit button
$buttons = $doc.GetElementsByTagName('button')
$clicked = $false
foreach ($btn in $buttons) {
    $text = $btn.InnerText
    $type = $btn.GetAttribute('type')
    $class = $btn.GetAttribute('className')
    Write-Host "Button: text='$text' type='$type' class='$class'"
    if ($type -eq 'submit' -or $text -match '���交') {
        $btn.InvokeMember('click')
        $clicked = $true
        Write-Host "Clicked submit"
        break
    }
}

if (-not $clicked) {
    # Try form submit
    $forms = $doc.GetElementsByTagName('form')
    foreach ($f in $forms) {
        Write-Host "Form: action=$($f.GetAttribute('action')) method=$($f.GetAttribute('method'))"
        $f.InvokeMember('submit')
        Write-Host "Form submitted"
        break
    }
}

# Wait for response
$waited = 0
while ($browser.ReadyState -ne 'Complete' -and $waited -lt 200) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
    $waited++
}
Start-Sleep -Seconds 1
Write-Host "After submit: $($browser.DocumentTitle)"

# Step 3: Approve via HTTP (admin)
Write-Host "Step 3: Admin approval..."
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host "Admin login: $($loginR.StatusCode)"

$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$allIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
Write-Host "Pending requests: $($allIds.Count)"

foreach ($m in $allIds) {
    $rid = $m.Groups[1].Value
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$rid} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Step 4: Navigate to proxy again (now should be approved)
Write-Host "Step 4: Navigate to proxy..."
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

# Take screenshot
$outPath = Join-Path $screenshotDir '05-erp-login-proxied.png'
$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Screenshot: $outPath (isLogin=$isLogin, size=$((Get-Item $outPath).Length))"

$form.Close()
Write-Host "Done!"
