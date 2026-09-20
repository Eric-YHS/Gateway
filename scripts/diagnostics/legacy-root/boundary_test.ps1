$key = (Get-Content 'C:\Users\Eric\.codex\auth.json' -Raw | ConvertFrom-Json).OPENAI_API_KEY
$sentence = 'The quick brown fox jumps over the lazy dog near the river bank. '
$mkBig = { param($n) $s = ''; for ($i = 0; $i -lt $n; $i++) { $s += $sentence }; $s }
$tests = @(
  @{ n='48k_medium'; reps=3000; effort='medium' },
  @{ n='12k_xhigh'; reps=750; effort='xhigh' }
)
foreach ($t in $tests) {
  $big = & $mkBig $t.reps
  $input = 'Count the total number of sentences in the following text and reply with ONLY the number. TEXT: ' + $big
  $body = @{ model='gpt-5.6-sol'; input=$input; stream=$true; reasoning=@{ effort=$t.effort } } | ConvertTo-Json -Depth 4 -Compress
  $bodyFile = "C:\Users\Eric\codex-home-test\body_$($t.n).json"
  [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = (& curl.exe -sS -N -m 240 -w "`nHTTP:%{http_code}" -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$bodyFile" https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
  $sw.Stop()
  $code = if ($out -match 'HTTP:(\d+)') { $Matches[1] } else { '???' }
  $completed = ($out -match 'event: response.completed')
  $failed = ($out -match 'response.failed')
  $dataEvents = ([regex]::Matches($out, '(?m)^event: (response\.[a-z_.]+)') | ForEach-Object { $_.Groups[1].Value })
  $dataCount = ($dataEvents | Where-Object { $_ -ne 'response.created' -and $_ -ne 'response.in_progress' }).Count
  $last = ($dataEvents | Select-Object -Last 1)
  Write-Output ("{0}: http={1} elapsed={2}s completed={3} failed={4} data_events={5} last={6}" -f $t.n, $code, [int]$sw.Elapsed.TotalSeconds, $completed, $failed, $dataCount, $last)
}
Write-Output 'done'
