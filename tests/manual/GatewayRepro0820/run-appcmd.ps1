$out = Join-Path $PSScriptRoot 'appcmd-wp-elevated.txt'
& "$env:windir\system32\inetsrv\appcmd.exe" list wp *> $out
& "$env:windir\system32\inetsrv\appcmd.exe" list apppool *> (Join-Path $PSScriptRoot 'appcmd-pools-elevated.txt')
