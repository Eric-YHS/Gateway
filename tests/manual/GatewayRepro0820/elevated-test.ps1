$log = Join-Path $PSScriptRoot 'elevated-test.log'
[Environment]::UserName | Out-File -LiteralPath $log -Encoding utf8
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) | Add-Content -LiteralPath $log
