# Read the earliest backup and check ManagedPipelineHandler entry
$backupPath = "C:\inetpub\history\CFGHISTORY_0000000009\applicationHost.config"
if (Test-Path $backupPath) {
    $content = Get-Content $backupPath -Raw
    $matches = [regex]::Matches($content, '.*ManagedPipelineHandler.*')
    Write-Host "=== Earliest backup (CFGHISTORY_0000000009) ==="
    foreach ($m in $matches) {
        Write-Host $m.Value.Trim()
    }
} else {
    Write-Host "Backup not found at $backupPath"
    # Try alternate location
    $alt = Get-ChildItem "C:\inetpub\history" -Directory | Sort-Object LastWriteTime | Select-Object -First 1
    if ($alt) {
        $cfg = Join-Path $alt.FullName "applicationHost.config"
        if (Test-Path $cfg) {
            Write-Host "Using $cfg instead"
            $content = Get-Content $cfg -Raw
            $matches = [regex]::Matches($content, '.*ManagedPipelineHandler.*')
            foreach ($m in $matches) {
                Write-Host $m.Value.Trim()
            }
        }
    }
}

# Also check the latest backup
Write-Host "`n=== Latest backup ==="
$latestBackup = Get-ChildItem "C:\inetpub\history" -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestBackup) {
    $cfg = Join-Path $latestBackup.FullName "applicationHost.config"
    if (Test-Path $cfg) {
        $content = Get-Content $cfg -Raw
        $matches = [regex]::Matches($content, '.*ManagedPipelineHandler.*')
        foreach ($m in $matches) {
            Write-Host $m.Value.Trim()
        }
    }
}
