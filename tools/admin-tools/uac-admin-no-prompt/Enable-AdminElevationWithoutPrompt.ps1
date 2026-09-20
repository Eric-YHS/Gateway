#Requires -RunAsAdministrator

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$policyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
$expectedInstallDirectory = 'C:\Program Files\CodexDesktopElevated'
$taskName = 'Codex Desktop Elevated'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Codex Desktop Elevated.lnk'

$policy = Get-ItemProperty -LiteralPath $policyPath
if ([int]$policy.EnableLUA -ne 1) {
    throw 'EnableLUA is not enabled. Refusing to modify an unexpected UAC configuration.'
}

Set-ItemProperty `
    -LiteralPath $policyPath `
    -Name 'ConsentPromptBehaviorAdmin' `
    -Type DWord `
    -Value 0

$updatedPolicy = Get-ItemProperty -LiteralPath $policyPath
if ([int]$updatedPolicy.ConsentPromptBehaviorAdmin -ne 0) {
    throw 'ConsentPromptBehaviorAdmin did not change to 0.'
}
if ([int]$updatedPolicy.EnableLUA -ne 1) {
    throw 'EnableLUA changed unexpectedly.'
}

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue

$resolvedExpectedDirectory = [System.IO.Path]::GetFullPath($expectedInstallDirectory).TrimEnd('\')
if (Test-Path -LiteralPath $resolvedExpectedDirectory -PathType Container) {
    $resolvedActualDirectory = (Resolve-Path -LiteralPath $resolvedExpectedDirectory).Path.TrimEnd('\')
    if (-not $resolvedActualDirectory.Equals($resolvedExpectedDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected directory: $resolvedActualDirectory"
    }
    Remove-Item -LiteralPath $resolvedActualDirectory -Recurse -Force
}

Write-Output 'CONSENT_PROMPT_BEHAVIOR_ADMIN=0'
Write-Output 'ENABLE_LUA=1'
Write-Output ('REMOVED_TASK=' + $taskName)
Write-Output ('REMOVED_SHORTCUT=' + $shortcutPath)
Write-Output ('REMOVED_DIRECTORY=' + $resolvedExpectedDirectory)
