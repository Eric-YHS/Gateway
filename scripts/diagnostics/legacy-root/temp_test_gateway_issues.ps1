# Test gateway page issues
$ErrorActionPreference = 'Stop'

Write-Host "=== Testing Gateway Pages ===" -ForegroundColor Cyan
Write-Host ""

$testUrl = "http://localhost:5050"

# Test 1: Main page
Write-Host "1. Testing main page..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$testUrl/default.aspx" -UseBasicParsing -TimeoutSec 10
    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Green

    $content = $response.Content

    # Check for [object Object]
    if ($content -match '\[object Object\]') {
        Write-Host "   [X] Issue 1 FOUND: Page contains [object Object]" -ForegroundColor Red
    } else {
        Write-Host "   [OK] Issue 1 FIXED: No [object Object] found" -ForegroundColor Green
    }

    # Check for device environment message (using regex to avoid encoding issues)
    if ($content -match 'APP.*device|environment.*change|trigger.*review') {
        Write-Host "   [X] Issue 2 FOUND: Device environment message present" -ForegroundColor Red
    } else {
        Write-Host "   [OK] Issue 2 FIXED: No device environment message" -ForegroundColor Green
    }

    # Save content for inspection
    $content | Out-File -FilePath "E:\验证页面\temp_gateway_page.html" -Encoding UTF8
    Write-Host "   Page saved to: temp_gateway_page.html" -ForegroundColor Gray

} catch {
    Write-Host "   [X] Failed to access: $_" -ForegroundColor Red
}

Write-Host ""

# Test 2: Gateway entry page
Write-Host "2. Testing Gateway/Default.aspx..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$testUrl/Gateway/Default.aspx" -UseBasicParsing -TimeoutSec 10
    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Green

    $content = $response.Content

    if ($content -match '\[object Object\]') {
        Write-Host "   [X] Issue 1 FOUND: Page contains [object Object]" -ForegroundColor Red
    } else {
        Write-Host "   [OK] Issue 1 FIXED: No [object Object] found" -ForegroundColor Green
    }

    if ($content -match 'APP.*device|environment.*change|trigger.*review') {
        Write-Host "   [X] Issue 2 FOUND: Device environment message present" -ForegroundColor Red
    } else {
        Write-Host "   [OK] Issue 2 FIXED: No device environment message" -ForegroundColor Green
    }

    $content | Out-File -FilePath "E:\验证页面\temp_gateway_entry.html" -Encoding UTF8
    Write-Host "   Page saved to: temp_gateway_entry.html" -ForegroundColor Gray

} catch {
    Write-Host "   [X] Failed to access: $_" -ForegroundColor Red
}

Write-Host ""

# Test 3: Check database
Write-Host "3. Checking database..." -ForegroundColor Yellow
$dbPath = "E:\验证页面\deliverables\GatewayDemo.Legacy-net472\src\GatewayDemo.Legacy.Web\App_Data\gateway-demo-legacy.db"
if (Test-Path $dbPath) {
    $dbSize = (Get-Item $dbPath).Length
    Write-Host "   [OK] Database exists: $dbSize bytes" -ForegroundColor Green
} else {
    Write-Host "   [X] Database not found" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
