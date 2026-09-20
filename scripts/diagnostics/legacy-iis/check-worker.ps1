# Check if worker process can actually handle requests
# First, check if the worker process is running and healthy
$w3wp = Get-Process w3wp -ErrorAction SilentlyContinue
if ($w3wp) {
    Write-Host "Worker processes found: $($w3wp.Count)"
    foreach ($p in $w3wp) {
        Write-Host "  PID: $($p.Id), Threads: $($p.Threads.Count), Handles: $($p.HandleCount), Memory: $([math]::Round($p.WorkingSet64 / 1MB, 1)) MB"
    }
} else {
    Write-Host "No worker processes"
}

# Try raw TCP connection to port 80
Write-Host "`n=== Raw TCP test to port 80 ==="
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect("127.0.0.1", 80)
    $stream = $tcp.GetStream()
    $stream.ReadTimeout = 5000
    $stream.WriteTimeout = 5000

    # Send HTTP request
    $request = "GET / HTTP/1.1`r`nHost: localhost`r`nConnection: close`r`n`r`n"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($request)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()

    # Read response
    Start-Sleep -Milliseconds 500
    $buffer = New-Object byte[] 4096
    if ($stream.DataAvailable) {
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $response = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read)
        Write-Host "Response ($read bytes):"
        Write-Host $response.Substring(0, [Math]::Min(500, $response.Length))
    } else {
        Write-Host "No data available after 500ms"
    }

    $tcp.Close()
} catch {
    Write-Host "TCP Error: $($_.Exception.Message)"
}
