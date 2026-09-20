<#
.SYNOPSIS
    Phase 2 setup: Assumes IIS + SQL Server already installed.
    Restores DBs, creates IIS sites, installs KpService, switches gateway.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-local-demo-phase2.ps1
#>

param(
    [int]$DemoPort = 80,
    [int]$GatewayPort = 5050,
    [int]$AdminPort = 5051,
    [string]$KpmisPassword = $env:KP_DEMO_KPMIS_PASSWORD
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if (-not $scriptPath) { $scriptRoot = Join-Path $PWD "scripts\legacy" }
    else { $scriptRoot = Split-Path -Parent $scriptPath }
}
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$demoBase = Join-Path $repoRoot "演示站点20260330\演示站点20260330"
$webDir = Join-Path $demoBase "web\701日构建20260330"
$dbDir = Join-Path $demoBase "数据库"
$kpServiceDir = Join-Path $demoBase "KpService"
$gatewayWebDir = Join-Path $repoRoot "src\GatewayDemo.Legacy.Web"
$logDir = Join-Path $repoRoot "artifacts\local-demo-validation\2026-04-03"
$logFile = Join-Path $logDir "setup-phase2-log.txt"

if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }

function Log {
    param([string]$msg)
    $ts = Get-Date -Format "HH:mm:ss"
    $line = "$ts  $msg"
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Check-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Require-PasswordValue {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        Log "FAIL: $Name is required. Pass -$Name or set the matching environment variable."
        exit 1
    }
}

function Escape-SqlLiteral {
    param([string]$Value)
    return ($Value -replace "'", "''")
}

Log "========================================="
Log " Phase 2 Setup (IIS + SQL already done)"
Log " $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Log "========================================="

if (-not (Check-Admin)) {
    Log "FAIL: This script requires administrator privileges."
    exit 1
}
Log "OK: Running as administrator."
Require-PasswordValue -Name "KpmisPassword" -Value $KpmisPassword

# --- Pre-checks ---
Log ""
Log "--- Pre-checks ---"
$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) { Log "OK: IIS appcmd found." }
else { Log "FAIL: IIS not ready. Did you reboot after DISM install?"; exit 1 }

$sqlService = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
if ($sqlService) {
    Log "OK: SQL Server service found ($($sqlService.Status))."
    if ($sqlService.Status -ne 'Running') {
        Log "  Starting SQL Server..."
        Start-Service -Name 'MSSQLSERVER'
    }
} else {
    # Check for SQLEXPRESS named instance
    $sqlExpress = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
    if ($sqlExpress) {
        Log "OK: SQL Server Express (named instance) found ($($sqlExpress.Status))."
        if ($sqlExpress.Status -ne 'Running') {
            Log "  Starting SQL Server Express..."
            Start-Service -Name 'MSSQL$SQLEXPRESS'
        }
    } else {
        Log "FAIL: SQL Server not found. Please install SQL Server first."
        Log "  Download offline installer from:"
        Log "  https://go.microsoft.com/fwlink/?linkid=866658"
        exit 1
    }
}

# Find sqlcmd
$sqlcmd = Get-Command 'sqlcmd' -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $candidates = Get-ChildItem "C:\Program Files\Microsoft SQL Server" -Recurse -Filter "sqlcmd.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidates) { $sqlcmd = $candidates.FullName }
}
if (-not $sqlcmd) {
    $candidates = Get-ChildItem "C:\Program Files" -Recurse -Filter "SQLCMD.EXE" -ErrorAction SilentlyContinue -Depth 6 | Select-Object -First 1
    if ($candidates) { $sqlcmd = $candidates.FullName }
}
if (-not $sqlcmd) {
    Log "FAIL: sqlcmd not found. Please install SQL Server command-line tools."
    exit 1
}
Log "OK: sqlcmd found at $sqlcmd"

# Determine SQL instance name
$sqlInstance = "localhost"
$sqlNamedInstance = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
if ($sqlNamedInstance) {
    $sqlInstance = "localhost\SQLEXPRESS"
    Log "  Using named instance: $sqlInstance"
} else {
    Log "  Using default instance: $sqlInstance"
}

function Invoke-Sql {
    param([string]$Query)
    $result = & $sqlcmd -S $sqlInstance -E -Q $Query -h -1 -W 2>&1
    return $result
}

# ===========================
# Restore Databases
# ===========================
Log ""
Log "--- Restore Databases ---"

$sqlDataDir = "C:\SQLData"
if (-not (Test-Path $sqlDataDir)) {
    New-Item -ItemType Directory -Path $sqlDataDir -Force | Out-Null
    Log "Created $sqlDataDir."
}

function Restore-Db {
    param([string]$DbName, [string]$BakPath)

    $check = Invoke-Sql "SELECT 1 FROM sys.databases WHERE name='$DbName'"
    if ($check -match '1') {
        Log "WARN: $DbName already exists. Skipping."
        return
    }

    Log "Restoring $DbName from $BakPath ..."

    # Get logical file names
    $fileListRaw = & $sqlcmd -S $sqlInstance -E -Q "RESTORE FILELISTONLY FROM DISK='$BakPath'" -W 2>&1
    Log "  FILELISTONLY raw output captured."

    # Build MOVE clauses by parsing the output
    $moveClauses = @()
    foreach ($line in $fileListRaw) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        $parts = $trimmed -split '\s+'
        if ($parts.Count -ge 3) {
            $logicalName = $parts[0]
            $fileType = $parts[2]
            if ($fileType -eq 'D') {
                $target = Join-Path $sqlDataDir "$DbName.mdf"
                $moveClauses += "MOVE '$logicalName' TO '$target'"
            } elseif ($fileType -eq 'L') {
                $target = Join-Path $sqlDataDir "$DbName`_log.ldf"
                $moveClauses += "MOVE '$logicalName' TO '$target'"
            }
        }
    }

    if ($moveClauses.Count -eq 0) {
        Log "  WARN: Could not parse file list. Trying RESTORE without MOVE."
        $sql = "RESTORE DATABASE [$DbName] FROM DISK='$BakPath' WITH REPLACE"
    } else {
        $moveStr = $moveClauses -join ', '
        $sql = "RESTORE DATABASE [$DbName] FROM DISK='$BakPath' WITH REPLACE, $moveStr"
    }

    Log "  Executing restore..."
    $result = Invoke-Sql $sql
    Log "  Result: $result"
}

$bak7E = Join-Path $dbDir "7E2088-20260330.bak"
$bakAnnex = Join-Path $dbDir "7E2088_Annex-20260330.bak"
Restore-Db -DbName "7E2088" -BakPath $bak7E
Restore-Db -DbName "7E2088_Annex" -BakPath $bakAnnex

# Verify
$check7E = Invoke-Sql "SELECT name FROM sys.databases WHERE name='7E2088'"
$checkAnnex = Invoke-Sql "SELECT name FROM sys.databases WHERE name='7E2088_Annex'"
if ($check7E -match '7E2088') { Log "OK: 7E2088 restored." } else { Log "FAIL: 7E2088 not found after restore." }
if ($checkAnnex -match '7E2088_Annex') { Log "OK: 7E2088_Annex restored." } else { Log "FAIL: 7E2088_Annex not found after restore." }

# ===========================
# Create KPMIS login
# ===========================
Log ""
Log "--- Create KPMIS login ---"
$null = Invoke-Sql ("IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'KPMIS') CREATE LOGIN [KPMIS] WITH PASSWORD = '" + (Escape-SqlLiteral $KpmisPassword) + "', DEFAULT_DATABASE = [7E2088], CHECK_POLICY = OFF;")
Log "  Login created or already exists."

# Grant db_owner on 7E2088
$null = Invoke-Sql "USE [7E2088]; IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'KPMIS') BEGIN CREATE USER [KPMIS] FOR LOGIN [KPMIS]; ALTER ROLE [db_owner] ADD MEMBER [KPMIS]; END"
Log "  User mapped in 7E2088."

# Grant db_owner on 7E2088_Annex
$null = Invoke-Sql "USE [7E2088_Annex]; IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = 'KPMIS') BEGIN CREATE USER [KPMIS] FOR LOGIN [KPMIS]; ALTER ROLE [db_owner] ADD MEMBER [KPMIS]; END"
Log "  User mapped in 7E2088_Annex."

# Quick test
$testResult = Invoke-Sql "SELECT TOP 1 name FROM [7E2088].sys.tables"
if ($testResult) { Log "OK: Can read from 7E2088." }
else { Log "WARN: Could not verify read access." }

# ===========================
# Deploy dongle DLLs
# ===========================
Log ""
Log "--- Deploy dongle DLLs ---"
$deployBat = Join-Path $webDir "部署软狗.bat"
if (Test-Path $deployBat) {
    Push-Location $webDir
    cmd /c "部署软狗.bat" 2>&1 | ForEach-Object { Log "  $_" }
    Pop-Location
    Log "OK: Dongle DLLs deployed."
} else {
    Log "WARN: 部署软狗.bat not found."
}

# ===========================
# Create IIS site for demo
# ===========================
Log ""
Log "--- Create IIS demo site (KpLocalDemoRaw on port $DemoPort) ---"

# Remove existing if ours
$existing = & $appcmd list site "KpLocalDemoRaw" 2>&1
if ($existing -match 'KpLocalDemoRaw') {
    & $appcmd delete site /site.name:"KpLocalDemoRaw" 2>&1 | Out-Null
    Log "  Removed existing KpLocalDemoRaw."
}
$existingPool = & $appcmd list apppool "KpLocalDemoRawPool" 2>&1
if ($existingPool -match 'KpLocalDemoRawPool') {
    & $appcmd delete apppool /apppool.name:"KpLocalDemoRawPool" 2>&1 | Out-Null
}

& $appcmd add apppool /name:"KpLocalDemoRawPool" /managedRuntimeVersion:v4.0 /managedPipelineMode:Integrated 2>&1 | Out-Null
& $appcmd add site /name:"KpLocalDemoRaw" /bindings:"http/*:${DemoPort}:" /physicalPath:$webDir 2>&1 | Out-Null
& $appcmd set site /site.name:"KpLocalDemoRaw" /[path='/'].applicationPool:"KpLocalDemoRawPool" 2>&1 | Out-Null
Log "OK: KpLocalDemoRaw site created."
Log "    Port: $DemoPort"
Log "    Path: $webDir"
Log "    Pool: KpLocalDemoRawPool (v4.0, Integrated)"

# Set App_Data permissions for gateway
$gwAppData = Join-Path $gatewayWebDir "App_Data"
if (Test-Path $gwAppData) {
    $acl = Get-Acl $gwAppData
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl $gwAppData $acl
    Log "OK: Gateway App_Data write permissions set."
}

# ===========================
# Install KpScheduleService
# ===========================
Log ""
Log "--- Install KpScheduleService ---"
$kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
if ($kpSvc) {
    Log "KpService already installed ($($kpSvc.Status))."
    if ($kpSvc.Status -ne 'Running') {
        Start-Service -Name 'KpService' -ErrorAction SilentlyContinue
    }
} else {
    $installUtil = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe"
    Push-Location $kpServiceDir
    & $installUtil ".\KpScheduleService.exe" 2>&1 | ForEach-Object { Log "  $_" }
    Pop-Location

    Start-Sleep -Seconds 3
    $kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
    if ($kpSvc) {
        Log "OK: KpService installed. Starting..."
        Start-Service -Name 'KpService' -ErrorAction SilentlyContinue
    } else {
        Log "WARN: KpService not found after install."
    }
}

Start-Sleep -Seconds 2
$kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
if ($kpSvc -and $kpSvc.Status -eq 'Running') {
    Log "OK: KpService running."
} elseif ($kpSvc) {
    Log "WARN: KpService installed but not running: $($kpSvc.Status)"
} else {
    Log "WARN: KpService not installed."
}

# ===========================
# Create gateway IIS site
# ===========================
Log ""
Log "--- Create gateway IIS site (ports $GatewayPort / $AdminPort) ---"
$existingGw = & $appcmd list site "GatewayDemoLegacy" 2>&1
if ($existingGw -match 'GatewayDemoLegacy') {
    & $appcmd delete site /site.name:"GatewayDemoLegacy" 2>&1 | Out-Null
}
$existingGwPool = & $appcmd list apppool "GatewayDemoLegacyPool" 2>&1
if ($existingGwPool -match 'GatewayDemoLegacyPool') {
    & $appcmd delete apppool /apppool.name:"GatewayDemoLegacyPool" 2>&1 | Out-Null
}

& $appcmd add apppool /name:"GatewayDemoLegacyPool" /managedRuntimeVersion:v4.0 /managedPipelineMode:Integrated 2>&1 | Out-Null
& $appcmd add site /name:"GatewayDemoLegacy" /bindings:"http/*:${GatewayPort}:,http/*:${AdminPort}:" /physicalPath:$gatewayWebDir 2>&1 | Out-Null
& $appcmd set site /site.name:"GatewayDemoLegacy" /[path='/'].applicationPool:"GatewayDemoLegacyPool" 2>&1 | Out-Null
Log "OK: GatewayDemoLegacy site created."
Log "    Business port: $GatewayPort"
Log "    Admin port: $AdminPort"
Log "    Path: $gatewayWebDir"

# ===========================
# Switch gateway to local-demo
# ===========================
Log ""
Log "--- Switch gateway to local-demo ---"
$switchScript = Join-Path $scriptRoot "set-gateway-profile.ps1"
$demoUrl = "http://localhost:${DemoPort}/"
if (Test-Path $switchScript) {
    & $switchScript -Profile "local-demo" -DemoBaseUrl $demoUrl 2>&1 | ForEach-Object { Log "  $_" }
} else {
    Log "WARN: set-gateway-profile.ps1 not found at $switchScript"
}

# ===========================
# Summary
# ===========================
Log ""
Log "========================================="
Log " Phase 2 Complete!"
Log "========================================="
Log ""
Log ("  Demo site:       http://localhost:" + $DemoPort + "/default.aspx")
Log ("  Gateway public:  http://localhost:" + $GatewayPort + "/gateway")
Log ("  Gateway admin:   http://localhost:" + $AdminPort + "/admin")
Log ("  Gateway proxy:   http://localhost:" + $GatewayPort + "/proxy/erp-main/default.aspx")
Log ""
Log "  Log: $logFile"
Log ""
Log "  Next: Open browser to verify demo site."
