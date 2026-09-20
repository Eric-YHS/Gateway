# Complete ASP.NET module registration fix
$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"

# Read as raw string
$content = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8)

# Remove any broken ManagedPipelineHandler entries in modules
$content = $content -replace '<add name="ManagedPipelineHandler"[^/]*/>', ''

# Also remove duplicate globalModules entries for ManagedEngine (keep only first)
$first = $true
$content = [regex]::Replace($content, '<add name="ManagedEngine"[^/]*/>', {
    param($m)
    if ($script:first) {
        $script:first = $false
        return $m.Value
    } else {
        return ''
    }
})

# Add correct ManagedPipelineHandler module entry right before </modules> closing tag
$content = $content.Replace('</modules>', '<add name="ManagedPipelineHandler" preCondition="integratedMode,runtimeVersionv4.0" />' + "`n    </modules>")

# Ensure the webengine4.dll global module entry exists
if (-not ($content -match 'name="ManagedEngine"')) {
    $gmInsert = '<add name="ManagedEngine" image="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\webengine4.dll" preCondition="integratedMode,runtimeVersionv4.0,bitness64" />'
    $content = $content.Replace('</globalModules>', "$gmInsert`n    </globalModules>")
}

# Save
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, $content, $utf8NoBom)
Write-Host "Config updated."

# Verify modules section
$verify = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8)
if ($verify -match 'name="ManagedPipelineHandler"') {
    Write-Host "ManagedPipelineHandler: OK"
} else {
    Write-Host "ManagedPipelineHandler: MISSING"
}
if ($verify -match 'name="ManagedEngine"') {
    Write-Host "ManagedEngine: OK"
} else {
    Write-Host "ManagedEngine: MISSING"
}
