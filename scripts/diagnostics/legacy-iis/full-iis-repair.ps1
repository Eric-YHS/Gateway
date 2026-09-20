# Full IIS repair via DISM
Write-Host "=== Re-enabling IIS features ==="
dism /online /enable-feature /featurename:IIS-WebServerRole /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-WebServer /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-CommonHttpFeatures /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-StaticContent /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-DefaultDocument /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-DirectoryBrowsing /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-HttpErrors /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-ApplicationDevelopment /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-ASPNET45 /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-ISAPIExtensions /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-ISAPIFilter /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-NetFxExtensibility45 /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-Security /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-RequestFiltering /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-Performance /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-HttpCompressionStatic /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-WebServerManagementTools /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:IIS-ManagementConsole /all 2>&1 | Select-Object -Last 3
dism /online /enable-feature /featurename:WAS-NetFxEnvironment /all 2>&1 | Select-Object -Last 3

Write-Host "`n=== Restarting IIS ==="
iisreset

Write-Host "`n=== Testing ==="
try {
    $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:80/")
    $req.Timeout = 5000
    $resp = $req.GetResponse()
    Write-Host "Default site status:" $resp.StatusCode
    $resp.Close()
} catch {
    Write-Host "Default site error:" $_.Exception.Message
}
