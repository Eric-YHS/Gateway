@echo off
title Local Demo Environment Setup
echo =========================================================
echo   Local Demo Environment Setup
echo =========================================================
echo.
echo   1) Phase 1: Full install (IIS + SQL + everything)
echo   2) Phase 2: IIS/SQL already done, restore DB + create sites
echo.
set /p choice="  Choose (1 or 2): "

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo [FAIL] This script must be run as Administrator.
    echo Right-click this file and select "Run as administrator".
    pause
    exit /b 1
)

echo.
if "%choice%"=="2" (
    powershell -ExecutionPolicy Bypass -File "%~dp0scripts\legacy\setup-local-demo-phase2.ps1"
) else (
    powershell -ExecutionPolicy Bypass -File "%~dp0scripts\legacy\setup-local-demo-env.ps1"
)

echo.
echo =========================================================
echo   Script finished. Press any key to exit.
echo =========================================================
pause
