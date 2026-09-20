$key = (Get-Content "$env:USERPROFILE\.codex\auth.json" -Raw | ConvertFrom-Json).OPENAI_API_KEY
$tests = @(
  @{ n='baseline'; b=@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true } },
  @{ n='max_output_tokens'; b=@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true; max_output_tokens=100 } },
  @{ n='reasoning_medium'; b=@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true; reasoning=@{ effort='medium' } } },
  @{ n='instructions'; b=@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true; instructions='You are a helpful assistant.' } },
  @{ n='tools_empty'; b=@{ model='gpt-5.6-sol'; input='Reply with exactly the word: ok'; stream=$true; tools=@() } },
  @{ n='input_array'; b=@{ model='gpt-5.6-sol'; input=@(@{ role='user'; content=@(@{ type='input_text'; text='Reply with exactly the word: ok' }) }); stream=$true } },
  @{ n='full_codex_style'; b=@{ model='gpt-5.6-sol'; input=@(@{ role='user'; content=@(@{ type='input_text'; text='Reply with exactly the word: ok' }) }); stream=$true; instructions='You are Codex.'; reasoning=@{ effort='xhigh' }; tools=@(); parallel_tool_calls=$true } }
)
foreach ($t in $tests) {
  $json = $t.b | ConvertTo-Json -Depth 6 -Compress
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = (& curl.exe -sS -N -m 60 -w "`nHTTPCODE:%{http_code}" -H "Authorization: Bearer $key" -H "Content-Type: application/json" -d $json https://premium.hezubus.cc/v1/responses 2>&1 | Out-String)
  $sw.Stop()
  $code = if ($out -match 'HTTPCODE:(\d+)') { $Matches[1] } else { '???' }
  $completed = ($out -match 'event: response.completed')
  Write-Output ("{0}: http={1} elapsed={2}s completed={3}" -f $t.n, $code, [int]$sw.Elapsed.TotalSeconds, $completed)
  if (-not $completed) {
    $body = $out -replace "`nHTTPCODE:.*$", ''
    if ($body.Length -gt 260) { $body = $body.Substring(0, 260) }
    Write-Output ("   -> " + ($body -replace "`n", ' '))
  }
}
Write-Output 'done'
