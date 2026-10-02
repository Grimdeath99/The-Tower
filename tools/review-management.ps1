[CmdletBinding()]
param([string] $GodotPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolchain = . (Join-Path $PSScriptRoot 'setup.ps1') -GodotPath $GodotPath
$logPath = Join-Path $toolchain.RepositoryRoot 'artifacts/management-graphical.log'
$started = [DateTime]::UtcNow
# The graphical acceptance mode uses isolated smoke-save paths, pauses the logical
# session and captures actual rendered panels. No personal district is replaced.
$run = Invoke-VerticalDistrictProcess -Executable $toolchain.GodotPath -WorkingDirectory $toolchain.RepositoryRoot -TimeoutSeconds 90 -Arguments @(
    '--path', $toolchain.RepositoryRoot, '--log-file', $logPath, '--', '--construction-smoke', '--management-captures'
)
$output = $run.StandardOutput + [Environment]::NewLine + $run.StandardError
Write-Host $output.Trim()
$errors = @($output -split '\r?\n' | Where-Object {
    $_ -match '(?i)^\s*(SCRIPT ERROR:|ERROR:)|CONSTRUCTION_SMOKE_FAIL|Unhandled exception' -and
    $_ -notmatch '^\s*ERROR: Failed to read the root certificate store\.\s*$'
})
if ($run.ExitCode -ne 0 -or $errors.Count -gt 0 -or $output -notmatch '(?m)^CONSTRUCTION_SMOKE_PASS:') {
    throw "Graphical management acceptance failed. See $logPath"
}
foreach ($name in @('services', 'demand', 'progression', 'room')) {
    $capture = Join-Path $toolchain.RepositoryRoot "artifacts/management-$name.png"
    if (!(Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture).LastWriteTimeUtc -lt $started) {
        throw "Missing fresh management capture: $capture"
    }
    Write-Host "Rendered: $capture"
}
foreach ($name in @('ownership', 'catalogue', 'buyback', 'cutaway')) {
    $capture = Join-Path $toolchain.RepositoryRoot "artifacts/business-$name.png"
    if (!(Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture).LastWriteTimeUtc -lt $started) {
        throw "Missing fresh business capture: $capture"
    }
    Write-Host "Rendered: $capture"
}
foreach ($name in @('offering', 'purchases', 'cafe', 'cutaway')) {
    $capture = Join-Path $toolchain.RepositoryRoot "artifacts/retail-$name.png"
    if (!(Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture).LastWriteTimeUtc -lt $started) {
        throw "Missing fresh retail capture: $capture"
    }
    Write-Host "Rendered: $capture"
}
foreach ($name in @('bank', 'transfer', 'route', 'service')) {
    $capture = Join-Path $toolchain.RepositoryRoot "artifacts/transport-$name.png"
    if (!(Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture).LastWriteTimeUtc -lt $started) {
        throw "Missing fresh transport capture: $capture"
    }
    Write-Host "Rendered: $capture"
}
Write-Host 'Graphical management and advanced transport acceptance passed; inspect the sixteen fresh captures for layout quality.'
