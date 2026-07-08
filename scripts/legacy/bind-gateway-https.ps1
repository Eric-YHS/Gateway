param(
    [string]$GatewaySiteName = "GatewayDemoLegacy",
    [string]$HostName = "",
    [int]$HttpsPort = 443,
    [string]$CertThumbprint = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [int]$SslFlags = -1
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Normalize-Thumbprint {
    param([string]$Value)
    if ($null -eq $Value) {
        $Value = ""
    }

    return ($Value -replace "\s", "").ToUpperInvariant()
}

function Convert-ThumbprintToBytes {
    param([string]$Thumbprint)

    $normalized = Normalize-Thumbprint $Thumbprint
    if ($normalized.Length -eq 0 -or $normalized.Length % 2 -ne 0) {
        throw "Certificate thumbprint is empty or invalid."
    }

    $bytes = New-Object byte[] ($normalized.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($normalized.Substring($i * 2, 2), 16)
    }

    return $bytes
}

function Import-IisAdministrationAssembly {
    $loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {
        $_.GetName().Name -eq "Microsoft.Web.Administration"
    } | Select-Object -First 1
    if ($loaded) {
        return
    }

    try {
        Add-Type -AssemblyName Microsoft.Web.Administration -ErrorAction Stop
        return
    }
    catch {
        $candidatePaths = @(
            (Join-Path $env:SystemRoot "System32\inetsrv\Microsoft.Web.Administration.dll"),
            (Join-Path $env:SystemRoot "Sysnative\inetsrv\Microsoft.Web.Administration.dll")
        )

        foreach ($candidatePath in $candidatePaths) {
            if (Test-Path -LiteralPath $candidatePath) {
                Add-Type -Path $candidatePath
                return
            }
        }

        throw "Microsoft.Web.Administration.dll was not found. Install IIS Management Console / IIS management tools, then run this script again."
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Please run this script as Administrator because IIS HTTPS bindings require elevated privileges."
}

if ($HttpsPort -le 0) {
    throw "HttpsPort must be greater than 0."
}

if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    $resolvedPfxPath = [System.IO.Path]::GetFullPath($PfxPath)
    if (-not (Test-Path $resolvedPfxPath)) {
        throw "PFX file not found: $resolvedPfxPath"
    }

    if ($null -eq $PfxPassword) {
        $PfxPassword = ""
    }

    $securePassword = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
    $imported = Import-PfxCertificate -FilePath $resolvedPfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $securePassword
    if (-not $imported) {
        throw "Failed to import PFX certificate."
    }

    $CertThumbprint = $imported.Thumbprint
}

$CertThumbprint = Normalize-Thumbprint $CertThumbprint
if ([string]::IsNullOrWhiteSpace($CertThumbprint)) {
    throw "Specify either -CertThumbprint for an existing LocalMachine\\My certificate or -PfxPath to import one."
}

$cert = Get-Item -LiteralPath ("Cert:\LocalMachine\My\" + $CertThumbprint) -ErrorAction SilentlyContinue
if (-not $cert) {
    throw "Certificate $CertThumbprint was not found in Cert:\LocalMachine\My."
}

Import-IisAdministrationAssembly
$serverManager = New-Object Microsoft.Web.Administration.ServerManager
$site = $serverManager.Sites[$GatewaySiteName]
if (-not $site) {
    throw "IIS site not found: $GatewaySiteName"
}

if ($null -eq $HostName) {
    $HostName = ""
}

$bindingHost = $HostName.Trim()
$bindingInformation = "*:${HttpsPort}:$bindingHost"
$binding = $site.Bindings | Where-Object {
    $_.Protocol -eq "https" -and $_.BindingInformation -eq $bindingInformation
} | Select-Object -First 1

if (-not $binding) {
    $binding = $site.Bindings.Add($bindingInformation, "https")
}

if ($SslFlags -lt 0) {
    $SslFlags = if ([string]::IsNullOrWhiteSpace($bindingHost)) { 0 } else { 1 }
}

$binding.CertificateStoreName = "My"
$binding.CertificateHash = Convert-ThumbprintToBytes $CertThumbprint
$binding.SslFlags = $SslFlags
$serverManager.CommitChanges()

Write-Host ("HTTPS binding updated for site: {0}" -f $GatewaySiteName)
Write-Host ("  Binding:     https/{0}" -f $bindingInformation)
Write-Host ("  Certificate: {0}" -f $CertThumbprint)
Write-Host ("  SslFlags:    {0}" -f $SslFlags)
