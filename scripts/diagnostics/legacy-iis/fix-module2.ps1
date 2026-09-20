Add-WebConfigurationProperty -Filter system.webServer/modules -Name . -Value @{name='ManagedPipelineHandler';type='System.Web.Handlers.TransferRequestHandler';preCondition='integratedMode,runtimeVersionv4.0'}
Write-Host "Module registered. Verifying:"
Get-WebConfigurationProperty -Filter system.webServer/modules -Name . | Where-Object { $_.name -like '*Managed*' } | Select-Object name,type
