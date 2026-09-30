[CmdletBinding()]
param(
    [string] $GodotPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Dot-source setup so the same hidden, redirected process launcher handles both
# engine validation and the integration test. Setup emits one toolchain object.
$toolchain = . (Join-Path $PSScriptRoot 'setup.ps1') -GodotPath $GodotPath
Push-Location -LiteralPath $toolchain.RepositoryRoot
try {
    Write-Host 'Building the integrated solution...'
    & $toolchain.DotnetPath build VerticalDistrict.sln --nologo
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }

    foreach ($suite in @('Core', 'Transport', 'Persistence', 'Geography', 'Simulation', 'Finance')) {
        $testProject = "tests/VerticalDistrict.$suite.Tests"
        Write-Host "Running $suite tests..."
        & $toolchain.DotnetPath run --project $testProject -c Release
        if ($LASTEXITCODE -ne 0) { throw "$suite tests failed with exit code $LASTEXITCODE." }
    }

    $logPath = Join-Path $toolchain.RepositoryRoot 'artifacts/headless-smoke.log'
    # Truncate the previous artifact before launch so stale success markers cannot
    # satisfy this run. The current process output must also contain its marker.
    [System.IO.File]::WriteAllText($logPath, '', (New-Object System.Text.UTF8Encoding($false)))
    Write-Host 'Running the integrated Godot scene smoke test (60-second limit)...'
    $smoke = Invoke-VerticalDistrictProcess -Executable $toolchain.GodotPath -WorkingDirectory $toolchain.RepositoryRoot -TimeoutSeconds 60 -Arguments @(
        '--headless', '--path', $toolchain.RepositoryRoot, '--log-file', $logPath, '--', '--construction-smoke'
    )
    $processOutput = $smoke.StandardOutput + [Environment]::NewLine + $smoke.StandardError
    Write-Host $processOutput.Trim()
    $logOutput = if (Test-Path -LiteralPath $logPath -PathType Leaf) { Get-Content -LiteralPath $logPath -Raw } else { '' }
    $allOutput = $processOutput + [Environment]::NewLine + $logOutput

    if ($allOutput -match 'Failed to read the root certificate store\.') {
        Write-Warning 'Godot reported that it could not read the Windows root certificate store. This environment startup diagnostic is recorded in the log; the offline scene test does not use network certificates.'
    }
    $errors = @($allOutput -split '\r?\n' | Where-Object {
        $_ -match '(?i)^\s*(SCRIPT ERROR:|ERROR:)|CONSTRUCTION_SMOKE_FAIL|Unhandled exception' -and
        $_ -notmatch '^\s*ERROR: Failed to read the root certificate store\.\s*$'
    } | Select-Object -Unique)
    if ($smoke.ExitCode -ne 0) { throw "Godot smoke test exited with code $($smoke.ExitCode). Log: $logPath" }
    if ($errors.Count -gt 0) { throw "Godot reported errors: $($errors -join [Environment]::NewLine). Log: $logPath" }
    if ($processOutput -notmatch '(?m)^CONSTRUCTION_SMOKE_PASS:') {
        throw "Godot exited without the required CONSTRUCTION_SMOKE_PASS marker. Log: $logPath"
    }

    Write-Host "Verification passed: solution build, all six test suites, and integrated Godot scene smoke. Log: $logPath"
}
finally {
    Pop-Location
}
