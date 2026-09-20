param(
    [string]$GatewaySitePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
if ([string]::IsNullOrWhiteSpace($GatewaySitePath)) {
    $GatewaySitePath = Join-Path $root "src\GatewayDemo.Legacy.Web"
}

$dbFiles = @(
    (Join-Path $GatewaySitePath "App_Data\gateway-demo-legacy.db")
    (Join-Path $GatewaySitePath "App_Data\gateway-demo-legacy.db-shm")
    (Join-Path $GatewaySitePath "App_Data\gateway-demo-legacy.db-wal")
)

foreach ($file in $dbFiles) {
    if (Test-Path $file) {
        Remove-Item -Force $file
        Write-Host "Removed: $file"
    }
}
