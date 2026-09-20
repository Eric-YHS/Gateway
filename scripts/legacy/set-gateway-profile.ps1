<#
.SYNOPSIS
    切换 legacy 网关的上游站点配置 profile。

.DESCRIPTION
    支持 profile：
      - experience  ：交付占位配置（需要编辑 UpstreamBaseUrl）
      - local-demo  ：本地演示站（需要指定 -DemoBaseUrl）

    切换时会用对应模板覆盖 gateway-sites.json，并在同目录生成
    gateway-sites.profile.txt 记录当前激活的 profile 名称。

.PARAMETER Profile
    要激活的 profile 名称：experience 或 local-demo。

.PARAMETER DemoBaseUrl
    仅 local-demo profile 需要。本地演示站的基础 URL，例如
    http://localhost:80/ 或 http://localhost:8080/ERP/。

.EXAMPLE
    .\set-gateway-profile.ps1 -Profile experience

.EXAMPLE
    .\set-gateway-profile.ps1 -Profile local-demo -DemoBaseUrl http://localhost:80/
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("experience", "local-demo")]
    [string]$Profile,

    [string]$DemoBaseUrl = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 定位 gateway-sites.json 所在目录
# 优先用 $PSScriptRoot（-File 模式），fallback 到脚本文件物理路径
$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if (-not $scriptPath) {
        # -Command 模式下两者都为空，从已知相对路径推导
        $scriptRoot = Join-Path $PWD "scripts\legacy"
    } else {
        $scriptRoot = Split-Path -Parent $scriptPath
    }
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$appDataDir = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\App_Data"

$sitesFile = Join-Path $appDataDir "gateway-sites.json"
$profileMarker = Join-Path $appDataDir "gateway-sites.profile.txt"

if (-not (Test-Path $appDataDir)) {
    throw "App_Data directory not found: $appDataDir"
}

switch ($Profile) {
    "experience" {
        $templateFile = Join-Path $appDataDir "gateway-sites.experience.json"
        if (-not (Test-Path $templateFile)) {
            throw "experience template not found: $templateFile"
        }
        Copy-Item -Path $templateFile -Destination $sitesFile -Force
        Set-Content -Path $profileMarker -Value "experience" -Encoding UTF8 -Force
        Write-Host "OK - switched to experience profile"
        Write-Host "     Edit UpstreamBaseUrl in gateway-sites.json before use."
    }

    "local-demo" {
        if ([string]::IsNullOrWhiteSpace($DemoBaseUrl)) {
            throw "local-demo profile requires -DemoBaseUrl, e.g.: -DemoBaseUrl http://localhost:80/"
        }

        # Validate: must be an absolute http:// or https:// URL
        $uri = $null
        $parsedOk = [System.Uri]::TryCreate($DemoBaseUrl, [System.UriKind]::Absolute, [ref]$uri)
        if (-not $parsedOk -or -not $uri.IsAbsoluteUri) {
            throw "Invalid -DemoBaseUrl '$DemoBaseUrl': must be an absolute URL (e.g. http://localhost:80/)."
        }
        if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') {
            throw "Invalid -DemoBaseUrl '$DemoBaseUrl': only http:// and https:// schemes are allowed (got '$($uri.Scheme)://')."
        }
        if ([string]::IsNullOrWhiteSpace($uri.Host)) {
            throw "Invalid -DemoBaseUrl '$DemoBaseUrl': URL must contain a host."
        }

        # normalize URL: ensure trailing /
        $normalizedUrl = $uri.ToString().TrimEnd('/') + '/'

        $templateFile = Join-Path $appDataDir "gateway-sites.local-demo.template.json"
        if (-not (Test-Path $templateFile)) {
            throw "local-demo template not found: $templateFile"
        }

        # Read template, replace placeholder, then write (atomic: only write on success)
        $templateContent = Get-Content -Path $templateFile -Raw -Encoding UTF8
        $replaced = $templateContent -replace '\{\{DemoBaseUrl\}\}', $normalizedUrl
        Set-Content -Path $sitesFile -Value $replaced -Encoding UTF8 -Force

        $markerValue = "local-demo|" + $normalizedUrl
        Set-Content -Path $profileMarker -Value $markerValue -Encoding UTF8 -Force
        Write-Host "OK - switched to local-demo profile (local demo site)"
        Write-Host "     UpstreamBaseUrl = $normalizedUrl"
    }
}

Write-Host ""
Write-Host "Config file: $sitesFile"
Write-Host "Profile marker: $profileMarker"

# display first site summary after switch
$json = Get-Content -Path $sitesFile -Raw -Encoding UTF8 | ConvertFrom-Json
$firstSite = @($json)[0]
if ($firstSite) {
    Write-Host ""
    Write-Host "--- active site config summary ---"
    Write-Host "  Key        : $($firstSite.Key)"
    Write-Host "  Name       : $($firstSite.Name)"
    Write-Host "  Upstream   : $($firstSite.UpstreamBaseUrl)"
    Write-Host "  EntryPath  : $($firstSite.EntryPath)"
    Write-Host "  ExposeRoot : $($firstSite.ExposeLegacyAppAtRoot)"
    Write-Host "  Version    : $($firstSite.LegacyAppVersionResponse)"
    Write-Host "  TenantName : $($firstSite.LegacyAppTenantNameResponse)"
}
