$file = "C:\KpDemoWeb\web.config"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
$content = $content.Replace(
    '<add name="UrlRoutingModule-4.0" type="System.Web.Routing.UrlRoutingModule" preCondition="integratedMode"/>',
    '<add name="UrlRoutingModule-4.0" type="System.Web.Routing.UrlRoutingModule" preCondition=""/>'
)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($file, $content, $utf8NoBom)
Write-Host "Reverted"
