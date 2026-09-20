$ErrorActionPreference = 'Stop'

# Use a dedicated user data dir to persist session
$userDataDir = "$env:TEMP\edge-gateway-screenshot"
$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'

# Step 1: Set cookie via a simple HTML page
$setCookieHtml = @"
<!DOCTYPE html>
<html><body>
<script>
document.cookie = "gw_device_credential=lIlzZEW_udvPkXPBNiTx_PYdnyQN9gprQxJm71ELcP8; path=/; SameSite=Lax";
setTimeout(function() { window.location.href = "http://localhost:5050/proxy/erp-main/default.aspx"; }, 500);
</script>
</body></html>
"@
$setCookieHtml | Out-File -FilePath "$userDataDir\setcookie.html" -Encoding utf8 -Force
if (-not (Test-Path $userDataDir)) { New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null }

# Step 2: Use Edge with user-data-dir to take screenshot
# First load the setcookie page, then take screenshot of the result

# Create a page that sets cookie and waits for redirect
$redirectHtml = @"
<!DOCTYPE html>
<html><body>
<p>Redirecting...</p>
<img src="http://localhost:5050/favicon.ico" onerror="this.remove()" onload="
document.cookie = 'gw_device_credential=lIlzZEW_udvPkXPBNiTx_PYdnyQN9gprQxJm71ELcP8; path=/; SameSite=Lax';
window.location.href = 'http://localhost:5050/proxy/erp-main/default.aspx';
" />
</body></html>
"@
$redirectHtml | Out-File -FilePath "$userDataDir\redirect.html" -Encoding utf8 -Force

Write-Host "HTML files created in $userDataDir"
Write-Host "Now run Edge manually or use the next step"
