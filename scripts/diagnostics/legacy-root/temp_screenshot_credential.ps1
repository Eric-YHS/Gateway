$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$code = '
[DllImport("wininet.dll", CharSet=CharSet.Auto, SetLastError=true)]
public static extern bool InternetSetCookie(string lpszUrlName, string lpszCookieName, string lpszCookieData);
'
$wininet = Add-Type -MemberDefinition $code -Name WininetCred -Namespace Win32Cred -PassThru

$screenshotDir = 'E:\验证页面\artifacts\gateway-screenshots'

# Step 1: Login to admin and set cookie
$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginBody = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$loginR = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $loginBody -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host "Admin login: $($loginR.StatusCode)"

# Set admin cookies via InternetSetCookie
$adminCookies = $admin.Cookies.GetCookies('http://localhost:5051')
foreach ($c in $adminCookies) {
    $wininet::InternetSetCookie('http://localhost:5051', $null, "$($c.Name)=$($c.Value); path=/") | Out-Null
}

# Step 2: Issue APP credential to get the credential display page
$dash = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -WebSession $admin -UseBasicParsing -TimeoutSec 10
$authPattern = 'name="authorizationId"\s+value="([0-9a-f-]+)"'
$authMatches = [regex]::Matches($dash.Content, $authPattern)
$authId = $authMatches[$authMatches.Count - 1].Groups[1].Value
Write-Host "Auth ID: $authId"

# Issue credential
$issueResult = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body @{action='issue-app-credential';authorizationId=$authId} -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host "Issue page: $($issueResult.Content.Length) bytes"
$hasAppKey = $issueResult.Content -match 'app_'
Write-Host "Has AppKey: $hasAppKey"

# Save the issue page HTML for debugging
$issueResult.Content | Out-File "$env:TEMP\credential-page.html" -Encoding utf8

# Step 3: Take screenshot of the admin dashboard (showing issued credential)
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

# Navigate to admin dashboard
$browser.Navigate('http://localhost:5051/Admin/Default.aspx')
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 15000) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 100
    if ($browser.ReadyState -eq 'Complete') { break }
}
Start-Sleep -Seconds 3
Write-Host "Admin page: $($browser.DocumentTitle), size=$($browser.DocumentText.Length)"

$hasCred = $browser.DocumentText -match 'APP' -or $browser.DocumentText -match '凭据'
Write-Host "Has credential info: $hasCred"

$bitmap = New-Object System.Drawing.Bitmap(1440, 900)
$browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
$tempPath = 'C:\temp\admin-credential.png'
$bitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

$finalPath = Join-Path $screenshotDir '07-admin-app-credential.png'
Copy-Item -Path $tempPath -Destination $finalPath -Force
Write-Host "Screenshot saved: $finalPath ($((Get-Item $finalPath).Length) bytes)"

$form.Close()
Write-Host "Done!"
