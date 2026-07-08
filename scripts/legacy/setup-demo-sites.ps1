param(
    [string]$GatewaySitePath = "",
    [string]$BackendSitePath = "",
    [int]$GatewayPort = 5050,
    [int]$AdminPort = 5051,
    [int[]]$AdditionalGatewayPorts = @(),
    [int]$BackendPort = 5055,
    [string]$GatewaySiteName = "GatewayDemoLegacy",
    [string]$BackendSiteName = "MockBusinessBackendLegacy",
    [string]$GatewayAppPoolName = "",
    [string]$BackendAppPoolName = "",
    [string]$DisplayHost = "",
    [string]$UpstreamBaseUrl = "",
    [string]$GatewayEntryPath = "",
    [string]$DeviceCookieDomain = "",
    [string]$DeviceCookieSecurePolicy = "Never",
    [string]$AdminPassword = "",
    [bool]$BrowserDeviceReviewOnSignalChange = $true,
    [string[]]$BrowserDeviceSignalHeaders = @("User-Agent", "Sec-CH-UA-Platform", "Sec-CH-UA-Mobile"),
    [string]$ExternalScheme = "",
    [string]$UpstreamProxyUrl = "",
    [string]$SiteKey = "2027",
    [string[]]$TrustedProxyAddresses = @(),
    [string[]]$TrustedProxyCidrs = @(),
    [string[]]$GatewayHostNames = @(),
    [string[]]$AnonymousAllowedPaths = @("/sys.ashx*", "/ashx/sys.ashx*", "/datacenter/*", "/default.aspx/datacenter/*"),
    [string[]]$PathRuleProfiles = @("legacy-web-default", "jvs-default"),
    [string[]]$ExternalApiAllowedPathPatterns = @("/api/*", "/mgr/*", "/jvs-public/*"),
    [string[]]$LegacyAppMobileUserAgentMarkers = @(),
    [switch]$RedirectToUpstream,
    [bool]$ConfigureHttpSysLongUrlSupport = $true,
    [bool]$BuildBeforeDeploy = $true,
    [string]$MSBuildPath = "",
    [int]$HttpSysUrlSegmentMaxLength = 32766,
    [int]$HttpSysMaxFieldLength = 65534,
    [int]$HttpSysMaxRequestBytes = 131072,
    [switch]$SkipGatewaySiteConfig
)

$ErrorActionPreference = "Stop"

function New-Utf8NoBomEncoding {
    return New-Object System.Text.UTF8Encoding -ArgumentList $false
}

function New-AdminPasswordHash {
    param([string]$Password)

    $iterations = 210000
    $salt = New-Object byte[] 16
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($salt)
    }
    finally {
        $rng.Dispose()
    }

    $passwordBytes = [System.Text.Encoding]::UTF8.GetBytes($Password)
    $derive = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $passwordBytes,
        $salt,
        $iterations,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $hash = $derive.GetBytes(32)
    }
    finally {
        $derive.Dispose()
    }

    return "pbkdf2-sha256`$$iterations`$" + [Convert]::ToBase64String($salt) + "`$" + [Convert]::ToBase64String($hash)
}

function Read-XmlUtf8 {
    param([string]$Path)

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.LoadXml([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8))
    return $xml
}

function Save-XmlUtf8 {
    param(
        [System.Xml.XmlDocument]$Xml,
        [string]$Path
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Utf8NoBomEncoding
    $settings.OmitXmlDeclaration = $false
    $settings.CloseOutput = $true
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Xml.Save($writer)
    }
    finally {
        $writer.Close()
    }
}

function Set-AppSetting {
    param(
        [System.Xml.XmlDocument]$Xml,
        [string]$Key,
        [string]$Value
    )

    $appSettings = $Xml.SelectSingleNode("/configuration/appSettings")
    if (-not $appSettings) {
        $configuration = $Xml.SelectSingleNode("/configuration")
        $appSettings = $Xml.CreateElement("appSettings")
        $null = $configuration.AppendChild($appSettings)
    }

    $setting = $appSettings.SelectSingleNode("add[@key='$Key']")
    if (-not $setting) {
        $setting = $Xml.CreateElement("add")
        $setting.SetAttribute("key", $Key)
        $null = $appSettings.AppendChild($setting)
    }

    $setting.SetAttribute("value", $Value)
}

function Normalize-Url {
    param([string]$Url)

    return $Url.Trim().TrimEnd("/") + "/"
}

function Normalize-StringList {
    param([string[]]$Values)

    $items = @()
    foreach ($value in $Values) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        $items += $value -split "[,;]" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }
    }

    return @($items | Select-Object -Unique)
}

function Add-UniqueString {
    param(
        [string[]]$Values,
        [string]$Value
    )

    $items = @(Normalize-StringList -Values $Values)
    if (-not [string]::IsNullOrWhiteSpace($Value) -and ($items -notcontains $Value.Trim())) {
        $items += $Value.Trim()
    }

    return $items
}

function Write-GatewaySitesConfig {
    param(
        [string]$Path,
        [string]$SiteUpstreamBaseUrl,
        [string]$SiteEntryPath,
        [string]$ConfiguredSiteKey,
        [string[]]$SiteHostNames,
        [string[]]$SiteAnonymousAllowedPaths,
        [string[]]$SitePathRuleProfiles,
        [string[]]$SiteExternalApiAllowedPathPatterns,
        [string[]]$SiteLegacyAppMobileUserAgentMarkers,
        [bool]$SiteRedirectToUpstream
    )

    $anonymousPaths = @(Normalize-StringList $SiteAnonymousAllowedPaths)
    $normalizedSiteKey = if ([string]::IsNullOrWhiteSpace($ConfiguredSiteKey)) { "2027" } else { $ConfiguredSiteKey.Trim() }
    $profiles = @(Normalize-StringList $SitePathRuleProfiles)
    if ($profiles.Count -eq 0) {
        $profiles = @("legacy-web-default")
    }

    $externalApiAllowedPaths = @(Normalize-StringList $SiteExternalApiAllowedPathPatterns)
    if ($externalApiAllowedPaths.Count -eq 0) {
        $externalApiAllowedPaths = @("/api/*")
    }

    $proxyExternalApiAllowedPaths = @()
    foreach ($pattern in $externalApiAllowedPaths) {
        if ($pattern -eq "*") {
            $proxyExternalApiAllowedPaths += $pattern
            continue
        }

        $cleanPattern = "/" + $pattern.Trim().TrimStart("/")
        $proxyExternalApiAllowedPaths += $cleanPattern
        $proxyExternalApiAllowedPaths += ("/proxy/{0}{1}" -f $normalizedSiteKey, $cleanPattern)
    }

    $mobileUserAgentMarkers = @(Normalize-StringList $SiteLegacyAppMobileUserAgentMarkers)

    $site = [ordered]@{
        Key = $normalizedSiteKey
        Name = "ERP Main"
        Description = ("ERP " + [char]0x4E1A + [char]0x52A1 + [char]0x7CFB + [char]0x7EDF)
        Accent = "#0f766e"
        UpstreamBaseUrl = (Normalize-Url $SiteUpstreamBaseUrl)
        EntryPath = $SiteEntryPath
        ExposeLegacyAppAtRoot = $true
        HostNames = @(Normalize-StringList $SiteHostNames)
        AnonymousAllowedPaths = $anonymousPaths
        PathRuleProfiles = $profiles
        ExternalApiDefaultPathPatterns = @("/api/*")
        ExternalApiClients = @(
            [ordered]@{
                ClientId = "loopback-external-api"
                AllowedIpRanges = @("127.0.0.1", "::1")
                AllowedSiteKeys = @($normalizedSiteKey)
                AllowedPathPatterns = @($proxyExternalApiAllowedPaths | Select-Object -Unique)
                Contact = "Operations"
                Description = "Server-to-server API allowlist. Add partner source IPs and allowed paths here; callers keep the original API URL shape through the gateway."
                Enabled = $true
            }
        )
        RedirectToUpstream = $SiteRedirectToUpstream
    }

    if ($mobileUserAgentMarkers.Count -gt 0) {
        $site.LegacyAppProfile = [ordered]@{
            MobileUserAgentMarkers = $mobileUserAgentMarkers
        }
    }

    $json = ConvertTo-Json -InputObject @($site) -Depth 8
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Utf8NoBomEncoding))
}

function New-OrResetAppPool {
    param(
        [string]$AppCmd,
        [string]$Name
    )

    & $AppCmd delete apppool /apppool.name:$Name 2>$null | Out-Null
    & $AppCmd add apppool /name:$Name | Out-Null
    & $AppCmd set apppool /apppool.name:$Name /managedRuntimeVersion:v4.0 /managedPipelineMode:Integrated /processModel.identityType:ApplicationPoolIdentity | Out-Null
    & $AppCmd start apppool /apppool.name:$Name 2>$null | Out-Null
}

function Grant-ModifyToIdentity {
    param(
        [string]$Path,
        [string]$Identity
    )

    & icacls $Path /grant "$($Identity):(OI)(CI)(M)" /T /C | Out-Null
    return $LASTEXITCODE -eq 0
}

function Grant-AppDataModify {
    param(
        [string]$SitePath,
        [string]$AppPoolName
    )

    $appDataPath = Join-Path $SitePath "App_Data"
    New-Item -ItemType Directory -Path $appDataPath -Force | Out-Null
    $identity = "IIS AppPool\$AppPoolName"
    if (Grant-ModifyToIdentity -Path $appDataPath -Identity $identity) {
        return
    }

    Write-Warning ("Failed to grant App_Data permissions to {0}; falling back to IIS_IUSRS." -f $identity)
    if (-not (Grant-ModifyToIdentity -Path $appDataPath -Identity "IIS_IUSRS")) {
        throw ("Failed to grant Modify permission on {0}. Please grant write permission to the gateway application pool identity manually." -f $appDataPath)
    }
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-WebSocketFeature {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -ErrorAction SilentlyContinue
    if (-not $feature) {
        Write-Warning "Unable to detect IIS-WebSockets. WebSocket proxy requires the IIS WebSocket Protocol feature."
        return
    }

    if ($feature.State -ne "Enabled") {
        if (Test-IsAdministrator) {
            Write-Host "IIS-WebSockets is not enabled. Enabling IIS WebSocket Protocol..."
            Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All -NoRestart | Out-Null
            $feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -ErrorAction SilentlyContinue
            if ($feature -and $feature.State -eq "Enabled") {
                return
            }
        }

        Write-Warning "IIS-WebSockets is not enabled or is pending restart. WebSocket proxy requires: Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All, then restart Windows if the feature reports EnablePending."
    }
}

function Set-HttpSysLongUrlSupport {
    param(
        [int]$UrlSegmentMaxLength,
        [int]$MaxFieldLength,
        [int]$MaxRequestBytes
    )

    if ($UrlSegmentMaxLength -lt 260 -or $UrlSegmentMaxLength -gt 32766) {
        throw "HttpSysUrlSegmentMaxLength must be between 260 and 32766."
    }

    if ($MaxFieldLength -lt 64 -or $MaxFieldLength -gt 65534) {
        throw "HttpSysMaxFieldLength must be between 64 and 65534."
    }

    if ($MaxRequestBytes -lt 256 -or $MaxRequestBytes -gt 16777216) {
        throw "HttpSysMaxRequestBytes must be between 256 and 16777216."
    }

    if ($MaxRequestBytes -lt $MaxFieldLength) {
        throw "HttpSysMaxRequestBytes must be greater than or equal to HttpSysMaxFieldLength."
    }

    $httpParametersPath = "HKLM:\SYSTEM\CurrentControlSet\Services\HTTP\Parameters"
    New-Item -Path $httpParametersPath -Force | Out-Null
    New-ItemProperty -Path $httpParametersPath -Name "UrlSegmentMaxLength" -PropertyType DWord -Value $UrlSegmentMaxLength -Force | Out-Null
    New-ItemProperty -Path $httpParametersPath -Name "MaxFieldLength" -PropertyType DWord -Value $MaxFieldLength -Force | Out-Null
    New-ItemProperty -Path $httpParametersPath -Name "MaxRequestBytes" -PropertyType DWord -Value $MaxRequestBytes -Force | Out-Null
    Write-Warning ("Configured HTTP.sys UrlSegmentMaxLength={0}, MaxFieldLength={1}, MaxRequestBytes={2}. Restart the HTTP service or reboot Windows before relying on very long URLs." -f $UrlSegmentMaxLength, $MaxFieldLength, $MaxRequestBytes)
}

function Resolve-MSBuildPath {
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (Test-Path -LiteralPath $ConfiguredPath) {
            return (Resolve-Path -LiteralPath $ConfiguredPath).Path
        }

        throw ("MSBuildPath does not exist: {0}" -f $ConfiguredPath)
    }

    $candidates = @()
    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    foreach ($vswhere in $vswhereCandidates) {
        if (Test-Path -LiteralPath $vswhere) {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
            foreach ($path in $found) {
                if (-not [string]::IsNullOrWhiteSpace($path)) {
                    $candidates += $path
                }
            }
        }
    }

    $candidates += @(
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        (Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"),
        (Join-Path $env:SystemRoot "Microsoft.NET\Framework\v4.0.30319\MSBuild.exe")
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "MSBuild.exe was not found. Install .NET Framework build tools or pass -BuildBeforeDeploy `$false after compiling the web projects."
}

function Build-LegacySolution {
    param(
        [string]$RootPath,
        [string]$ConfiguredMSBuildPath
    )

    $solutionPath = Join-Path $RootPath "GatewayDemo.Legacy.sln"
    if (-not (Test-Path -LiteralPath $solutionPath)) {
        throw ("Solution file not found: {0}" -f $solutionPath)
    }

    $resolvedMSBuildPath = Resolve-MSBuildPath -ConfiguredPath $ConfiguredMSBuildPath
    Write-Host ("Building solution: {0}" -f $solutionPath)
    & $resolvedMSBuildPath $solutionPath /t:Build /p:Configuration=Release "/p:Platform=Any CPU" /m /v:m
    if ($LASTEXITCODE -ne 0) {
        throw ("MSBuild failed with exit code {0}." -f $LASTEXITCODE)
    }
}

$scriptPath = $MyInvocation.MyCommand.Path
if (-not [System.IO.Path]::IsPathRooted($scriptPath)) {
    $scriptPath = Join-Path (Get-Location) $scriptPath
}
$scriptPath = [System.IO.Path]::GetFullPath($scriptPath)
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptPath))
if ([string]::IsNullOrWhiteSpace($GatewaySitePath)) {
    $GatewaySitePath = Join-Path $root "src\GatewayDemo.Legacy.Web"
}
if ([string]::IsNullOrWhiteSpace($BackendSitePath)) {
    $BackendSitePath = Join-Path $root "src\MockBusinessBackend.Legacy.Web"
}
if ([string]::IsNullOrWhiteSpace($GatewayAppPoolName)) {
    $GatewayAppPoolName = $GatewaySiteName + "AppPool"
}
if ([string]::IsNullOrWhiteSpace($BackendAppPoolName)) {
    $BackendAppPoolName = $BackendSiteName + "AppPool"
}
if ([string]::IsNullOrWhiteSpace($DisplayHost)) {
    $DisplayHost = [System.Net.Dns]::GetHostName()
}

$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"
if (-not (Test-Path $appcmd)) {
    throw "appcmd.exe not found. Please install IIS first."
}

if ($BuildBeforeDeploy) {
    Build-LegacySolution -RootPath $root -ConfiguredMSBuildPath $MSBuildPath
}

Test-WebSocketFeature
& $appcmd unlock config /section:system.webServer/webSocket 2>$null | Out-Null

if ($ConfigureHttpSysLongUrlSupport) {
    Set-HttpSysLongUrlSupport -UrlSegmentMaxLength $HttpSysUrlSegmentMaxLength -MaxFieldLength $HttpSysMaxFieldLength -MaxRequestBytes $HttpSysMaxRequestBytes
}

& $appcmd delete site /site.name:$GatewaySiteName 2>$null | Out-Null
& $appcmd delete site /site.name:$BackendSiteName 2>$null | Out-Null
New-OrResetAppPool -AppCmd $appcmd -Name $GatewayAppPoolName
New-OrResetAppPool -AppCmd $appcmd -Name $BackendAppPoolName

$gatewayBindingList = @("http/*:${GatewayPort}:", "http/*:${AdminPort}:")
foreach ($additionalPort in $AdditionalGatewayPorts) {
    if ($additionalPort -gt 0 -and $gatewayBindingList -notcontains "http/*:${additionalPort}:") {
        $gatewayBindingList += "http/*:${additionalPort}:"
    }
}
$gatewayBindings = $gatewayBindingList -join ","
$backendBindings = "http/*:${BackendPort}:"

& $appcmd add site /name:$GatewaySiteName /bindings:$gatewayBindings /physicalPath:$GatewaySitePath | Out-Null
& $appcmd add site /name:$BackendSiteName /bindings:$backendBindings /physicalPath:$BackendSitePath | Out-Null
& $appcmd set app "$GatewaySiteName/" /applicationPool:$GatewayAppPoolName | Out-Null
& $appcmd set app "$BackendSiteName/" /applicationPool:$BackendAppPoolName | Out-Null

Grant-AppDataModify -SitePath $GatewaySitePath -AppPoolName $GatewayAppPoolName

$gatewayWebConfig = Join-Path $GatewaySitePath "Web.config"
if (Test-Path $gatewayWebConfig) {
    $webConfig = Read-XmlUtf8 -Path $gatewayWebConfig
    Set-AppSetting -Xml $webConfig -Key "Admin.ManagementPort" -Value ([string]$AdminPort)
    if (-not [string]::IsNullOrWhiteSpace($AdminPassword)) {
        Set-AppSetting -Xml $webConfig -Key "Admin.PasswordHash" -Value (New-AdminPasswordHash -Password $AdminPassword)
    }
    Set-AppSetting -Xml $webConfig -Key "Gateway.DeviceCookieDomain" -Value $DeviceCookieDomain
    Set-AppSetting -Xml $webConfig -Key "Gateway.DeviceCookieSecurePolicy" -Value $DeviceCookieSecurePolicy
    Set-AppSetting -Xml $webConfig -Key "Gateway.BrowserDevice.ReviewOnSignalChange" -Value ([string]$BrowserDeviceReviewOnSignalChange)
    Set-AppSetting -Xml $webConfig -Key "Gateway.BrowserDevice.SignalHeaders" -Value ((Normalize-StringList -Values $BrowserDeviceSignalHeaders) -join ",")
    Set-AppSetting -Xml $webConfig -Key "Gateway.ExternalScheme" -Value $ExternalScheme
    Set-AppSetting -Xml $webConfig -Key "Gateway.UpstreamProxyUrl" -Value $UpstreamProxyUrl
    Set-AppSetting -Xml $webConfig -Key "Gateway.TrustedProxyAddresses" -Value ((Normalize-StringList -Values $TrustedProxyAddresses) -join ",")
    Set-AppSetting -Xml $webConfig -Key "Gateway.TrustedProxyCidrs" -Value ((Normalize-StringList -Values $TrustedProxyCidrs) -join ",")
    Save-XmlUtf8 -Xml $webConfig -Path $gatewayWebConfig
}

$activeUpstreamBaseUrl = $UpstreamBaseUrl
$activeEntryPath = $GatewayEntryPath
$usingMockUpstream = [string]::IsNullOrWhiteSpace($activeUpstreamBaseUrl)
if (-not $SkipGatewaySiteConfig) {
    if ($usingMockUpstream) {
        $activeUpstreamBaseUrl = "http://${DisplayHost}:${BackendPort}/mock/erp-main/"
        if ([string]::IsNullOrWhiteSpace($activeEntryPath)) {
            $activeEntryPath = "portal"
        }
    }
    elseif ([string]::IsNullOrWhiteSpace($activeEntryPath)) {
        $activeEntryPath = "default.aspx"
    }
    if ($GatewayHostNames.Count -eq 0) {
        $GatewayHostNames = @("${DisplayHost}:${GatewayPort}")
    }
    else {
        $GatewayHostNames = Add-UniqueString -Values $GatewayHostNames -Value "${DisplayHost}:${GatewayPort}"
    }
    foreach ($additionalPort in $AdditionalGatewayPorts) {
        if ($additionalPort -gt 0) {
            $GatewayHostNames = Add-UniqueString -Values $GatewayHostNames -Value "${DisplayHost}:${additionalPort}"
        }
    }

    $sitesConfigPath = Join-Path $GatewaySitePath "App_Data\gateway-sites.json"
    Write-GatewaySitesConfig -Path $sitesConfigPath -SiteUpstreamBaseUrl $activeUpstreamBaseUrl -SiteEntryPath $activeEntryPath -ConfiguredSiteKey $SiteKey -SiteHostNames $GatewayHostNames -SiteAnonymousAllowedPaths $AnonymousAllowedPaths -SitePathRuleProfiles $PathRuleProfiles -SiteExternalApiAllowedPathPatterns $ExternalApiAllowedPathPatterns -SiteLegacyAppMobileUserAgentMarkers $LegacyAppMobileUserAgentMarkers -SiteRedirectToUpstream ([bool]$RedirectToUpstream)
}

Write-Host ("Gateway site: {0}" -f $GatewaySiteName)
Write-Host ("  AppPool:      {0}" -f $GatewayAppPoolName)
Write-Host ("  Public entry: http://{0}:{1}/gateway" -f $DisplayHost, $GatewayPort)
Write-Host ("  Admin entry:  http://{0}:{1}/admin" -f $DisplayHost, $AdminPort)
Write-Host ("Backend site: {0}" -f $BackendSiteName)
Write-Host ("  AppPool:          {0}" -f $BackendAppPoolName)
Write-Host ("  Local mock entry: http://{0}:{1}/mock/erp-main/portal" -f $DisplayHost, $BackendPort)
if (-not $SkipGatewaySiteConfig) {
    Write-Host "Gateway upstream config:"
    Write-Host ("  UpstreamBaseUrl: {0}" -f (Normalize-Url $activeUpstreamBaseUrl))
    Write-Host ("  EntryPath:       {0}" -f $activeEntryPath)
    Write-Host ("  HostNames:       {0}" -f ($GatewayHostNames -join ", "))
    Write-Host ("  ExternalScheme:  {0}" -f $ExternalScheme)
    Write-Host ("  Cookie secure:   {0}" -f $DeviceCookieSecurePolicy)
    Write-Host ("  Device review:   SignalChange={0}; Headers={1}" -f $BrowserDeviceReviewOnSignalChange, ((Normalize-StringList -Values $BrowserDeviceSignalHeaders) -join ", "))
    Write-Host ("  Upstream proxy:  {0}" -f $UpstreamProxyUrl)
    Write-Host ("  Trusted proxies: {0}" -f (((Normalize-StringList -Values $TrustedProxyAddresses) + (Normalize-StringList -Values $TrustedProxyCidrs)) -join ", "))
}
Write-Host ("  Gateway business entry: http://{0}:{1}/{2}" -f $DisplayHost, $GatewayPort, $activeEntryPath)
Write-Host ("  Legacy proxy fallback:  http://{0}:{1}/proxy/{2}/{3}" -f $DisplayHost, $GatewayPort, $SiteKey, $activeEntryPath)
