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

# Step 1: Navigate to proxy
Write-Host "Step 1: Visit proxy..."
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 10000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser.ReadyState -eq 'Complete') { break }
}
Write-Host "Page: $($browser.DocumentTitle), ready=$($browser.ReadyState)"

# Step 2: Submit access request
Write-Host "Step 2: Submit request..."
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
Write-Host "Step 3: Approve..."
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$allIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $allIds) {
    Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "Approved: $($allIds.Count)"

# Step 4: Navigate to proxy
Write-Host "Step 4: Revisit proxy..."
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
$sw.Restart()
while ($sw.ElapsedMilliseconds -lt 15000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser.DocumentText -match 'txtLoginName') {
        Write-Host "Found ERP login! ($($sw.ElapsedMilliseconds)ms)"
        Start-Sleep -Seconds 3
        [System.Windows.Forms.Application]::DoEvents()
        break
    }
    if ($browser.ReadyState -eq 'Complete') { break }
}

$isLogin = $false
if ($browser.DocumentText) { $isLogin = $browser.DocumentText -match 'txtLoginName' }
Write-Host "Has ERP login: $isLogin"

# Take screenshot - save to ASCII path first, then copy
$tempPath = 'C:\temp\erp-screenshot.png'
if (-not (Test-Path 'C:\temp')) { New-Item -ItemType Directory -Path 'C:\temp' -Force | Out-Null }

$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$bitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
[GC]::Collect()

Write-Host "Saved to temp: $(Test-Path $tempPath)"
if (Test-Path $tempPath) {
    $fi = Get-Item $tempPath
    Write-Host "Temp file size: $($fi.Length) bytes"
}

# Copy to final location
$finalPath = Join-Path $screenshotDir '05-erp-login-proxied.png'
Copy-Item -Path $tempPath -Destination $finalPath -Force
Write-Host "Copied to: $finalPath exists=$(Test-Path $finalPath)"

# Also capture admin dashboard
Write-Host "Step 5: Admin dashboard..."
# Set admin session cookies via InternetSetCookie
$code = '
[DllImport("wininet.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern bool InternetSetCookie(string lpszUrlName, string lpszCookieName, string lpszCookieData);
'
$wininet = Add-Type -MemberDefinition $code -Name WininetLast -Namespace Win32Last -PassThru

$adminCookies = $admin.Cookies.GetCookies('http://localhost:5051')
foreach ($c in $adminCookies) {
    $wininet::InternetSetCookie('http://localhost:5051', $null, "$($c.Name)=$($c.Value); path=/") | Out-Null
}

$form.Controls.Remove($browser)
$browser2 = New-Object System.Windows.Forms.WebBrowser
$browser2.Size = New-Object System.Drawing.Size(1440, 900)
$browser2.ScriptErrorsSuppressed = $true
$form.Controls.Add($browser2)

$browser2.Navigate('http://localhost:5051/Admin/Default.aspx')
$sw.Restart()
while ($sw.ElapsedMilliseconds -lt 10000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser2.ReadyState -eq 'Complete') { break }
}
Start-Sleep -Seconds 2
Write-Host "Admin: $($browser2.DocumentTitle), size=$($browser2.DocumentText.Length)"

$bitmap2 = New-Object System.Drawing.Bitmap(1440, 900)
$browser2.DrawToBitmap($bitmap2, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$tempPath2 = 'C:\temp\admin-dashboard.png'
$bitmap2.Save($tempPath2, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap2.Dispose()
[GC]::Collect()

$finalPath2 = Join-Path $screenshotDir '06-admin-dashboard.png'
Copy-Item -Path $tempPath2 -Destination $finalPath2 -Force
Write-Host "Admin dashboard: $finalPath2 exists=$(Test-Path $finalPath2)"

$form.Close()
Write-Host "Done!"
