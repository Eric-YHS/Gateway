$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"
$bytes = [System.IO.File]::ReadAllBytes($configPath)

# Check BOM
$bom = ""
if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    $bom = "UTF-8 BOM"
} elseif ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
    $bom = "UTF-16 LE BOM"
} elseif ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
    $bom = "UTF-16 BE BOM"
} else {
    $bom = "No BOM (plain ASCII/UTF-8)"
}

Write-Host "File size: $($bytes.Length) bytes"
Write-Host "BOM: $bom"
Write-Host "First 10 bytes: $($bytes[0..9] -join ', ')"

# Check for any null bytes (would indicate UTF-16)
$nullCount = ($bytes | Where-Object { [byte]0 -eq [byte]0 }).Count
Write-Host "Null bytes: $nullCount"

# Look for any non-ASCII bytes in first 100 bytes (except BOM)
$nonAscii = $bytes[3..99] | Where-Object { [int]$_ -gt 127 }
if ($nonAscii) {
    Write-Host "Non-ASCII bytes found in first 100: $($nonAscii -join ', ')"
} else {
    Write-Host "First 100 bytes are pure ASCII"
}

# Check around the modules section for any encoding issues
$content = [System.Text.Encoding]::UTF8.GetString($bytes)
$modulesIdx = $content.IndexOf("<modules>")
if ($modulesIdx -gt 0) {
    Write-Host "`nModules section starts at byte offset: $modulesIdx"
    $before = $content.Substring($modulesIdx - 20, 20)
    $bytesBefore = $bytes[($modulesIdx - 20)..($modulesIdx - 1)]
    Write-Host "Bytes before <modules>: $($bytesBefore -join ', ')"
}
