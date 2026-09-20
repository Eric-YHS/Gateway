# Register ASP.NET managed module and global modules
$aspNetDll = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\webengine4.dll"
if (-not (Test-Path $aspNetDll)) {
    $aspNetDll = "$env:windir\Microsoft.NET\Framework\v4.0.30319\webengine4.dll"
}

# Add global module
$gm = Get-WebGlobalModule | Where-Object { $_.Name -eq 'ManagedEngine' }
if (-not $gm) {
    New-WebGlobalModule -Name 'ManagedEngine' -Image $aspNetDll -Precondition 'integratedMode,runtimeVersionv4.0,bitness64'
    Write-Host "Added global module ManagedEngine"
} else {
    Write-Host "Global module ManagedEngine already exists"
}

# Add module
$m = Get-WebModule | Where-Object { $_.Name -eq 'ManagedPipelineHandler' }
if (-not $m) {
    New-WebModule -Name 'ManagedPipelineHandler' -Type 'System.Web.Handlers.TransferRequestHandler' -Precondition 'integratedMode,runtimeVersionv4.0,bitness64'
    Write-Host "Added module ManagedPipelineHandler"
} else {
    Write-Host "Module ManagedPipelineHandler already exists"
}

# Verify
Write-Host ""
Write-Host "--- Global Modules containing Managed ---"
Get-WebGlobalModule | Where-Object { $_.Name -like '*Managed*' -or $_.Name -like '*Engine*' } | Format-Table Name,Image
Write-Host "--- Modules containing Managed ---"
Get-WebModule | Where-Object { $_.Name -like '*Managed*' -or $_.Name -like '*Pipeline*' } | Format-Table Name,Type
