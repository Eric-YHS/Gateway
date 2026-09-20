$url = "http://localhost:8080/default.aspx"
try {
    $wc = New-Object System.Net.WebClient
    $result = $wc.DownloadString($url)
    Write-Host "OK - length: $($result.Length)"
} catch {
    $wr = $_.Exception.InnerException.Response
    if ($wr) {
        $stream = $wr.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
        $body = $reader.ReadToEnd()
        $reader.Close()
        $outFile = "C:\last-error.html"
        [System.IO.File]::WriteAllText($outFile, $body, [System.Text.Encoding]::UTF8)
        Write-Host "Error saved to C:\last-error.html"
        Write-Host "Status: $($wr.StatusCode)"
    } else {
        Write-Host "No response: $_"
    }
}
