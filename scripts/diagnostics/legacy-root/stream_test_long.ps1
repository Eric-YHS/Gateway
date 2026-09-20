$ErrorActionPreference = 'Continue'
$key = (Get-Content "$env:USERPROFILE\.codex\auth.json" -Raw | ConvertFrom-Json).OPENAI_API_KEY
$body = @{
  model = 'gpt-5.6-sol'
  input = 'Write a detailed technical essay of about 1500 words on the history and evolution of database indexing, from B-trees to learned indexes. Do not use any tools. Just write the essay.'
  stream = $true
  reasoning = @{ effort = 'xhigh' }
} | ConvertTo-Json -Depth 5 -Compress

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$out = (& curl.exe -sS -N -m 900 -H "Authorization: Bearer $key" -H "Content-Type: application/json" -d $body https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
$sw.Stop()
$curlExit = $LASTEXITCODE

$events = ([regex]::Matches($out, '(?m)^event: (\S+)') | ForEach-Object { $_.Groups[1].Value })
Write-Output ("elapsed_seconds=" + $sw.Elapsed.TotalSeconds)
Write-Output ("curl_exit=" + $curlExit)
Write-Output ("event_count=" + $events.Count)
Write-Output ("completed_count=" + (($events | Where-Object { $_ -eq 'response.completed' }).Count))
Write-Output ("error_count=" + (($events | Where-Object { $_ -match 'error' }).Count))
Write-Output ("first_events=" + (($events | Select-Object -First 8) -join ','))
Write-Output ("last_events=" + (($events | Select-Object -Last 8) -join ','))
Write-Output ("response_failed_present=" + ($out -match 'response.failed'))
if ($out -match '"message":"[^"]{0,300}"') { Write-Output ("error_msg=" + $Matches[0]) }
$out | Set-Content -Path 'E:\验证页面\stream_test_long.txt' -Encoding UTF8
Write-Output 'done'
