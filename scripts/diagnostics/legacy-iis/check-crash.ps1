# Get WAS and application crash events
Write-Host "=== WAS Events (last hour) ==="
Get-WinEvent -LogName System -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -eq 'Microsoft-Windows-WAS' -and $_.TimeCreated -gt (Get-Date).AddHours(-2) } |
    Select-Object -First 10 |
    Format-List TimeCreated, Id, LevelDisplayName, Message

Write-Host "=== Application Error Events ==="
Get-WinEvent -LogName Application -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -eq 'Application Error' -and $_.TimeCreated -gt (Get-Date).AddHours(-2) } |
    Select-Object -First 5 |
    Format-List TimeCreated, Id, Message

Write-Host "=== .NET Runtime Events ==="
Get-WinEvent -LogName Application -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match '\.NET Runtime' -and $_.TimeCreated -gt (Get-Date).AddHours(-2) } |
    Select-Object -First 5 |
    Format-List TimeCreated, Id, Message
