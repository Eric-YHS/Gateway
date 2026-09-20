$key = (Get-Content 'C:\Users\Eric\.codex\auth.json' -Raw | ConvertFrom-Json).OPENAI_API_KEY
$sentence = 'The quick brown fox jumps over the lazy dog near the river bank. '
$big = ''
for ($i = 0; $i -lt 3000; $i++) { $big += $sentence }
$input = 'Count the total number of sentences in the following text and reply with ONLY the number. TEXT: ' + $big
$body = @{ model='gpt-5.6-sol'; input=$input; stream=$true; reasoning=@{ effort='xhigh' } } | ConvertTo-Json -Depth 4 -Compress
$bodyFile = 'C:\Users\Eric\codex-home-test\big_body.json'
[System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
$bodyBytes = [System.IO.File]::ReadAllBytes($bodyFile).Length
Write-Output ("request_body_bytes=" + $bodyBytes + " approx_input_tokens=" + [int]($bodyBytes / 4))
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$out = (& curl.exe -sS -N -m 1200 -w "`nHTTP:%{http_code}" -H "Authorization: Bearer $key" -H "Content-Type: application/json" --data-binary "@$bodyFile" https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
$sw.Stop()
$code = if ($out -match 'HTTP:(\d+)') { $Matches[1] } else { '???' }
$completed = ($out -match 'event: response.completed')
$failed = ($out -match 'response.failed')
$lastEvent = ([regex]::Matches($out, '(?m)^event: (\S+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Last 1)
$errMsg = ''
if ($out -match '"message":"([^"]{0,200})"') { $errMsg = $Matches[1] }
Write-Output ("elapsed_s=" + [int]$sw.Elapsed.TotalSeconds + " http=$code completed=$completed failed=$failed last_event=$lastEvent err=$errMsg")
$out | Set-Content -Path 'C:\Users\Eric\codex-home-test\big_stream_test.txt' -Encoding UTF8
Write-Output 'done'
