$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"
try {
    $xml = [xml]([System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8))
    Write-Host "XML is valid. Root element: $($xml.DocumentElement.Name)"

    # Check modules section
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("iis", "http://schemas.microsoft.com/.NetConfiguration/v2.0")

    # Try to find the modules section in location/system.webServer
    $locations = $xml.SelectNodes("//location")
    Write-Host "Found $($locations.Count) location elements"

    foreach ($loc in $locations) {
        $sws = $loc.SelectSingleNode("system.webServer")
        if ($sws) {
            $mods = $sws.SelectSingleNode("modules")
            if ($mods) {
                Write-Host "Location '$($loc.path)' has modules section with $($mods.ChildNodes.Count) children"
                foreach ($child in $mods.ChildNodes) {
                    if ($child.Name -eq "add") {
                        Write-Host "  Module: $($child.name) preCondition=$($child.preCondition) type=$($child.type)"
                    } elseif ($child.NodeType -eq "Whitespace" -or $child.NodeType -eq "SignificantWhitespace") {
                        # Skip whitespace
                    } else {
                        Write-Host "  Other: $($child.Name) type=$($child.NodeType)"
                    }
                }
            }
        }
    }
} catch {
    Write-Host "XML ERROR: $($_.Exception.Message)"
    if ($_.Exception.InnerException) {
        Write-Host "Inner: $($_.Exception.InnerException.Message)"
    }
}
