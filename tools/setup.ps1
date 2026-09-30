[CmdletBinding()]
param(
    [string] $GodotPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expectedEngineVersion = '4.7.2.stable.mono.official.ed1daf0bf'
$expectedSdkVersion = '10.0.301'

# Godot's Windows GUI executable needs redirected process handles to capture its
# output reliably. Read both pipes concurrently so either pipe can fill safely.
function Invoke-VerticalDistrictProcess {
    param(
        [Parameter(Mandatory = $true)][string] $Executable,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [int] $TimeoutSeconds = 60
    )

    $quotedArguments = foreach ($argument in $Arguments) {
        # Windows command-line escaping: double backslashes before embedded
        # quotes and before the closing quote. No shell evaluates these values.
        $quoted = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
        $quoted = [regex]::Replace($quoted, '(\\+)$', '$1$1')
        '"' + $quoted + '"'
    }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not start $Executable."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            [void] $process.WaitForExit(5000)
            throw "Process exceeded the $TimeoutSeconds-second timeout: $Executable"
        }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdoutTask.GetAwaiter().GetResult()
            StandardError = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    $sdkPin = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($sdkPin -ne $expectedSdkVersion) {
        throw "global.json must pin .NET SDK $expectedSdkVersion; found $sdkPin."
    }
    $dotnetPath = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $sdkVersion = (& $dotnetPath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -ne $expectedSdkVersion) {
        throw "Install .NET SDK $expectedSdkVersion. The selected dotnet reports '$sdkVersion'."
    }

    if ([string]::IsNullOrWhiteSpace($GodotPath)) {
        $candidates = New-Object 'System.Collections.Generic.List[string]'
        $settingsPath = Join-Path $repositoryRoot '.vscode/settings.json'
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            try {
                $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
                $configured = $settings.PSObject.Properties['godotTools.editorPath.godot4']
                if ($null -ne $configured -and -not [string]::IsNullOrWhiteSpace($configured.Value)) {
                    $candidates.Add([string] $configured.Value)
                }
            }
            catch {
                Write-Warning "Could not read the optional Godot editor path from .vscode/settings.json: $($_.Exception.Message)"
            }
        }
        foreach ($name in @('godot', 'godot4', 'Godot_v4.7.2-stable_mono_win64.exe')) {
            $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $command) { $candidates.Add($command.Source) }
        }
        foreach ($installationRoot in @($env:ProgramFiles, 'I:\Program Files')) {
            if (-not [string]::IsNullOrWhiteSpace($installationRoot)) {
                $candidates.Add((Join-Path $installationRoot 'Godot/Godot_v4.7.2-stable_mono_win64.exe'))
            }
        }
        $GodotPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($GodotPath)) {
            throw 'Godot 4.7.2 .NET was not found. Pass -GodotPath with the installed engine executable. This script does not install tools.'
        }
    }
    if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
        throw "Godot executable does not exist: $GodotPath"
    }
    $resolvedGodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
    $engineResult = Invoke-VerticalDistrictProcess -Executable $resolvedGodotPath -Arguments @('--version') -WorkingDirectory $repositoryRoot -TimeoutSeconds 15
    $engineVersion = $engineResult.StandardOutput.Trim()
    if ($engineResult.ExitCode -ne 0 -or $engineVersion -ne $expectedEngineVersion) {
        throw "Expected Godot $expectedEngineVersion, got '$engineVersion' (exit $($engineResult.ExitCode)). $($engineResult.StandardError)"
    }

    $packageFeed = Join-Path (Split-Path -Parent $resolvedGodotPath) 'GodotSharp/Tools/nupkgs'
    foreach ($package in @('Godot.NET.Sdk', 'GodotSharp', 'GodotSharpEditor', 'Godot.SourceGenerators')) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageFeed "$package.4.7.2.nupkg") -PathType Leaf)) {
            throw "Missing bundled package $package.4.7.2.nupkg in $packageFeed. Use the complete Godot 4.7.2 .NET distribution."
        }
    }

    $escapedFeed = [System.Security.SecurityElement]::Escape($packageFeed)
    $nugetConfiguration = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="godot-bundled" value="$escapedFeed" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value=".nuget/packages" />
  </config>
</configuration>
"@
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $repositoryRoot 'NuGet.Config'), $nugetConfiguration, $utf8)
    foreach ($directory in @('.nuget', 'artifacts')) {
        $path = Join-Path $repositoryRoot $directory
        [void] [System.IO.Directory]::CreateDirectory($path)
        [System.IO.File]::WriteAllText((Join-Path $path '.gdignore'), '', $utf8)
    }

    Write-Host "Validated Godot $engineVersion and .NET SDK $sdkVersion."
    Write-Host 'Generated ignored NuGet.Config using the installed Godot package feed. No software was installed.'
    [pscustomobject]@{
        GodotPath = $resolvedGodotPath
        DotnetPath = $dotnetPath
        RepositoryRoot = $repositoryRoot
        EngineVersion = $engineVersion
        SdkVersion = $sdkVersion
        PackageFeed = $packageFeed
    }
}
finally {
    Pop-Location
}
