# check-local-demo-prereqs.ps1
# Local demo site prerequisite check - outputs what's still needed for integration.
#
# Checks:
#   1. IIS / appcmd.exe existence
#   2. Local demo site directory existence
#   3. Database backup files existence
#   4. KpScheduleService.exe existence
#   5. Legacy gateway config (public vs local)
#   6. SQL Server existence
#   7. .NET Framework 4.7.2
#   8. Sentinel driver
#
# This script performs NO installation, only reports status.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\legacy\check-local-demo-prereqs.ps1

param()

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Locate key paths
$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if (-not $scriptPath) {
        $scriptRoot = Join-Path $PWD "scripts\legacy"
    } else {
        $scriptRoot = Split-Path -Parent $scriptPath
    }
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)

$demoBase = Join-Path $repoRoot (Join-Path "演示站点20260330" "演示站点20260330")
$webDir = Join-Path (Join-Path $demoBase "web") "701日构建20260330"
$dbDir = Join-Path $demoBase "数据库"
$kpServiceDir = Join-Path $demoBase "KpService"

$script:pass = 0
$script:fail = 0
$script:warn = 0

function Write-Check {
    param(
        [string]$Category,
        [string]$Item,
        [string]$Status,
        [string]$Detail
    )
    $prefix = ""
    if ($Status -eq "PASS") { $prefix = "PASS  " }
    elseif ($Status -eq "FAIL") { $prefix = "FAIL  " }
    else { $prefix = "WARN  " }
    Write-Host ($prefix + "[" + $Category + "] " + $Item + " -- " + $Detail)
    if ($Status -eq "PASS") { $script:pass++ }
    elseif ($Status -eq "FAIL") { $script:fail++ }
    else { $script:warn++ }
}

Write-Host "============================================"
Write-Host " Local Demo Site Prerequisite Check Report"
Write-Host (" Check time : " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Write-Host (" Repo root   : $repoRoot")
Write-Host "============================================"
Write-Host ""

# ---- 1. IIS ----
Write-Host "--- IIS ---"

$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    Write-Check "IIS" "appcmd.exe" "PASS" "Found: $appcmd"
    try {
        $sitesOutput = & $appcmd list site 2>&1
        if ($LASTEXITCODE -eq 0 -and $sitesOutput) {
            $siteList = $sitesOutput -join "; "
            Write-Check "IIS" "Registered sites" "PASS" $siteList
        } else {
            Write-Check "IIS" "Registered sites" "WARN" "IIS installed but no sites or empty response"
        }
    } catch {
        Write-Check "IIS" "Registered sites" "WARN" "Cannot list sites (may need admin rights)"
    }
} else {
    Write-Check "IIS" "appcmd.exe" "FAIL" "Not found - please enable IIS and install ASP.NET 4.x"
}

$aspnet40 = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\aspnet_regiis.exe"
if (Test-Path $aspnet40) {
    Write-Check "IIS" "ASP.NET 4.x" "PASS" "Found: $aspnet40"
} else {
    Write-Check "IIS" "ASP.NET 4.x" "FAIL" "aspnet_regiis.exe not found"
}

Write-Host ""

# ---- 2. Demo site directory ----
Write-Host "--- Demo Site Directory ---"

if (Test-Path $demoBase) {
    Write-Check "Dir" "Demo site root" "PASS" $demoBase
} else {
    Write-Check "Dir" "Demo site root" "FAIL" "Not found: $demoBase"
}

if (Test-Path $webDir) {
    $webConfig = Join-Path $webDir "Web.config"
    if (Test-Path $webConfig) {
        Write-Check "Dir" "Web site files" "PASS" "Found Web.config in: $webDir"
    } else {
        Write-Check "Dir" "Web site files" "WARN" "Dir exists but no Web.config: $webDir"
    }
} else {
    Write-Check "Dir" "Web site files" "FAIL" "Not found: $webDir"
}

$kpwebConfig = Join-Path (Join-Path $demoBase "web") "kpweb.config"
if (Test-Path $kpwebConfig) {
    Write-Check "Dir" "kpweb.config" "PASS" $kpwebConfig
} else {
    Write-Check "Dir" "kpweb.config" "WARN" "Not found: $kpwebConfig"
}

Write-Host ""

# ---- 3. Database backups ----
Write-Host "--- Database Backups ---"

if (Test-Path $dbDir) {
    Write-Check "DB" "Database directory" "PASS" $dbDir
    $bak7E = Get-ChildItem -Path $dbDir -Filter "7E2088*.bak" -ErrorAction SilentlyContinue
    $bakAnnex = Get-ChildItem -Path $dbDir -Filter "7E2088_Annex*.bak" -ErrorAction SilentlyContinue
    if ($bak7E) {
        $info = ""
        foreach ($f in $bak7E) {
            $sizeMB = [math]::Round($f.Length / 1MB, 1)
            $mb = $sizeMB.ToString(); $info += ($f.Name + " (" + $mb + " MB); ")
        }
        Write-Check "DB" "7E2088 backup" "PASS" $info
    } else {
        Write-Check "DB" "7E2088 backup" "FAIL" "No 7E2088*.bak found in $dbDir"
    }
    if ($bakAnnex) {
        $info = ""
        foreach ($f in $bakAnnex) {
            $sizeMB = [math]::Round($f.Length / 1MB, 1)
            $mb = $sizeMB.ToString(); $info += ($f.Name + " (" + $mb + " MB); ")
        }
        Write-Check "DB" "7E2088_Annex backup" "PASS" $info
    } else {
        Write-Check "DB" "7E2088_Annex backup" "FAIL" "No 7E2088_Annex*.bak found in $dbDir"
    }
} else {
    Write-Check "DB" "Database directory" "FAIL" "Not found: $dbDir"
}

Write-Host ""

# ---- 4. SQL Server ----
Write-Host "--- SQL Server ---"

$sqlServiceNames = @("MSSQLSERVER", "MSSQL`$SQLEXPRESS")
$foundSql = $false
foreach ($svcName in $sqlServiceNames) {
    $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
    if ($svc) {
        $foundSql = $true
        $s = if ($svc.Status -eq "Running") { "PASS" } else { "WARN" }
        Write-Check "SQL" ("Service " + $svc.Name) $s ("Status: " + $svc.Status)
    }
}
if (-not $foundSql) {
    Write-Check "SQL" "SQL Server service" "FAIL" "No SQL Server service found - may not be installed"
}

$sqlcmd = Get-Command "sqlcmd" -ErrorAction SilentlyContinue
if ($sqlcmd) {
    Write-Check "SQL" "sqlcmd" "PASS" ("Found: " + $sqlcmd.Source)
} else {
    Write-Check "SQL" "sqlcmd" "WARN" "sqlcmd not found, some operations may be limited"
}

$sqlRegPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"
if (Test-Path $sqlRegPath) {
    try {
        $instances = Get-ItemProperty -Path $sqlRegPath -ErrorAction SilentlyContinue
        $instanceNames = @()
        foreach ($prop in $instances.PSObject.Properties) {
            if ($prop.Name -notmatch "^PS") {
                $instanceNames += ($prop.Name + "=" + $prop.Value)
            }
        }
        if ($instanceNames.Count -gt 0) {
            Write-Check "SQL" "Registered instances" "PASS" ($instanceNames -join ", ")
        } else {
            Write-Check "SQL" "Registered instances" "WARN" "Registry path exists but no instances found"
        }
    } catch {
        Write-Check "SQL" "Registered instances" "WARN" "Cannot read registry (may need admin rights)"
    }
} else {
    Write-Check "SQL" "Registered instances" "FAIL" "No SQL Server instances in registry"
}

Write-Host ""

# ---- 5. KpService ----
Write-Host "--- KpScheduleService ---"

$kpExe = Join-Path $kpServiceDir "KpScheduleService.exe"
if (Test-Path $kpExe) {
    Write-Check "KpSvc" "KpScheduleService.exe" "PASS" $kpExe
} else {
    Write-Check "KpSvc" "KpScheduleService.exe" "FAIL" "Not found: $kpExe"
}

$kpConfig = Join-Path $kpServiceDir "KpScheduleService.exe.config"
if (Test-Path $kpConfig) {
    Write-Check "KpSvc" "Service config" "PASS" $kpConfig
} else {
    Write-Check "KpSvc" "Service config" "FAIL" "Not found: $kpConfig"
}

$installBat = Join-Path $kpServiceDir "install.bat"
if (Test-Path $installBat) {
    Write-Check "KpSvc" "install.bat" "PASS" $installBat
} else {
    Write-Check "KpSvc" "install.bat" "WARN" "Not found: $installBat"
}

Write-Host ""

# ---- 6. Sentinel ----
Write-Host "--- Sentinel Dongle ---"

$haspDriver = Get-Service -Name "hasplms" -ErrorAction SilentlyContinue
if ($haspDriver) {
    $s = if ($haspDriver.Status -eq "Running") { "PASS" } else { "WARN" }
    Write-Check "Dongle" "Sentinel driver" $s ("Status: " + $haspDriver.Status)
} else {
    Write-Check "Dongle" "Sentinel driver" "FAIL" "hasplms service not found - Sentinel driver may not be installed"
}

try {
    $accResponse = Invoke-WebRequest -Uri "http://localhost:1947" -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
    Write-Check "Dongle" "Sentinel ACC" "PASS" "http://localhost:1947 accessible"
} catch {
    Write-Check "Dongle" "Sentinel ACC" "WARN" "http://localhost:1947 not accessible (driver may not be running)"
}

Write-Host ""

# ---- 7. .NET Framework ----
Write-Host "--- .NET Framework ---"

$net472Release = 461808
$netReleasePath = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
if (Test-Path $netReleasePath) {
    try {
        $release = (Get-ItemProperty -Path $netReleasePath -Name Release -ErrorAction Stop).Release
        if ($release -ge $net472Release) {
            Write-Check "NET" ".NET Framework 4.7.2+" "PASS" ("Release=" + $release + " (>= " + $net472Release + ")")
        } else {
            Write-Check "NET" ".NET Framework 4.7.2+" "FAIL" ("Release=" + $release + " (< required " + $net472Release + ")")
        }
    } catch {
        Write-Check "NET" ".NET Framework 4.7.2+" "WARN" "Cannot read Release value"
    }
} else {
    Write-Check "NET" ".NET Framework 4.7.2+" "FAIL" ".NET Framework 4.x not found in registry"
}

Write-Host ""

# ---- 8. Legacy gateway config ----
Write-Host "--- Legacy Gateway Config ---"

$sitesFile = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.json"
if (Test-Path $sitesFile) {
    Write-Check "GW" "gateway-sites.json" "PASS" $sitesFile
    try {
        $sitesJson = Get-Content -Path $sitesFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $erpMain = $sitesJson | Where-Object { $_.Key -eq "erp-main" }
        if ($erpMain) {
            $upstream = $erpMain.UpstreamBaseUrl
            if ($upstream -match "localhost|127\.0\.0\.1") {
                Write-Check "GW" "Current upstream" "PASS" ("Local demo site: " + $upstream)
            } elseif ($upstream -match "j\.kuaipu\.com\.cn") {
                Write-Check "GW" "Current upstream" "WARN" ("Still on public experience site profile, not yet switched to local-demo: " + $upstream)
            } else {
                Write-Check "GW" "Current upstream" "WARN" ("Not a local demo address: " + $upstream)
            }
        }
    } catch {
        Write-Check "GW" "gateway-sites.json" "WARN" ("Cannot parse: " + $_.ToString())
    }
} else {
    Write-Check "GW" "gateway-sites.json" "FAIL" "Not found: $sitesFile"
}

$profileMarker = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.profile.txt"
if (Test-Path $profileMarker) {
    $profileName = (Get-Content -Path $profileMarker -TotalCount 1 -ErrorAction SilentlyContinue).Trim()
    Write-Check "GW" "Current profile" "PASS" $profileName
} else {
    Write-Check "GW" "Current profile" "WARN" "No profile marker file (set-gateway-profile.ps1 may not have been used yet)"
}

$expTemplate = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.experience.json"
$localTemplate = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\App_Data\gateway-sites.local-demo.template.json"
if (Test-Path $expTemplate) {
    Write-Check "GW" "experience template" "PASS" "In place"
} else {
    Write-Check "GW" "experience template" "WARN" "Not found"
}
if (Test-Path $localTemplate) {
    Write-Check "GW" "local-demo template" "PASS" "In place"
} else {
    Write-Check "GW" "local-demo template" "WARN" "Not found"
}

Write-Host ""

# ---- 9. Legacy build ----
Write-Host "--- Legacy Gateway Build ---"

$legacySln = Join-Path $repoRoot "GatewayDemo.Legacy.sln"
if (Test-Path $legacySln) {
    Write-Check "Build" "GatewayDemo.Legacy.sln" "PASS" $legacySln
} else {
    Write-Check "Build" "GatewayDemo.Legacy.sln" "FAIL" "Not found"
}

$legacyBin = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web\bin\GatewayDemo.Legacy.Web.dll"
if (Test-Path $legacyBin) {
    Write-Check "Build" "Compiled bin" "PASS" $legacyBin
} else {
    Write-Check "Build" "Compiled bin" "WARN" "No compiled output found - may need to build first"
}

Write-Host ""

# ---- Summary ----
Write-Host "============================================"
Write-Host " Summary"
Write-Host ("   PASS : " + $script:pass)
Write-Host ("   FAIL : " + $script:fail)
Write-Host ("   WARN : " + $script:warn)
Write-Host "============================================"

if ($script:fail -gt 0) {
    Write-Host ""
    Write-Host ("Still " + $script:fail + " FAIL item(s). Please resolve before deployment.")
    Write-Host "Common fixes:"
    Write-Host "  - Install IIS: Server Manager -> Add Roles -> Web Server (IIS)"
    Write-Host "  - Install ASP.NET 4.x: Check ASP.NET 4.x in the same wizard"
    Write-Host "  - Install SQL Server Express: Run installer from packages\sqlserver\"
    Write-Host "  - Register ASP.NET: Run aspnet_regiis -ir as admin"
}
