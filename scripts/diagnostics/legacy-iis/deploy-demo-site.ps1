# deploy-demo-site.ps1
# Run as Administrator: powershell -ExecutionPolicy Bypass -File "E:\验证页面\scripts\deploy-demo-site.ps1"

$ErrorActionPreference = "Stop"
$RepoRoot = "E:\验证页面"
$DemoRoot = Join-Path $RepoRoot ([char]0x6F14 . [char]0x793A . [char]0x7AD9 . [char]0x70B9 . "20260330")
