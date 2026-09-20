# Step 1: Stop all IIS services
Stop-Service W3SVC -Force -ErrorAction SilentlyContinue
Stop-Service WAS -Force -ErrorAction SilentlyContinue
Stop-Service WMSVC -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Step 2: Backup current config
$configDir = "$env:windir\system32\inetsrv\config"
$date = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = "E:\验证页面\config-backup-$date"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item "$configDir\applicationHost.config" "$backupDir\" -Force
Copy-Item "$configDir\administration.config" "$backupDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "$configDir\redirection.config" "$backupDir\" -Force -ErrorAction SilentlyContinue
Write-Host "Config backed up to $backupDir"

# Step 3: Rename current applicationHost.config
Rename-Item "$configDir\applicationHost.config" "applicationHost.config.old" -Force
Write-Host "Renamed applicationHost.config to .old"

# Step 4: Remove IIS feature and re-enable to regenerate config
# This is the only reliable way to get a clean config
dism /online /disable-feature /featurename:IIS-WebServer /NoRestart 2>&1 | Select-Object -Last 5
dism /online /enable-feature /featurename:IIS-WebServer /all /NoRestart 2>&1 | Select-Object -Last 5
dism /online /enable-feature /featurename:IIS-ASPNET45 /all /NoRestart 2>&1 | Select-Object -Last 5

# Step 5: Check if new config was created
if (Test-Path "$configDir\applicationHost.config") {
    $size = (Get-Item "$configDir\applicationHost.config").Length
    Write-Host "New applicationHost.config created ($size bytes)"
} else {
    Write-Host "ERROR: No new applicationHost.config created! Restoring backup..."
    Copy-Item "$backupDir\applicationHost.config" $configDir -Force
}

# Step 6: Start services
Start-Service WAS -ErrorAction SilentlyContinue
Start-Service W3SVC -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Step 7: Test
Write-Host "`nTesting default page..."
try {
    $r = Invoke-WebRequest -Uri "http://localhost:80/" -UseBasicParsing -TimeoutSec 5
    Write-Host "Status: $($r.StatusCode) - IIS default page works!"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
