$key = (Get-Content 'C:\Users\Eric\.codex\auth.json' -Raw | ConvertFrom-Json).OPENAI_API_KEY
$sentence = 'The quick brown fox jumps over the lazy dog near the river bank. '
$mkBig = { param($reps) $s = ''; for ($i = 0; $i -lt $reps; $i++) { $s += $sentence }; $s }
$tests = @(
  @{ n='100k_high'; reps=6400; effort='high' },
  @{ n='100k_xhigh'; reps=6400; effort='xhigh' },
  @{ n='150k_high'; reps=9600; effort='high' }
)
foreach ($t in $tests) {
  $big = & $mkBig $t.reps
  $input = 'Count the total number of sentences in the following text and reply with ONLY the number. TEXT: ' + $big
  $body = @{ model='gpt-5.6-sol'; input=$input; stream=$true; reasoning=@{ effort=$t.effort } } | ConvertTo-Json -Depth 4 -Compress
  $bodyFile = "C:\Users\Eric\codex-home-test\body_$($t.n).json"
  [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
  $bytes = [System.IO.File]::ReadAllBytes($bodyFile).Length
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = (& curl.exe -sS -N -m 300 -w "`nHTTP:%{http_code}" -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$bodyFile" https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
  $sw.Stop()
  $code = if ($out -match 'HTTP:(\d+)') { $Matches[1] } else { '???' }
  $completed = ($out -match 'event: response.completed')
  $pingCount = ([regex]::Matches($out, ': PING')).Count
  $events = ([regex]::Matches($out, '(?m)^event: (\S+)') | ForEach-Object { $_.Groups[1].Value })
  $last = ($events | Select-Object -Last 1)
  Write-Output ("{0}: bytes={1} http={2} elapsed={3}s completed={4} pings={5} events={6} last={7}" -f $t.n, $bytes, $code, [int]$sw.Elapsed.TotalSeconds, $completed, $pingCount, $events.Count, $last)
}
Write-Output 'done'
