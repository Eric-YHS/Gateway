# Backup current config
$date = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = "C:\inetpub\history\ManualBackup_$date"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item "$env:windir\system32\inetsrv\config\applicationHost.config" "$backupDir\applicationHost.config" -Force
Write-Host "Current config backed up to $backupDir"

# Reset IIS to default configuration
# This creates a fresh applicationHost.config
$defaultConfig = "$env:windir\system32\inetsrv\config\applicationHost.config"

# Stop IIS completely
Stop-Service W3SVC -Force
Stop-Service WAS -Force
Start-Sleep -Seconds 2

# Use appcmd to backup and restore default
& "$env:windir\system32\inetsrv\appcmd.exe" add backup "BeforeReset_$date"

# Check if there's a way to regenerate the default config
# The cleanest way is to uninstall and reinstall IIS
# But first, let's try replacing the config with a minimal working one

Write-Host "Services stopped. Checking config state..."
Get-Service W3SVC, WAS | Select-Object Name, Status
