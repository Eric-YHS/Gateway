try {
    $wc = New-Object System.Net.WebClient
    $result = $wc.DownloadString("http://localhost:8080/default.aspx")
    if ($result -match "登录|Login|用户名|密码|txtUserName|txtPassword") {
        Write-Host "Login page found - OK (length: $($result.Length))"
    } else {
        Write-Host "Page loaded (length: $($result.Length)) but no login form detected"
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
