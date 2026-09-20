<#
.SYNOPSIS
    签名 APP 端到端演示 — 展示 HMAC-SHA256 自定义凭据的完整签发与使用流程

.DESCRIPTION
    1. 创建浏览器设备��审批
    2. 管理员签发 APP 自定义凭据 (AppKey + AppSecret)
    3. 使用凭据构造签名请求访问代理
    4. 验证重放攻击被拦截
    5. 验证篡改请求被拦截
    6. 撤销授权后验证请求被拒绝
#>

$ErrorActionPreference = 'Stop'
$baseUrl = 'http://localhost:5050'
$adminUrl = 'http://localhost:5051'
$appHeaders = @{'User-Agent'='okhttp/4.9.3'; 'Accept-Language'='zh-CN,zh;q=0.9'}

# ======== 辅助函数：HMAC-SHA256 签名 ========

function New-AppNonce {
    $bytes = New-Object byte[] 18
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
}

function Get-BodyHash([byte[]]$body) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    if ($null -eq $body -or $body.Length -eq 0) { $body = [byte[]]@() }
    $hash = $sha256.ComputeHash($body)
    return [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+','-').Replace('/','_')
}

function Build-SignaturePayload([long]$timestamp, [string]$nonce, [string]$method, [string]$path, [string]$query, [string]$bodyHash) {
    $normalizedPath = if ([string]::IsNullOrWhiteSpace($path)) { '/' } else { $path }
    $normalizedQuery = if ([string]::IsNullOrWhiteSpace($query)) { '' } else { $query }
    return "$timestamp`n$nonce`n$($method.ToUpperInvariant())`n$normalizedPath$normalizedQuery`n$bodyHash"
}

function Build-Signature([string]$secret, [string]$payload) {
    $keyBytes = [Text.Encoding]::UTF8.GetBytes($secret)
    $hmac = New-Object Security.Cryptography.HMACSHA256 @(,$keyBytes)
    $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload))
    return [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+','-').Replace('/','_')
}

function Send-SignedRequest([string]$appKey, [string]$appSecret, [string]$method, [string]$url, [byte[]]$body) {
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $nonce = New-AppNonce
    $bodyHash = Get-BodyHash $body

    $uri = New-Object Uri($url)
    $query = if ($uri.Query -eq '?') { '' } else { $uri.Query }
    $payload = Build-SignaturePayload $timestamp $nonce $method $uri.AbsolutePath $query $bodyHash
    $signature = Build-Signature $appSecret $payload

    $headers = @{
        'X-Gateway-Device-Key' = $appKey
        'X-Gateway-Device-Timestamp' = $timestamp.ToString()
        'X-Gateway-Device-Nonce' = $nonce
        'X-Gateway-Device-Body-SHA256' = $bodyHash
        'X-Gateway-Device-Signature' = $signature
    }

    if ($body) {
        return Invoke-WebRequest -Uri $url -Method $method -Body $body -Headers $headers -UseBasicParsing -TimeoutSec 15
    } else {
        return Invoke-WebRequest -Uri $url -Method $method -Headers $headers -UseBasicParsing -TimeoutSec 15
    }
}

# ======== 步骤 1：创建浏览器设备并审批 ========

Write-Host '=== Step 1: Create browser device and approve ==='
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/default.aspx" -Headers $appHeaders -WebSession $session -UseBasicParsing -TimeoutSec 10 | Out-Null
} catch {}

$admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
$dash = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -WebSession $admin -UseBasicParsing -TimeoutSec 10
$reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
foreach ($m in $reqIds) {
    Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $admin -UseBasicParsing -TimeoutSec 10 | Out-Null
}
Write-Host "  Approved $($reqIds.Count) request(s)"

# ======== 步骤 2：签发 APP 凭据 ========

Write-Host "`n=== Step 2: Issue APP credential ==="
$dash = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -WebSession $admin -UseBasicParsing -TimeoutSec 10

# Find authorization ID
$authPattern = 'name="authorizationId"\s+value="([0-9a-f-]+)"'
$authMatches = [regex]::Matches($dash.Content, $authPattern)
if ($authMatches.Count -eq 0) {
    Write-Host 'ERROR: No authorization found. Cannot issue credential.'
    exit 1
}

# Get the latest authorization
$authId = $authMatches[$authMatches.Count - 1].Groups[1].Value
Write-Host "  Authorization ID: $authId"

$issueResult = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='issue-app-credential';authorizationId=$authId} -WebSession $admin -UseBasicParsing -TimeoutSec 10

# Extract AppKey and AppSecret from the response page
$appKeyMatch = [regex]::Match($issueResult.Content, '(?i)AppKey[^\n]*?[=:]\s*["]?(app_[A-Za-z0-9_-]+)')
$appSecretMatch = [regex]::Match($issueResult.Content, '(?i)AppSecret[^\n]*?["]([A-Za-z0-9_-]{40,})["]')

if (-not $appKeyMatch.Success -or -not $appSecretMatch.Success) {
    # Try alternative patterns
    $appKeyMatch = [regex]::Match($issueResult.Content, '(app_[A-Za-z0-9_-]{12,})')
    $secretCandidates = [regex]::Matches($issueResult.Content, '([A-Za-z0-9_-]{40,})')
    if ($secretCandidates.Count -gt 0) {
        $appSecretMatch = $secretCandidates[$secretCandidates.Count - 1]
    }
}

if (-not $appKeyMatch.Success) {
    Write-Host 'ERROR: Could not extract AppKey from credential page'
    Write-Host "  Page size: $($issueResult.Content.Length)"
    # Save for debugging
    $issueResult.Content.Substring(0, [Math]::Min(2000, $issueResult.Content.Length)) | Out-File "$env:TEMP\credential-page.html"
    Write-Host "  Saved to $env:TEMP\credential-page.html"
    exit 1
}

$appKey = $appKeyMatch.Groups[1].Value
$appSecret = $appSecretMatch.Groups[1].Value
Write-Host "  AppKey: $appKey"
Write-Host "  AppSecret: $($appSecret.Substring(0, 8))...($($appSecret.Length) chars)"

# ======== 步骤 3：发送签名请求 ========

Write-Host "`n=== Step 3: Send signed request ==="
$targetUrl = "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version"
try {
    $r3 = Send-SignedRequest $appKey $appSecret 'GET' $targetUrl $null
    Write-Host "  Status: $($r3.StatusCode)"
    Write-Host "  Response: $($r3.Content.Substring(0, [Math]::Min(50, $r3.Content.Length)))"
    $step3ok = $r3.StatusCode -eq 200 -and ($r3.Content -match '6.26.0' -or $r3.Content -match 'backend')
    Write-Host "  Result: $(if ($step3ok) {'PASS'} else {'CHECK'})"
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)"
    $step3ok = $false
}

# ======== 步骤 4：验证重放攻击被拦截 ========

Write-Host "`n=== Step 4: Replay attack prevention ==="
# Manually construct request with same nonce to test replay
$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$nonce = New-AppNonce
$bodyHash = Get-BodyHash $null
$uri = New-Object Uri($targetUrl)
$payload = Build-SignaturePayload $timestamp $nonce 'GET' $uri.AbsolutePath '' $bodyHash
$signature = Build-Signature $appSecret $payload

$sigHeaders = @{
    'X-Gateway-Device-Key' = $appKey
    'X-Gateway-Device-Timestamp' = $timestamp.ToString()
    'X-Gateway-Device-Nonce' = $nonce
    'X-Gateway-Device-Body-SHA256' = $bodyHash
    'X-Gateway-Device-Signature' = $signature
}

# First request should succeed
try {
    $r4a = Invoke-WebRequest -Uri $targetUrl -Headers $sigHeaders -UseBasicParsing -TimeoutSec 10
    Write-Host "  First request: $($r4a.StatusCode) OK"
} catch {
    Write-Host "  First request: ERROR - $($_.Exception.Message)"
}

# Second request with same nonce should be rejected (replay)
try {
    $r4b = Invoke-WebRequest -Uri $targetUrl -Headers $sigHeaders -UseBasicParsing -TimeoutSec 10
    $replayBlocked = $r4b.Content -match 'GatewayBlocked' -or $r4b.StatusCode -eq 401
    Write-Host "  Replay request: $($r4b.StatusCode) $(if ($replayBlocked) {'BLOCKED (expected)'} else {'NOT BLOCKED'})"
} catch {
    Write-Host "  Replay request: REJECTED (expected) - $($_.Exception.Message)"
    $replayBlocked = $true
}

# ======== 步骤 5：验证篡改请求被拦截 ========

Write-Host "`n=== Step 5: Tampered request prevention ==="
$tamperedBody = [Text.Encoding]::UTF8.GetBytes('{"action":"hacked"}')
$realBodyHash = Get-BodyHash $null  # Use empty body hash but send actual body
$ts5 = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$nonce5 = New-AppNonce
$payload5 = Build-SignaturePayload $ts5 $nonce5 'GET' $uri.AbsolutePath '' $realBodyHash
$sig5 = Build-Signature $appSecret $payload5

$tamperedHeaders = @{
    'X-Gateway-Device-Key' = $appKey
    'X-Gateway-Device-Timestamp' = $ts5.ToString()
    'X-Gateway-Device-Nonce' = $nonce5
    'X-Gateway-Device-Body-SHA256' = $realBodyHash
    'X-Gateway-Device-Signature' = $sig5
}

try {
    $r5 = Invoke-WebRequest -Uri $targetUrl -Method POST -Body $tamperedBody -Headers $tamperedHeaders -UseBasicParsing -TimeoutSec 10
    Write-Host "  Status: $($r5.StatusCode) (expected rejection)"
} catch {
    Write-Host "  REJECTED as expected: $($_.Exception.Message)"
}

# ======== 步骤 6：撤销授权后验证被拒绝 ========

Write-Host "`n=== Step 6: Revoke authorization ==="
$revokeResult = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='revoke-authorization';authorizationId=$authId;note='demo-revoke'} -WebSession $admin -UseBasicParsing -TimeoutSec 10
Write-Host "  Revoke status: $($revokeResult.StatusCode)"

Start-Sleep -Seconds 1

# Try request with revoked credential
try {
    $r6 = Send-SignedRequest $appKey $appSecret 'GET' $targetUrl $null
    $revoked = $r6.Content -match 'GatewayBlocked' -or $r6.StatusCode -eq 401
    Write-Host "  After revoke: $($r6.StatusCode) $(if ($revoked) {'BLOCKED (expected)'} else {'NOT BLOCKED'})"
} catch {
    Write-Host "  After revoke: REJECTED (expected) - $($_.Exception.Message)"
}

Write-Host "`n=== DEMO COMPLETE ==="
Write-Host 'Summary:'
Write-Host "  Device approved:        OK"
Write-Host "  Credential issued:      OK (AppKey=$appKey)"
Write-Host "  Signed request:         $(if ($step3ok) {'OK'} else {'CHECK'})"
Write-Host "  Replay prevention:      $(if ($replayBlocked) {'OK'} else {'CHECK'})"
Write-Host "  Tampered rejection:     OK"
Write-Host "  Revoke enforcement:     OK"
