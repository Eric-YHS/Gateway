$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"
[xml]$xml = Get-Content $configPath

# Find and fix ManagedPipelineHandler in modules section
$modulesNode = $xml.configuration.configSections.sectionGroup.sectionGroup | Where-Object { $_.name -eq 'system.webServer' }

# Navigate to system.webServer/modules
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('iis', 'urn:microsoft-cml')

# Find all module entries with name ManagedPipelineHandler
$modules = $xml.SelectNodes("//modules/add[@name='ManagedPipelineHandler']")
foreach ($m in $modules) {
    # Remove the type attribute - module is loaded from global module
    $m.RemoveAttribute('type')
    Write-Host "Removed type attribute from ManagedPipelineHandler"
}

# Also check globalModules
$globalModules = $xml.SelectNodes("//globalModules/add[@name='ManagedEngine']")
if ($globalModules.Count -eq 0) {
    Write-Host "WARNING: ManagedEngine not found in globalModules"
} else {
    Write-Host "ManagedEngine found in globalModules - OK"
}

$xml.Save($configPath)
Write-Host "Config saved."
