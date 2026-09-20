param(
    [ValidateSet("plan", "run")]
    [string]$Mode = "plan",

    [Parameter(Mandatory = $true)]
    [string]$Prompt,

    [switch]$ContinueLast
)

$argsList = @()

if ($ContinueLast) {
    $argsList += "-c"
}

$argsList += @(
    "-p",
    "--output-format", "json"
)

if ($Mode -eq "plan") {
    $argsList += @("--permission-mode", "plan")
}

& claude @argsList -- $Prompt
exit $LASTEXITCODE
