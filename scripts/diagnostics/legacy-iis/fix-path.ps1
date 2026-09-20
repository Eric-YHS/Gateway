$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"

# Read as raw bytes to preserve encoding
$bytes = [System.IO.File]::ReadAllBytes($configPath)
$content = [System.Text.Encoding]::UTF8.GetString($bytes)

# Fix the corrupted path - replace broken pattern with correct one
$fixed = $content -replace '日构[^"]*?0260330', '日构建20260330'

# Write back as UTF-8 without BOM
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, $fixed, $utf8NoBom)

Write-Host "Path fixed. Verifying..."

# Verify
$content2 = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8)
if ($content2 -match 'physicalPath="E:\\验证页面\\演示站点20260330\\演示站点20260330\\web\\701日构建20260330"') {
    Write-Host "Path is now correct!"
} else {
    Write-Host "WARNING: Path may still be wrong. Checking..."
    $match = [regex]::Match($content2, 'physicalPath="E:[^"]*701[^"]*"')
    Write-Host "Found: $($match.Value)"
}
