$file = "C:\KpDemoWeb\web.config"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# Fix UrlRoutingModule-4.0 preCondition
$old = 'preCondition=""'
$new = 'preCondition="integratedMode"'
$content = $content.Replace(
    '<add name="UrlRoutingModule-4.0" type="System.Web.Routing.UrlRoutingModule" preCondition=""/>',
    '<add name="UrlRoutingModule-4.0" type="System.Web.Routing.UrlRoutingModule" preCondition="integratedMode"/>'
)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($file, $content, $utf8NoBom)
Write-Host "Fixed UrlRoutingModule preCondition"
