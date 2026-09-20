# Trigger a request to start the worker process
try {
    $wc = New-Object System.Net.WebClient
    $null = $wc.DownloadString("http://localhost:8080/default.aspx")
} catch {}

Start-Sleep -Milliseconds 500

# Check w3wp processes
$w3wp = Get-Process w3wp -ErrorAction SilentlyContinue
if (-not $w3wp) {
    Write-Host "No w3wp processes found!"
    exit
}

foreach ($proc in $w3wp) {
    Write-Host "PID: $($proc.Id) - AppPool: checking..."
    Write-Host "  Modules loaded: $($proc.Modules.Count)"

    # Check for webengine4
    $we = $proc.Modules | Where-Object { $_.ModuleName -eq "webengine4.dll" }
    if ($we) {
        Write-Host "  webengine4.dll: FOUND at $($we.FileName)"
    } else {
        Write-Host "  webengine4.dll: NOT FOUND"
    }

    # Check for CLR
    $clr = $proc.Modules | Where-Object { $_.ModuleName -eq "clr.dll" }
    if ($clr) {
        Write-Host "  clr.dll: FOUND at $($clr.FileName)"
    } else {
        Write-Host "  clr.dll: NOT FOUND"
    }

    # Check for coreclr
    $coreclr = $proc.Modules | Where-Object { $_.ModuleName -eq "coreclr.dll" }
    if ($coreclr) {
        Write-Host "  coreclr.dll: FOUND at $($coreclr.FileName) - THIS MAY BE THE ISSUE!"
    } else {
        Write-Host "  coreclr.dll: not loaded (OK)"
    }

    # List .NET related modules
    $netMods = $proc.Modules | Where-Object { $_.FileName -match 'Microsoft\.NET|webengine|clr|mscorlib|System\.' }
    Write-Host "  .NET modules:"
    foreach ($m in $netMods) {
        Write-Host "    $($m.ModuleName) -> $($m.FileName)"
    }
}
