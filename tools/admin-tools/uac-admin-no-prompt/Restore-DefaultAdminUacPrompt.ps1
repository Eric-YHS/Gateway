[CmdletBinding()]
param(
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$scriptPath = $MyInvocation.MyCommand.Path
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    if ($Elevated) {
        throw 'The restore process did not receive an administrator token.'
    }

    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevated' -f $scriptPath
    $process = Start-Process `
        -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $process.ExitCode
}

$policyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
$policy = Get-ItemProperty -LiteralPath $policyPath
if ([int]$policy.EnableLUA -ne 1) {
    throw 'EnableLUA is not enabled. Refusing to modify an unexpected UAC configuration.'
}

Set-ItemProperty `
    -LiteralPath $policyPath `
    -Name 'ConsentPromptBehaviorAdmin' `
    -Type DWord `
    -Value 5

$restoredPolicy = Get-ItemProperty -LiteralPath $policyPath
if ([int]$restoredPolicy.ConsentPromptBehaviorAdmin -ne 5) {
    throw 'ConsentPromptBehaviorAdmin did not restore to 5.'
}
if ([int]$restoredPolicy.EnableLUA -ne 1) {
    throw 'EnableLUA changed unexpectedly.'
}

Write-Output 'CONSENT_PROMPT_BEHAVIOR_ADMIN=5'
Write-Output 'ENABLE_LUA=1'
