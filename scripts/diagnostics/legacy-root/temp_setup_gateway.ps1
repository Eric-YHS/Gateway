$ErrorActionPreference = "Stop"
$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"

# Create dedicated app pools for gateway and backend
& $appcmd delete apppool /apppool.name:"GatewayDemoLegacyPool" 2>$null | Out-Null
& $appcmd delete apppool /apppool.name:"MockBackendLegacyPool" 2>$null | Out-Null

& $appcmd add apppool /name:"GatewayDemoLegacyPool" /managedRuntimeVersion:"v4.0" /managedPipelineMode:"Integrated"
& $appcmd add apppool /name:"MockBackendLegacyPool" /managedRuntimeVersion:"v4.0" /managedPipelineMode:"Integrated"

# Assign app pools to sites
& $appcmd set app /app.name:"GatewayDemoLegacy/" /applicationPool:"GatewayDemoLegacyPool"
& $appcmd set app /app.name:"MockBusinessBackendLegacy/" /applicationPool:"MockBackendLegacyPool"

Write-Host "App pools configured."

# Set gateway profile to local-demo
$scriptPath = "E:\验证页面\scripts\legacy\set-gateway-profile.ps1"
& powershell -ExecutionPolicy Bypass -File $scriptPath -Profile local-demo -DemoBaseUrl "http://localhost:8080/"

Write-Host "`nChecking site bindings..."
& $appcmd list site "GatewayDemoLegacy"
& $appcmd list site "MockBusinessBackendLegacy"

Write-Host "`nChecking app pools..."
& $appcmd list apppool "GatewayDemoLegacyPool"
& $appcmd list apppool "MockBackendLegacyPool"
