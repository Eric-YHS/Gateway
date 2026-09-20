<#
.SYNOPSIS
    One-shot admin setup: IIS + SQL Server + DB restore + demo site + KpService + Gateway

.DESCRIPTION
    MUST be run in an elevated PowerShell.

    Steps:
    1. Install IIS features
    2. Register ASP.NET 4.x
    3. Install SQL Server 2022 Express (default instance)
    4. Restore databases 7E2088 + 7E2088_Annex
    5. Create KPMIS login
    6. Run 部署软狗.bat
    7. Create IIS site KpLocalDemoRaw on port 80
    8. Install KpScheduleService
    9. Create IIS site GatewayDemoLegacy on ports 5050/5051
    10. Switch gateway to local-demo profile

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\legacy\setup-local-demo-env.ps1
#>

param(
    [int]$DemoPort = 80,
    [int]$GatewayPort = 5050,
    [int]$AdminPort = 5051,
    [string]$SqlSaPassword = $env:KP_DEMO_SQL_SA_PASSWORD,
    [string]$KpmisPassword = $env:KP_DEMO_KPMIS_PASSWORD
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# --- Repo root detection ---
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
$logFile = Join-Path $logDir "setup-log.txt"

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

    if ($Value.Contains('"')) {
        Log "FAIL: $Name must not contain double quote characters."
        exit 1
    }
}

function Escape-SqlLiteral {
    param([string]$Value)
    return ($Value -replace "'", "''")
}

Log "========================================="
Log " Local Demo Environment Setup"
Log " $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Log "========================================="

# --- Admin check ---
if (-not (Check-Admin)) {
    Log "FAIL: This script requires administrator privileges."
    Log "Please re-run in an elevated PowerShell."
    exit 1
}
Log "OK: Running as administrator."
Require-PasswordValue -Name "KpmisPassword" -Value $KpmisPassword

# ===========================
# Phase 1: Install IIS
# ===========================
Log ""
Log "--- Phase 1: Install IIS ---"
$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    Log "OK: IIS already installed."
} else {
    Log "Installing IIS via DISM..."
    $iisFeatures = @(
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-StaticContent",
        "IIS-DefaultDocument",
        "IIS-DirectoryBrowsing",
        "IIS-HttpErrors",
        "IIS-ApplicationDevelopment",
        "IIS-ASPNET",
        "IIS-NetFxExtensibility",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-HealthAndDiagnostics",
        "IIS-HttpLogging",
        "IIS-RequestMonitor",
        "IIS-Security",
        "IIS-RequestFiltering",
        "IIS-Performance",
        "IIS-HttpCompressionStatic",
        "IIS-WebServerManagementTools",
        "IIS-ManagementConsole"
    )
    foreach ($feature in $iisFeatures) {
        Log "  Enabling $feature..."
        $null = dism /online /enable-feature /featurename:$feature /all /NoRestart 2>&1
    }
    if (Test-Path $appcmd) {
        Log "OK: IIS installed."
    } else {
        Log "WARN: IIS may need a reboot. appcmd still not found."
    }
}

# Register ASP.NET 4.x
Log "Registering ASP.NET 4.x..."
$aspnetRegiis = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\aspnet_regiis.exe"
if (Test-Path $aspnetRegiis) {
    & $aspnetRegiis -ir 2>&1 | ForEach-Object { Log "  $_" }
    Log "OK: ASP.NET registered."
} else {
    Log "FAIL: aspnet_regiis.exe not found."
}

# ===========================
# Phase 2: Install SQL Server
# ===========================
Log ""
Log "--- Phase 2: Install SQL Server ---"
$sqlService = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
if ($sqlService) {
    Log "OK: SQL Server already installed (status: $($sqlService.Status))."
    if ($sqlService.Status -ne 'Running') {
        Log "  Starting SQL Server..."
        Start-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
    }
} else {
    Require-PasswordValue -Name "SqlSaPassword" -Value $SqlSaPassword
    $sqlInstaller = Get-ChildItem (Join-Path $repoRoot "packages\sqlserver") -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sqlInstaller) {
        Log "FAIL: SQL Server installer not found in packages\sqlserver\"
        Log "Please download SQL Server 2022 Express and place it there."
        exit 1
    }

    Log "Found installer: $($sqlInstaller.FullName)"
    Log "Installing SQL Server 2022 Express (default instance)..."

    # SQL Server Express silent install - default instance
    $installArgs = '/Q /ACTION=Install /FEATURES=SQLEngine /INSTANCENAME=MSSQLSERVER ' + `
        '/SQLSVCSTARTUPTYPE=Automatic /SQLSVCACCOUNT="NT Service\MSSQLSERVER" ' + `
        '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /SECURITYMODE=SQL /SAPWD="' + $SqlSaPassword + '" ' + `
        '/TCPENABLED=1 /NPENABLED=1 /IACCEPTSQLSERVERLICENSETERMS ' + `
        '/UPDATEENABLED=0 /SQLTEMPDBDIR="C:\SQLData\TempDB" /INSTALLSQLDATADIR="C:\SQLData"'

    $proc = Start-Process -FilePath $sqlInstaller.FullName -ArgumentList $installArgs -Wait -PassThru -NoNewWindow
    Log "SQL Server install exit code: $($proc.ExitCode)"

    if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) {
        Log "WARN: SQL Server install returned non-zero exit code. Checking if service exists..."
    }

    $sqlService = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
    if ($sqlService) {
        Log "OK: SQL Server service found."
        if ($sqlService.Status -ne 'Running') {
            Log "  Starting SQL Server..."
            Start-Service -Name 'MSSQLSERVER'
        }
    } else {
        Log "FAIL: SQL Server service not found after installation."
        Log "You may need to install SQL Server manually."
        Log "After installing, re-run this script to continue."
        exit 1
    }
}

# ===========================
# Phase 3: Restore Databases
# ===========================
Log ""
Log "--- Phase 3: Restore Databases ---"

# Find sqlcmd or use sqlcmd from SQL Server path
$sqlcmd = Get-Command 'sqlcmd' -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $sqlcmdPath = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Binn\sqlcmd.exe"
    if (-not (Test-Path $sqlcmdPath)) {
        $sqlcmdPath = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
    }
    if (Test-Path $sqlcmdPath) {
        $sqlcmd = $sqlcmdPath
    }
}

# Try to find sqlcmd in common locations
if (-not $sqlcmd) {
    $candidates = Get-ChildItem "C:\Program Files\Microsoft SQL Server" -Recurse -Filter "sqlcmd.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidates) { $sqlcmd = $candidates.FullName }
}

if (-not $sqlcmd) {
    Log "FAIL: sqlcmd not found. Cannot restore databases."
    Log "Please install SQL Server command-line tools."
    Log "You can restore manually:"
    Log "  RESTORE DATABASE [7E2088] FROM DISK = '$dbDir\7E2088-20260330.bak'"
    Log "  RESTORE DATABASE [7E2088_Annex] FROM DISK = '$dbDir\7E2088_Annex-20260330.bak'"
    exit 1
}

Log "Using sqlcmd: $sqlcmd"

function Invoke-Sql {
    param([string]$Query)
    $result = & $sqlcmd -S localhost -E -Q $Query -h -1 -W 2>&1
    return $result
}

# Ensure restore target directory exists
$sqlDataDir = "C:\SQLData"
if (-not (Test-Path $sqlDataDir)) {
    New-Item -ItemType Directory -Path $sqlDataDir -Force | Out-Null
    Log "Created $sqlDataDir for database files."
}

function Restore-DatabaseFromBackup {
    param(
        [string]$DbName,
        [string]$BakPath
    )
    # Check if database already exists
    $check = Invoke-Sql "SELECT 1 FROM sys.databases WHERE name='$DbName'"
    if ($check -match '1') {
        Log "WARN: Database $DbName already exists. Skipping restore."
        return
    }

    Log "Getting logical file names from backup: $BakPath"
    $fileListResult = & $sqlcmd -S localhost -E -Q "RESTORE FILELISTONLY FROM DISK='$BakPath'" -h -1 -W 2>&1
    Log "  FILELISTONLY output: $($fileListResult -join ' | ')"

    # Parse logical names - look for rows and type D/L
    $moveClauses = @()
    $lines = $fileListResult -split "`n"
    foreach ($line in $lines) {
        $parts = $line.Trim() -split '\s+'
        if ($parts.Count -ge 2) {
            $logicalName = $parts[0]
            $fileType = $parts[1]
            if ($fileType -eq 'D' -or $fileType -eq 'S') {
                $targetFile = Join-Path $sqlDataDir "$DbName.mdf"
                $moveClauses += "MOVE '$logicalName' TO '$targetFile'"
            } elseif ($fileType -eq 'L') {
                $targetFile = Join-Path $sqlDataDir "$DbName`_log.ldf"
                $moveClauses += "MOVE '$logicalName' TO '$targetFile'"
            }
        }
    }

    if ($moveClauses.Count -eq 0) {
        Log "WARN: Could not parse FILELISTONLY. Trying restore without MOVE..."
        $restoreSql = "RESTORE DATABASE [$DbName] FROM DISK='$BakPath' WITH REPLACE"
    } else {
        $moveStr = $moveClauses -join ', '
        $restoreSql = "RESTORE DATABASE [$DbName] FROM DISK='$BakPath' WITH REPLACE, $moveStr"
    }

    Log "Restoring $DbName..."
    Log "  SQL: $restoreSql"
    $result = Invoke-Sql $restoreSql
    Log "  Restore result: $result"
}

$bak7E = Join-Path $dbDir "7E2088-20260330.bak"
$bakAnnex = Join-Path $dbDir "7E2088_Annex-20260330.bak"
Restore-DatabaseFromBackup -DbName "7E2088" -BakPath $bak7E
Restore-DatabaseFromBackup -DbName "7E2088_Annex" -BakPath $bakAnnex

# Verify databases
$db7ECheck = Invoke-Sql "SELECT name FROM sys.databases WHERE name='7E2088'"
$dbAnnexCheck = Invoke-Sql "SELECT name FROM sys.databases WHERE name='7E2088_Annex'"
if ($db7ECheck -match '7E2088') { Log "OK: Database 7E2088 restored." } else { Log "FAIL: 7E2088 not found." }
if ($dbAnnexCheck -match '7E2088_Annex') { Log "OK: Database 7E2088_Annex restored." } else { Log "FAIL: 7E2088_Annex not found." }

# ===========================
# Phase 4: Create KPMIS login
# ===========================
Log ""
Log "--- Phase 4: Create KPMIS login ---"
$createLoginSql = "IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = 'KPMIS') CREATE LOGIN [KPMIS] WITH PASSWORD = '" + (Escape-SqlLiteral $KpmisPassword) + "', DEFAULT_DATABASE = [7E2088], CHECK_POLICY = OFF;"
$result = Invoke-Sql $createLoginSql
Log "  Create login result: $result"

# Create user in 7E2088
$createUser7E = "IF NOT EXISTS (SELECT * FROM [7E2088].sys.database_principals WHERE name = 'KPMIS') BEGIN USE [7E2088]; CREATE USER [KPMIS] FOR LOGIN [KPMIS]; ALTER ROLE [db_owner] ADD MEMBER [KPMIS]; END;"
$result = Invoke-Sql $createUser7E
Log "  Create user in 7E2088: $result"

# Create user in 7E2088_Annex
$createUserAnnex = "IF NOT EXISTS (SELECT * FROM [7E2088_Annex].sys.database_principals WHERE name = 'KPMIS') BEGIN USE [7E2088_Annex]; CREATE USER [KPMIS] FOR LOGIN [KPMIS]; ALTER ROLE [db_owner] ADD MEMBER [KPMIS]; END;"
$result = Invoke-Sql $createUserAnnex
Log "  Create user in 7E2088_Annex: $result"

# Test connection
$testConn = Invoke-Sql "SELECT TOP 1 name FROM [7E2088].sys.tables"
if ($testConn) {
    Log "OK: KPMIS can read from 7E2088. Sample table: $testConn"
} else {
    Log "WARN: Could not verify KPMIS access to 7E2088."
}

# ===========================
# Phase 5: Deploy dongle DLLs
# ===========================
Log ""
Log "--- Phase 5: Deploy dongle DLLs ---"
$deployBat = Join-Path $webDir "部署软狗.bat"
if (Test-Path $deployBat) {
    Log "Running 部署软狗.bat..."
    Push-Location $webDir
    cmd /c "部署软狗.bat" 2>&1 | ForEach-Object { Log "  $_" }
    Pop-Location
    Log "OK: Dongle DLLs deployed."
} else {
    Log "WARN: 部署软狗.bat not found at $deployBat"
}

# ===========================
# Phase 6: Create IIS site for demo
# ===========================
Log ""
Log "--- Phase 6: Create IIS demo site ---"
$appcmd = Join-Path $env:SystemRoot "System32\inetsrv\appcmd.exe"

# Remove existing site if it's ours
$existingSite = & $appcmd list site "KpLocalDemoRaw" 2>&1
if ($existingSite -match 'KpLocalDemoRaw') {
    Log "Removing existing KpLocalDemoRaw site..."
    & $appcmd delete site /site.name:"KpLocalDemoRaw" 2>&1 | Out-Null
}

# Create app pool
$existingPool = & $appcmd list apppool "KpLocalDemoRawPool" 2>&1
if ($existingPool -match 'KpLocalDemoRawPool') {
    & $appcmd delete apppool /apppool.name:"KpLocalDemoRawPool" 2>&1 | Out-Null
}
& $appcmd add apppool /name:"KpLocalDemoRawPool" /managedRuntimeVersion:v4.0 /managedPipelineMode:Integrated 2>&1 | Out-Null
Log "OK: App pool KpLocalDemoRawPool created."

# Create site
& $appcmd add site /name:"KpLocalDemoRaw" /bindings:"http/*:${DemoPort}:" /physicalPath:$webDir 2>&1 | Out-Null
& $appcmd set site /site.name:"KpLocalDemoRaw" /[path='/'].applicationPool:"KpLocalDemoRawPool" 2>&1 | Out-Null
Log "OK: Site KpLocalDemoRaw created on port $DemoPort."
Log "    Physical path: $webDir"

# Ensure IIS has write permissions to App_Data for the gateway
$gwAppData = Join-Path $gatewayWebDir "App_Data"
$acl = Get-Acl $gwAppData
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl $gwAppData $acl
Log "OK: Write permissions set on gateway App_Data."

# ===========================
# Phase 7: Install KpScheduleService
# ===========================
Log ""
Log "--- Phase 7: Install KpScheduleService ---"
$kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
if ($kpSvc) {
    Log "KpService already installed (status: $($kpSvc.Status))."
    if ($kpSvc.Status -ne 'Running') {
        Log "  Starting KpService..."
        Start-Service -Name 'KpService' -ErrorAction SilentlyContinue
    }
} else {
    Log "Installing KpScheduleService..."
    $installUtil = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe"
    Push-Location $kpServiceDir
    & $installUtil ".\KpScheduleService.exe" 2>&1 | ForEach-Object { Log "  $_" }
    Pop-Location

    $kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
    if ($kpSvc) {
        Log "OK: KpService installed. Starting..."
        Start-Service -Name 'KpService' -ErrorAction SilentlyContinue
    } else {
        Log "WARN: KpService not found after install. May need manual install."
    }
}

Start-Sleep -Seconds 2
$kpSvc = Get-Service -Name 'KpService' -ErrorAction SilentlyContinue
if ($kpSvc -and $kpSvc.Status -eq 'Running') {
    Log "OK: KpService is running."
} else {
    Log "WARN: KpService is not running. Status: $(if($kpSvc){$kpSvc.Status}else{'NOT FOUND'})"
}

# ===========================
# Phase 8: Create IIS site for gateway
# ===========================
Log ""
Log "--- Phase 8: Create gateway IIS site ---"
$existingGw = & $appcmd list site "GatewayDemoLegacy" 2>&1
if ($existingGw -match 'GatewayDemoLegacy') {
    Log "Removing existing GatewayDemoLegacy site..."
    & $appcmd delete site /site.name:"GatewayDemoLegacy" 2>&1 | Out-Null
}

$existingGwPool = & $appcmd list apppool "GatewayDemoLegacyPool" 2>&1
if ($existingGwPool -match 'GatewayDemoLegacyPool') {
    & $appcmd delete apppool /apppool.name:"GatewayDemoLegacyPool" 2>&1 | Out-Null
}
& $appcmd add apppool /name:"GatewayDemoLegacyPool" /managedRuntimeVersion:v4.0 /managedPipelineMode:Integrated 2>&1 | Out-Null

& $appcmd add site /name:"GatewayDemoLegacy" /bindings:"http/*:${GatewayPort}:,http/*:${AdminPort}:" /physicalPath:$gatewayWebDir 2>&1 | Out-Null
& $appcmd set site /site.name:"GatewayDemoLegacy" /[path='/'].applicationPool:"GatewayDemoLegacyPool" 2>&1 | Out-Null
Log "OK: Gateway site created on ports $GatewayPort (business) and $AdminPort (admin)."

# ===========================
# Phase 9: Switch gateway to local-demo
# ===========================
Log ""
Log "--- Phase 9: Switch gateway to local-demo ---"
$switchScript = Join-Path $scriptRoot "set-gateway-profile.ps1"
$demoUrl = "http://localhost:${DemoPort}/"
& $switchScript -Profile "local-demo" -DemoBaseUrl $demoUrl 2>&1 | ForEach-Object { Log "  $_" }

# ===========================
# Summary
# ===========================
Log ""
Log "========================================="
Log " Setup Complete!"
Log "========================================="
Log ""
Log ("Demo site:       http://localhost:" + $DemoPort + "/default.aspx")
Log ("Gateway public:  http://localhost:" + $GatewayPort + "/gateway")
Log ("Gateway admin:   http://localhost:" + $AdminPort + "/admin")
Log ("Gateway proxy:   http://localhost:" + $GatewayPort + "/proxy/erp-main/default.aspx")
Log ""
Log "Log file: $logFile"
Log ""
Log "Next steps:"
Log ("  1. Open browser to http://localhost:" + $DemoPort + "/default.aspx - verify login page")
Log ("  2. Open browser to http://localhost:" + $GatewayPort + "/proxy/erp-main/default.aspx - verify proxy")
Log ""
