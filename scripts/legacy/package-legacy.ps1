param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$outputRoot = Join-Path $root "deliverables\GatewayDemo.Legacy-net472"
$zipPath = Join-Path $root "deliverables\GatewayDemo.Legacy-net472.zip"
$stagingRoot = Join-Path $root ("deliverables\_staging\" + [Guid]::NewGuid().ToString('N'))

& (Join-Path $root "scripts\legacy\build-legacy.ps1") -Configuration $Configuration

if (Test-Path $stagingRoot) {
    Remove-Item -Recurse -Force $stagingRoot
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$pathsToCopy = @(
    "GatewayDemo.Legacy.sln",
    "src\GatewayDemo.Legacy.Core",
    "src\GatewayDemo.Legacy.Web",
    "src\MockBusinessBackend.Legacy.Web",
    "packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0",
    "scripts\legacy"
)

foreach ($relativePath in $pathsToCopy) {
    $source = Join-Path $root $relativePath
    if (-not (Test-Path $source)) {
        continue
    }

    $destination = Join-Path $stagingRoot $relativePath
    $destinationParent = Split-Path -Parent $destination
    if (-not (Test-Path $destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }

    Copy-Item $source $destination -Recurse -Force
}

$excludeNames = @("obj", ".vs")
Get-ChildItem -Path $stagingRoot -Recurse -Directory | Where-Object {
    $excludeNames -contains $_.Name
} | Sort-Object FullName -Descending | Remove-Item -Recurse -Force

Get-ChildItem -Path $stagingRoot -Recurse -Directory | Where-Object {
    $_.Name -eq "Debug" -and $_.Parent -and $_.Parent.Name -eq "bin"
} | Sort-Object FullName -Descending | Remove-Item -Recurse -Force

Get-ChildItem -Path $stagingRoot -Recurse -File -Filter "*.pdb" | Remove-Item -Force

# Remove runtime state files that should not be delivered
$runtimeStateFiles = @(
    "gateway-sites.profile.txt",
    "app-diagnostics.log",
    "gateway-demo-legacy.db",
    "gateway-demo-legacy.db-shm",
    "gateway-demo-legacy.db-wal"
)
foreach ($stateFile in $runtimeStateFiles) {
    Get-ChildItem -Path $stagingRoot -Recurse -File | Where-Object {
        $_.Name -eq $stateFile
    } | Remove-Item -Force
}

# Keep the delivery package focused on the deployable gateway. Local demo,
# packaging, prerequisite and container helpers are useful in the repository,
# but they carry machine-specific defaults that should not be part of the handoff.
$deliveryScriptsDir = Join-Path $stagingRoot "scripts\legacy"
if (Test-Path $deliveryScriptsDir) {
    Get-ChildItem -Path $deliveryScriptsDir -File | Where-Object {
        $_.Name -notin @("setup-demo-sites.ps1", "bind-gateway-https.ps1")
    } | Remove-Item -Force
}

Get-ChildItem -Path $stagingRoot -Recurse -File -Filter "Dockerfile" | Remove-Item -Force
Get-ChildItem -Path $stagingRoot -Recurse -File -Filter ".dockerignore" -Force | Remove-Item -Force

# The gateway web bin already carries the Core assembly; the Core project's own
# bin output is redundant in the handoff.
$coreBinDir = Join-Path $stagingRoot "src\GatewayDemo.Legacy.Core\bin"
if (Test-Path $coreBinDir) {
    Remove-Item -Recurse -Force $coreBinDir
}

$deliveryAppDataDir = Join-Path $stagingRoot "src\GatewayDemo.Legacy.Web\App_Data"
if (Test-Path $deliveryAppDataDir) {
    Get-ChildItem -Path $deliveryAppDataDir -File -Filter "gateway-sites.*.json" | Remove-Item -Force
}

$packageReadmeSource = Join-Path $root "delivery\legacy\Package.README.md"
if (Test-Path $packageReadmeSource) {
    Copy-Item $packageReadmeSource (Join-Path $stagingRoot "README.md") -Force
}

Get-ChildItem -Path $stagingRoot -Recurse -File -Include *.md,*.txt,*.rst | Where-Object {
    $_.FullName -ne (Join-Path $stagingRoot "README.md")
} | Remove-Item -Force

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $zipPath

try {
    if (Test-Path $outputRoot) {
        Remove-Item -Recurse -Force $outputRoot
    }

    Get-ChildItem -Path $stagingRoot | Copy-Item -Destination $outputRoot -Recurse -Force
}
catch {
    Write-Warning ("Failed to refresh delivery folder: {0}" -f $_.Exception.Message)
    Write-Warning "Zip package was created successfully and can be delivered directly."
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -Recurse -Force $stagingRoot
    }
}

Write-Host ("Delivery folder: {0}" -f $outputRoot)
Write-Host ("Delivery zip:    {0}" -f $zipPath)
