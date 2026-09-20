$configPath = "$env:windir\system32\inetsrv\config\applicationHost.config"
$content = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8)

# Find and fix corrupted Chinese path
$wrongPatterns = @(
    '楠岃瘉椤甸潰',
    '婕旂ず绔欑偣',
    '鏃ユ瀯寤?0260330'
)

# Check if corruption exists
$needsFix = $false
foreach ($p in $wrongPatterns) {
    if ($content.Contains($p)) {
        $needsFix = $true
        break
    }
}

if (-not $needsFix) {
    Write-Host "No encoding corruption detected."
    exit 0
}

Write-Host "Encoding corruption detected. Rebuilding KpDemo site entry..."

# Load as XML, remove KpDemo site, re-add with correct path
[xml]$xml = [System.Xml.XmlDocument]::new()
$xml.Load($configPath)

# Remove KpDemo site
$sites = $xml.configuration.'system.applicationHost'.sites
$kpDemo = $sites.site | Where-Object { $_.name -eq 'KpDemo' }
if ($kpDemo) {
    $sites.RemoveChild($kpDemo)
    Write-Host "Removed corrupted KpDemo site entry"
}

# Save
$xml.Save($configPath)
Write-Host "Saved cleaned config"

# Now re-create using WebAdministration with correct path
Import-Module WebAdministration
New-Website -Name 'KpDemo' -PhysicalPath 'E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330' -Port 8080 -ApplicationPool 'KpDemoPool' -Force
Write-Host "Re-created KpDemo site with correct path"

# Verify
$site = Get-Website -Name 'KpDemo'
Write-Host "Site state: $($site.State)"
Write-Host "Physical path: $($site.PhysicalPath)"
