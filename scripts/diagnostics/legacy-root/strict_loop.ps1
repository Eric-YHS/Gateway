$tHome = 'C:\Users\Eric\codex-home-test'
[System.IO.File]::Copy('C:\Users\Eric\.codex\config.toml', (Join-Path $tHome 'config.toml'), $true)
[System.IO.File]::Copy('C:\Users\Eric\.codex\auth.json', (Join-Path $tHome 'auth.json'), $true)
$env:CODEX_HOME = $tHome
$bin = 'C:\Users\Eric\AppData\Roaming\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
$cfg = Join-Path $tHome 'config.toml'
$enc = New-Object System.Text.UTF8Encoding($false)
$removed = @()
for ($i = 0; $i -lt 25; $i++) {
  $out = (& $bin --strict-config exec -s read-only -C 'C:\Users\Eric' --ephemeral --skip-git-repo-check 'Reply with exactly the word: ok' 2>&1 | Out-String)
  if ($out -match 'unknown configuration field `([^`]+)`') {
    $field = $Matches[1]
    $leaf = ($field -split '\.')[-1]
    $removed += $field
    Write-Output "unknown_field: $field"
    $text = [System.IO.File]::ReadAllText($cfg, $enc)
    $lines = $text -split "`r?`n"
    $newLines = $lines | Where-Object { $_ -notmatch ('^\s*' + [regex]::Escape($leaf) + '\s*=') }
    [System.IO.File]::WriteAllText($cfg, ($newLines -join "`r`n"), $enc)
    continue
  }
  Write-Output '=== FINAL ==='
  Write-Output $out.Substring(0, [Math]::Min(700, $out.Length))
  break
}
Write-Output ('removed_fields: ' + ($removed -join ', '))
