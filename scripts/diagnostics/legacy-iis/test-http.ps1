try {
    $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:80/")
    $req.Timeout = 5000
    $resp = $req.GetResponse()
    Write-Host "Status:" $resp.StatusCode
    $resp.Close()
} catch {
    Write-Host "Error:" $_.Exception.Message
    if ($_.Exception.InnerException) {
        Write-Host "Inner:" $_.Exception.InnerException.Message
    }
}
