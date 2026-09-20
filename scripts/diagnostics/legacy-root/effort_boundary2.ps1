$key = (Get-Content 'C:\Users\Eric\.codex\auth.json' -Raw | ConvertFrom-Json).OPENAI_API_KEY
$sentence = 'The quick brown fox jumps over the lazy dog near the river bank. '
$big = ''
for ($i = 0; $i -lt 3000; $i++) { $big += $sentence }
$input = 'Count the total number of sentences in the following text and reply with ONLY the number. TEXT: ' + $big
foreach ($effort in @('high','xhigh')) {
  $body = @{ model='gpt-5.6-sol'; input=$input; stream=$true; reasoning=@{ effort=$effort } } | ConvertTo-Json -Depth 4 -Compress
  $bodyFile = "C:\Users\Eric\codex-home-test\body_$effort.json"
  [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = (& curl.exe -sS -N -m 240 -w "`nHTTP:%{http_code}" -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$bodyFile" https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
  $sw.Stop()
  $code = if ($out -match 'HTTP:(\d+)') { $Matches[1] } else { '???' }
  $completed = ($out -match 'event: response.completed')
  $pingCount = ([regex]::Matches($out, ': PING')).Count
  $dataEvents = ([regex]::Matches($out, '(?m)^event: (\S+)') | ForEach-Object { $_.Groups[1].Value })
  $last = ($dataEvents | Select-Object -Last 1)
  Write-Output ("48k+{0}: http={1} elapsed={2}s completed={3} pings={4} events={5} last={6}" -f $effort, $code, [int]$sw.Elapsed.TotalSeconds, $completed, $pingCount, $dataEvents.Count, $last)
}
Write-Output 'done'
