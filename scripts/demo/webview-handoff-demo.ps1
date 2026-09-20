param(
    [string]$BaseUrl = "http://localhost:5050",
    [string]$SiteKey = "demo",
    [string]$TargetPath = "/random-business",
    [Parameter(Mandatory = $true)][string]$AppKey,
    [Parameter(Mandatory = $true)][string]$AppSecret
)

$ErrorActionPreference = "Stop"

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Get-BodyHash([byte[]]$Body) {
    if ($null -eq $Body) { $Body = [byte[]]@() }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ConvertTo-Base64Url ($sha.ComputeHash($Body)) }
    finally { $sha.Dispose() }
}

function Get-Signature([string]$Secret, [string]$Payload) {
    $keyBytes = [Text.Encoding]::UTF8.GetBytes($Secret)
    $hmac = New-Object Security.Cryptography.HMACSHA256 -ArgumentList (,$keyBytes)
    try { return ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Payload))) }
    finally { $hmac.Dispose() }
}

$bodyText = (@{ siteKey = $SiteKey; targetPath = $TargetPath } | ConvertTo-Json -Compress)
$body = [Text.Encoding]::UTF8.GetBytes($bodyText)
$uri = [Uri]($BaseUrl.TrimEnd("/") + "/Gateway/WebViewHandoff.ashx")
$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$nonceBytes = New-Object byte[] 18
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($nonceBytes) } finally { $rng.Dispose() }
$nonce = ConvertTo-Base64Url $nonceBytes
$bodyHash = Get-BodyHash $body
$payload = "$timestamp`n$nonce`nPOST`n$($uri.AbsolutePath)$($uri.Query)`n$bodyHash"
$signature = Get-Signature $AppSecret $payload
$headers = @{
    "X-Gateway-Device-Key" = $AppKey
    "X-Gateway-Device-Timestamp" = $timestamp.ToString()
    "X-Gateway-Device-Nonce" = $nonce
    "X-Gateway-Device-Body-SHA256" = $bodyHash
    "X-Gateway-Device-Signature" = $signature
}

$result = Invoke-WebRequest -Uri $uri -Method POST -Body $body -ContentType "application/json" -Headers $headers -UseBasicParsing
$result.Content
Write-Host "Open the returned consumeUrl in the APP WebView. Do not put AppSecret, SessionKey, or device identifiers in the URL."
