$httpErr = "C:\Windows\System32\LogFiles\HTTPERR"
$files = Get-ChildItem $httpErr -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($files) {
    $latest = $files[0]
    Write-Host "Latest HTTP err log: $($latest.Name)"
    Get-Content $latest.FullName -Tail 5
} else {
    Write-Host "No HTTP error logs found"
}

# Check IIS worker process failures in event log
Write-Host "`n--- WAS / W3SVC Events ---"
Get-WinEvent -LogName System -MaxEvents 50 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match 'WAS|W3SVC|IIS' } |
    Select-Object -First 5 |
    Format-List TimeCreated, ProviderName, Id, Message
