$ErrorActionPreference = "Stop"
$gwSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Get device cookie
$gwPage = Invoke-WebRequest -Uri 'http://localhost:5050/proxy/erp-main/default.aspx' -Method GET -WebSession $gwSession -UseBasicParsing -TimeoutSec 10

$titleMatch = [regex]::Match($gwPage.Content, '<title>(.*?)</title>')
if ($titleMatch.Success) {
    Write-Host "Page title: $($titleMatch.Groups[1].Value)"
}

# Check for key elements that prove it's the demo login page
if ($gwPage.Content -match 'txtLoginName') {
    Write-Host "Found: Login name input field"
}
if ($gwPage.Content -match 'btnLogin') {
    Write-Host "Found: Login button"
}
if ($gwPage.Content -match 'ValidateCode') {
    Write-Host "Found: Validate code control"
}
if ($gwPage.Content -match 'ddlDataCenter') {
    Write-Host "Found: Data center dropdown"
}
if ($gwPage.Content -match 'KPMIIS') {
    Write-Host "Found: KPMIIS namespace"
}

Write-Host "Content length: $($gwPage.Content.Length)"
Write-Host "Status: $($gwPage.StatusCode)"
