[System.Reflection.Assembly]::LoadFrom("E:\验证页面\src\GatewayDemo.Legacy.Web\bin\System.Data.SQLite.dll") | Out-Null
$dbPath = "E:\验证页面\src\GatewayDemo.Legacy.Web\App_Data\gateway-demo-legacy.db"
$conn = New-Object System.Data.SQLite.SQLiteConnection
$conn.ConnectionString = "Data Source=$dbPath;Version=3;Read Only=True;"
$conn.Open()

Write-Host "=== Devices (latest 5) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT DeviceId, DeviceCode, TrustState, ChallengeReason, UserAgent FROM GatewayManagedDevices ORDER BY LastSeenAtUtc DESC LIMIT 5"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $ts = $reader["TrustState"]
    $trustStr = if ($ts -eq 0) {"Trusted"} elseif ($ts -eq 1) {"Challenged"} else {"Unknown($ts)"}
    Write-Host "  Code=$($reader["DeviceCode"]) Trust=$trustStr UA=$($reader["UserAgent"])"
    Write-Host "    Id=$($reader["DeviceId"])"
    Write-Host "    Challenge=$($reader["ChallengeReason"])"
}
$reader.Close()

Write-Host "`n=== Authorizations ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT a.DeviceId, a.Status, a.CompanyName FROM GatewayDeviceAuthorizations a ORDER BY a.ApprovedAtUtc DESC LIMIT 5"
$reader2 = $cmd2.ExecuteReader()
while ($reader2.Read()) {
    $statusStr = if ($reader2["Status"] -eq 1) {"Approved"} else {"Status=$($reader2["Status"])"}
    Write-Host "  DeviceId=$($reader2["DeviceId"]) $statusStr Company=$($reader2["CompanyName"])"
}
$reader2.Close()

Write-Host "`n=== Auth Sites ==="
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT a.DeviceId, s.SiteKey FROM GatewayDeviceAuthorizations a JOIN GatewayDeviceAuthorizationSites s ON a.Id = s.AuthorizationId ORDER BY a.ApprovedAtUtc DESC LIMIT 10"
$reader3 = $cmd3.ExecuteReader()
while ($reader3.Read()) {
    Write-Host "  $($reader3["DeviceId"]) -> $($reader3["SiteKey"])"
}
$reader3.Close()

Write-Host "`n=== Requests (latest 5) ==="
$cmd4 = $conn.CreateCommand()
$cmd4.CommandText = "SELECT DeviceId, ApplicantName, Status, ProcessedAtUtc FROM GatewayAccessRequests ORDER BY CreatedAtUtc DESC LIMIT 5"
$reader4 = $cmd4.ExecuteReader()
while ($reader4.Read()) {
    $s = $reader4["Status"]
    $ss = if ($s -eq 0) {"Pending"} elseif ($s -eq 1) {"Approved"} elseif ($s -eq 2) {"Rejected"} else {"$s"}
    $p = if ($reader4["ProcessedAtUtc"] -eq [DBNull]::Value) {"UNPROCESSED"} else {"processed"}
    Write-Host "  $($reader4["DeviceId"]) Applicant=$($reader4["ApplicantName"]) Status=$ss $p"
}
$reader4.Close()

$conn.Close()
