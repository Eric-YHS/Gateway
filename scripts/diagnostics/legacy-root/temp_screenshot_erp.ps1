$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create a hidden form with WebBrowser
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

# First navigate to set cookie
$browser.Navigate('http://localhost:5050/gateway')
while ($browser.ReadyState -ne 'Complete') { Start-Sleep -Milliseconds 200 }
Write-Host ('Initial load: ' + $browser.DocumentTitle)

# Set cookie via document
$browser.Document.Cookie = 'gw_device_credential=lIlzZEW_udvPkXPBNiTx_PYdnyQN9gprQxJm71ELcP8'

# Navigate to proxy page
$browser.Navigate('http://localhost:5050/proxy/erp-main/default.aspx')
while ($browser.ReadyState -ne 'Complete') { Start-Sleep -Milliseconds 200 }
Start-Sleep -Seconds 2
Write-Host ('Proxy page: ' + $browser.DocumentTitle + ' size: ' + $browser.DocumentText.Length)

# Check if it's the ERP login page
$isLogin = $browser.DocumentText -match 'txtLoginName'
Write-Host ('Is ERP login: ' + $isLogin)

if ($isLogin) {
    # Take screenshot
    $bitmap = New-Object System.Drawing.Bitmap(1440, 900)
    $browser.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, 1440, 900)))
    $bitmap.Save('E:\验证页面\artifacts\gateway-screenshots\06-erp-login-proxied.png')
    $bitmap.Dispose()
    Write-Host 'Screenshot saved: 06-erp-login-proxied.png'
} else {
    # Still showing gateway, try alternative approach
    Write-Host 'Still showing gateway page, content preview:'
    $preview = $browser.DocumentText.Substring(0, [Math]::Min(500, $browser.DocumentText.Length))
    Write-Host $preview
}

$form.Close()
