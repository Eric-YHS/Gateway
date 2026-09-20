$ErrorActionPreference = 'Stop'
$baseUrl = 'http://localhost:5050'
$adminUrl = 'http://localhost:5051'

# Real APP User-Agent (uni-app framework from APK analysis)
$realAppUA = 'Mozilla/5.0 (Linux; Android 13; SM-G9910) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36 uni-app Html5Plus/1.0 (Immersed/30.000000)'

$passCount = 0
$failCount = 0
$results = @()

function Write-Result($name, $pass, $status, $detail) {
    $tag = 'PASS'
    if (-not $pass) { $tag = 'FAIL'; $script:failCount++ } else { $script:passCount++ }
    Write-Host "$tag [$status] $name"
    Write-Host "  $detail"
    $script:results += @{name=$name; pass=$pass; status=$status; detail=$detail; tag=$tag}
}

# Setup
$appSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$appH = New-Object System.Collections.Generic.Dictionary"[string,string]"
$appH.Add('User-Agent', $realAppUA)
$appH.Add('Accept-Language', 'zh-CN,zh;q=0.9')

# Test 1: APP bootstrap - sys.ashx version
Write-Host '=== Test 1: APP bootstrap sys.ashx ==='
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    Write-Result 'APP bootstrap' ($r.Content -match '6\.26\.0') $r.StatusCode "Content: $($r.Content)"
} catch { Write-Result 'APP bootstrap' $false 'ERR' $_.Exception.Message }

# Test 2: APP bootstrap - tenant_name
Write-Host "`n=== Test 2: APP tenant_name ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=tenant_name" -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    Write-Result 'APP tenant_name' ($r.StatusCode -eq 200) $r.StatusCode "Content: $($r.Content)"
} catch { Write-Result 'APP tenant_name' $false 'ERR' $_.Exception.Message }

# Test 3: APP unauthorized login (should be blocked)
Write-Host "`n=== Test 3: APP unauthorized login ==="
try {
    $loginH = New-Object System.Collections.Generic.Dictionary"[string,string]"
    $loginH.Add('User-Agent', $realAppUA)
    $loginH.Add('Accept-Language', 'zh-CN,zh;q=0.9')
    $loginH.Add('Content-Type', 'application/x-www-form-urlencoded')
    $loginBody = 'UserName=admin&UserId=admin&UserPwd=123456&MachineId=kp-test-001&DeviceImei=kp-test-001&DeviceType=2&TrAppKey=__UNI__C0948B1'
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers $loginH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    $blocked = $r.Content -match 'GatewayBlocked'
    Write-Result 'APP unauthorized' $blocked $r.StatusCode "Blocked: $blocked"
} catch {
    Write-Result 'APP unauthorized' $true 'BLOCKED' "Error (expected): $($_.Exception.Message)"
}

# Setup: Admin approve APP device
Write-Host "`n=== Setup: Admin approve ==="
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='login';username='gateway-admin';password='GatewayDemo!2026'} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
    $dash = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
    $reqIds = [regex]::Matches($dash.Content, 'name="requestId"\s+value="([0-9a-f-]+)"')
    foreach ($m in $reqIds) {
        Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='approve-request';requestId=$m.Groups[1].Value} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10 | Out-Null
    }
    Write-Host "  Approved: $($reqIds.Count) request(s)"
} catch { Write-Host "  Admin error: $($_.Exception.Message)" }

# Test 5: APP authorized login (should proxy to upstream)
Write-Host "`n=== Test 5: APP authorized login ==="
try {
    $loginH2 = New-Object System.Collections.Generic.Dictionary"[string,string]"
    $loginH2.Add('User-Agent', $realAppUA)
    $loginH2.Add('Accept-Language', 'zh-CN,zh;q=0.9')
    $loginH2.Add('Content-Type', 'application/x-www-form-urlencoded')
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/user/login" -Method POST -Body $loginBody -Headers $loginH2 -WebSession $appSession -UseBasicParsing -TimeoutSec 15
    $stillBlocked = $r.Content -match 'GatewayBlocked'
    $pass5 = -not $stillBlocked -and $r.StatusCode -eq 200
    Write-Result 'APP authorized login' $pass5 $r.StatusCode "Blocked: $stillBlocked, Size: $($r.Content.Length)"
    if ($r.Content.Length -lt 500) { Write-Host "  Content: $($r.Content)" }
} catch { Write-Result 'APP authorized login' $false 'ERR' $_.Exception.Message }

# Test 6: Root mode sys.ashx
Write-Host "`n=== Test 6: Root mode sys.ashx ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/ashx/sys.ashx?action=version" -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    Write-Result 'Root sys.ashx' ($r.Content -match '6\.26\.0') $r.StatusCode "Content: $($r.Content)"
} catch { Write-Result 'Root sys.ashx' $false 'ERR' $_.Exception.Message }

# Test 7: Root mode default.aspx
Write-Host "`n=== Test 7: Root mode default.aspx ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/default.aspx" -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    $hasLogin = $r.Content -match 'txtLoginName'
    Write-Result 'Root default.aspx' ($r.StatusCode -eq 200) $r.StatusCode "Size: $($r.Content.Length), Has login: $hasLogin"
} catch { Write-Result 'Root default.aspx' $false 'ERR' $_.Exception.Message }

# Test 8: WCF via proxy
Write-Host "`n=== Test 8: WCF proxy ==="
try {
    $wcfBody = 'postJson=' + [char]123 + [char]34 + 'test' + [char]34 + ':1' + [char]125
    $r = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/WCFService/PostBus.ashx" -Method POST -Body $wcfBody -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    $wcfOk = -not ($r.Content -match 'GatewayBlocked')
    Write-Result 'WCF proxy' $wcfOk $r.StatusCode "Size: $($r.Content.Length)"
} catch {
    $msg = $_.Exception.Message
    Write-Result 'WCF proxy' (-not ($msg -match 'GatewayBlocked')) 'ERR' "Error: $msg"
}

# Test 9: Root mode WCF
Write-Host "`n=== Test 9: Root WCF ==="
try {
    $wcfBody2 = 'postJson=' + [char]123 + [char]34 + 'test' + [char]34 + ':1' + [char]125
    $r = Invoke-WebRequest -Uri "$baseUrl/WCFService/PostBus.ashx" -Method POST -Body $wcfBody2 -Headers $appH -WebSession $appSession -UseBasicParsing -TimeoutSec 10
    Write-Result 'Root WCF' ($r.StatusCode -eq 200) $r.StatusCode "Size: $($r.Content.Length)"
} catch { Write-Result 'Root WCF' $false 'ERR' $_.Exception.Message }

# Test 10: Signed APP credential
Write-Host "`n=== Test 10: Signed APP credential ==="
try {
    $dash = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -WebSession $adminSession -UseBasicParsing -TimeoutSec 10
    $authPattern = 'name="authorizationId"\s+value="([0-9a-f-]+)"'
    $authMatches = [regex]::Matches($dash.Content, $authPattern)
    if ($authMatches.Count -gt 0) {
        $authId = $authMatches[$authMatches.Count - 1].Groups[1].Value
        $issueResult = Invoke-WebRequest -Uri "$adminUrl/Admin/Default.aspx" -Method POST -Body @{action='issue-app-credential';authorizationId=$authId} -WebSession $adminSession -UseBasicParsing -TimeoutSec 10

        $appKeyMatch = [regex]::Match($issueResult.Content, '(app_[A-Za-z0-9_-]{12,})')
        $secretCandidates = [regex]::Matches($issueResult.Content, '([A-Za-z0-9_-]{40,})')
        if ($appKeyMatch.Success -and $secretCandidates.Count -gt 0) {
            $appKey = $appKeyMatch.Groups[1].Value
            $appSecret = $secretCandidates[$secretCandidates.Count - 1].Groups[1].Value

            $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            $nonceBytes = New-Object byte[] 18
            [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($nonceBytes)
            $nonce = [Convert]::ToBase64String($nonceBytes).TrimEnd('=').Replace('+','-').Replace('/','_')
            $sha256 = [Security.Cryptography.SHA256]::Create()
            $bodyHash = [Convert]::ToBase64String($sha256.ComputeHash([byte[]]@())).TrimEnd('=').Replace('+','-').Replace('/','_')

            $targetUri = New-Object Uri("$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version")
            $payload = "$timestamp`n$nonce`nGET`n$($targetUri.AbsolutePath)$($targetUri.Query)`n$bodyHash"
            $keyBytes = [Text.Encoding]::UTF8.GetBytes($appSecret)
            $hmac = New-Object Security.Cryptography.HMACSHA256 @(,$keyBytes)
            $sig = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload))).TrimEnd('=').Replace('+','-').Replace('/','_')

            $sigH = New-Object System.Collections.Generic.Dictionary"[string,string]"
            $sigH.Add('X-Gateway-Device-Key', $appKey)
            $sigH.Add('X-Gateway-Device-Timestamp', $timestamp.ToString())
            $sigH.Add('X-Gateway-Device-Nonce', $nonce)
            $sigH.Add('X-Gateway-Device-Body-SHA256', $bodyHash)
            $sigH.Add('X-Gateway-Device-Signature', $sig)
            $sigH.Add('User-Agent', $realAppUA)

            $r10 = Invoke-WebRequest -Uri "$baseUrl/proxy/erp-main/ashx/sys.ashx?action=version" -Headers $sigH -UseBasicParsing -TimeoutSec 10
            Write-Result 'Signed APP credential' ($r10.Content -match '6\.26\.0') $r10.StatusCode "AppKey=$appKey, Content=$($r10.Content)"
        } else {
            Write-Result 'Signed APP credential' $false 'N/A' "Could not extract AppKey/Secret"
        }
    } else {
        Write-Result 'Signed APP credential' $false 'N/A' "No authorization found"
    }
} catch { Write-Result 'Signed APP credential' $false 'ERR' $_.Exception.Message }

# Test 11: UA detection
Write-Host "`n=== Test 11: UA detection ==="
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/" -Headers $appH -UseBasicParsing -TimeoutSec 10
    Write-Result 'UA detection' ($r.Content -match 'GatewayReady') $r.StatusCode "Content: $($r.Content.Substring(0, [Math]::Min(80, $r.Content.Length)))"
} catch { Write-Result 'UA detection' $false 'ERR' $_.Exception.Message }

# Test 12: Browser UA (no uni-app)
Write-Host "`n=== Test 12: Browser UA ==="
try {
    $browserH = New-Object System.Collections.Generic.Dictionary"[string,string]"
    $browserH.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36')
    $r = Invoke-WebRequest -Uri "$baseUrl/" -Headers $browserH -UseBasicParsing -TimeoutSec 10
    Write-Result 'Browser UA' ($r.StatusCode -eq 200) $r.StatusCode "Content: $($r.Content.Substring(0, [Math]::Min(80, $r.Content.Length)))"
} catch { Write-Result 'Browser UA' $false 'ERR' $_.Exception.Message }

# Summary
Write-Host "`n`n========== RESULTS =========="
Write-Host "Real Kuaipu APP Simulation (uni-app framework)"
Write-Host ""
foreach ($entry in $results) {
    $icon = if ($entry.pass) { '[v]' } else { '[x]' }
    Write-Host "  $icon $($entry.tag) [$($entry.status)] $($entry.name)"
}
Write-Host ""
Write-Host "TOTAL: $passCount PASS / $failCount FAIL / $($passCount + $failCount) tests"
Write-Host "=================================="
