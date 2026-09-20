# Remove the wrong module entry
Remove-WebConfigurationProperty -Filter system.webServer/modules -Name . -At @{name='ManagedPipelineHandler'} -ErrorAction SilentlyContinue
# Re-add without type - it's loaded from global module ManagedEngine
Add-WebConfigurationProperty -Filter system.webServer/modules -Name . -Value @{name='ManagedPipelineHandler';preCondition='integratedMode,runtimeVersionv4.0'}
Write-Host "Fixed ManagedPipelineHandler - no custom type, loaded from global module"
# Verify
Get-WebConfigurationProperty -Filter system.webServer/modules -Name . | Where-Object { $_.name -eq 'ManagedPipelineHandler' } | Format-List name,type,preCondition
