$ErrorActionPreference = "Stop"

# Step 1: Login
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$body = @{action='login';username='gateway-admin';password='GatewayDemo!2026'}
$resp = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $body -WebSession $session -UseBasicParsing -TimeoutSec 10
Write-Host "Login status: $($resp.StatusCode)"
Write-Host "Cookies: $($session.Cookies.Count)"

# Step 2: Check for pending devices - try to approve via the gateway
# First, let's see what the admin dashboard shows
$content = $resp.Content
# Look for device IDs in the response
if ($content -match 'DEV-\d+-[A-F0-9]+') {
    $deviceId = $Matches[0]
    Write-Host "Found device: $deviceId"
}

# Step 3: Try to approve the device
# The gateway might have an approve endpoint
$approveBody = @{action='approve';deviceId='DEV-260422-3FC347'}
try {
    $approveResp = Invoke-WebRequest -Uri 'http://localhost:5051/Admin/Default.aspx' -Method POST -Body $approveBody -WebSession $session -UseBasicParsing -TimeoutSec 10
    Write-Host "Approve status: $($approveResp.StatusCode)"
    Write-Host "Response: $($approveResp.Content.Substring(0, [Math]::Min(500, $approveResp.Content.Length)))"
} catch {
    Write-Host "Approve error: $($_.Exception.Message)"
}

# Step 4: Check device list in DB
$dbPath = "E:\验证页面\src\GatewayDemo.Legacy.Web\App_Data\gateway-demo-legacy.db"
if (Test-Path $dbPath) {
    Write-Host "`nDatabase file exists at: $dbPath"
    Write-Host "Size: $((Get-Item $dbPath).Length) bytes"
}
