$key = (Get-Content "$env:USERPROFILE\.codex\auth.json" -Raw | ConvertFrom-Json).OPENAI_API_KEY
foreach ($effort in @('low','medium','high','xhigh')) {
  $body = (@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true; reasoning=@{ effort=$effort } } | ConvertTo-Json -Depth 5 -Compress)
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = (& curl.exe -sS -N -m 120 -H "Authorization: Bearer $key" -H "Content-Type: application/json" -d $body https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
  $sw.Stop()
  $completed = ($out -match 'event: response.completed')
  $has400 = ($out -match 'E48002|400|invalid')
  $lastEvent = ([regex]::Matches($out, '(?m)^event: (\S+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Last 1)
  Write-Output ("effort={0} elapsed={1}s completed={2} last_event={3} body_err={4}" -f $effort, [int]$sw.Elapsed.TotalSeconds, $completed, $lastEvent, $has400)
}
Write-Output 'done'
