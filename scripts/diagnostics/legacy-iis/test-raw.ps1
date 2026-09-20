Get-Process w3wp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2

try {
    $c = New-Object System.Net.WebClient
    $result = $c.DownloadString("http://localhost:80/")
    Write-Host "OK - length:" $result.Length
    Write-Host $result.Substring(0, [Math]::Min(200, $result.Length))
} catch {
    Write-Host "Error:" $_.Exception.Message
    if ($_.Exception.InnerException) {
        Write-Host "Inner:" $_.Exception.InnerException.Message
    }
}
