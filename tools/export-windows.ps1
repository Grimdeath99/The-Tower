[CmdletBinding()]
param(
    [string] $GodotPath,
    [string] $TemplateArchivePath,
    [switch] $AllowDownloads,
    [switch] $PrepareTemplatesOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolchain = . (Join-Path $PSScriptRoot 'setup.ps1') -GodotPath $GodotPath
$repositoryRoot = $toolchain.RepositoryRoot
$templateVersion = '4.7.2.stable.mono'
$runtimeVersion = '8.0.28'
$templateRoot = Join-Path $repositoryRoot ".build/templates/$templateVersion"
$templateDownload = 'https://downloads.godotengine.org/?flavor=stable&platform=templates&slug=mono_export_templates.tpz&version=4.7.2'
$requiredTemplates = @('windows_debug_x86_64.exe', 'windows_debug_x86_64_console.exe', 'windows_release_x86_64.exe', 'windows_release_x86_64_console.exe')
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-ProcessLog {
    param([string] $Path, $Result)
    $text = $Result.StandardOutput + [Environment]::NewLine + $Result.StandardError
    [System.IO.File]::WriteAllText($Path, $text, $utf8)
    $text
}

function Assert-NoEngineErrors {
    param([string] $Text, [string] $Context)
    $errors = @($Text -split '\r?\n' | Where-Object {
        $_ -match '(?i)^\s*(SCRIPT ERROR:|ERROR:)|CONSTRUCTION_SMOKE_FAIL|Unhandled exception|Build FAILED' -and
        $_ -notmatch '^\s*ERROR: Failed to read the root certificate store\.\s*$'
    } | Select-Object -Unique)
    if ($errors.Count -gt 0) { throw "$Context failed: $($errors -join [Environment]::NewLine)" }
}

function Invoke-ExportEngineProcess {
    param([string] $Executable, [string[]] $Arguments, [string] $WorkingDirectory, [int] $TimeoutSeconds = 180)
    $quotedArguments = foreach ($argument in $Arguments) {
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
    # The editor writes settings, profiler files, and publish logs while exporting.
    # Override these paths for this child process only; the user's profile stays untouched.
    foreach ($profilePart in @(@('APPDATA', 'roaming'), @('LOCALAPPDATA', 'local'))) {
        $profileDirectory = Join-Path $repositoryRoot ('.build/export-profile/' + $profilePart[1])
        [void] [System.IO.Directory]::CreateDirectory($profileDirectory)
        $startInfo.EnvironmentVariables[$profilePart[0]] = $profileDirectory
    }
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "Could not start $Executable." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            [void] $process.WaitForExit(5000)
            throw "Export process exceeded $TimeoutSeconds seconds: $Executable"
        }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdoutTask.GetAwaiter().GetResult()
            StandardError = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally { $process.Dispose() }
}

Push-Location -LiteralPath $repositoryRoot
try {
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $repositoryRoot '.build/templates'))
    [System.IO.File]::WriteAllText((Join-Path $repositoryRoot '.build/.gdignore'), '', $utf8)
    if ([string]::IsNullOrWhiteSpace($TemplateArchivePath)) {
        $TemplateArchivePath = Join-Path $repositoryRoot '.build/templates/Godot_v4.7.2-stable_mono_export_templates.tpz'
    }
    $TemplateArchivePath = [System.IO.Path]::GetFullPath($TemplateArchivePath)
    $missing = @($requiredTemplates | Where-Object { -not (Test-Path -LiteralPath (Join-Path $templateRoot $_) -PathType Leaf) })
    if ($missing.Count -gt 0) {
        if (-not (Test-Path -LiteralPath $TemplateArchivePath -PathType Leaf)) {
            if (-not $AllowDownloads) {
                throw "Missing Godot $templateVersion export templates. Run with -AllowDownloads or provide -TemplateArchivePath for the official .NET TPZ archive. No global template installation is required."
            }
            $downloadPart = Join-Path $repositoryRoot '.build/templates/Godot_v4.7.2-stable_mono_export_templates.tpz.download'
            Write-Host 'Downloading the official matching .NET templates into this workspace...'
            & curl.exe --location --fail --silent --show-error --connect-timeout 20 --max-time 600 --output $downloadPart $templateDownload
            if ($LASTEXITCODE -ne 0) { throw "Official template download failed (curl exit $LASTEXITCODE). Existing extracted templates were preserved." }
            $TemplateArchivePath = Join-Path $repositoryRoot '.build/templates/Godot_v4.7.2-stable_mono_export_templates.tpz'
            Move-Item -LiteralPath $downloadPart -Destination $TemplateArchivePath -Force
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($TemplateArchivePath)
        try {
            $versionEntry = $archive.GetEntry('templates/version.txt')
            if ($null -eq $versionEntry) { throw 'Template archive has no templates/version.txt.' }
            $versionReader = New-Object System.IO.StreamReader($versionEntry.Open())
            try { $archiveVersion = $versionReader.ReadToEnd().Trim() } finally { $versionReader.Dispose() }
            if ($archiveVersion -ne $templateVersion) { throw "Expected template $templateVersion; archive contains $archiveVersion." }
            [void] [System.IO.Directory]::CreateDirectory($templateRoot)
            foreach ($name in $requiredTemplates) {
                $entry = $archive.GetEntry("templates/$name")
                if ($null -eq $entry) { throw "Template archive lacks templates/$name." }
                # Extract only this explicit filename list; archive paths never choose destinations.
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templateRoot $name), $true)
            }
            [System.IO.File]::WriteAllText((Join-Path $templateRoot 'version.txt'), $archiveVersion, $utf8)
        }
        finally { $archive.Dispose() }
        $archiveHash = (Get-FileHash -LiteralPath $TemplateArchivePath -Algorithm SHA256).Hash
        [System.IO.File]::WriteAllText((Join-Path $templateRoot 'SOURCE.txt'), "Official source: $templateDownload`r`nLocal archive SHA256: $archiveHash`r`nVersion: $templateVersion`r`n", $utf8)
    }
    foreach ($name in @('windows_debug_x86_64.exe', 'windows_release_x86_64.exe')) {
        $versionCheck = Invoke-VerticalDistrictProcess -Executable (Join-Path $templateRoot $name) -Arguments @('--version') -WorkingDirectory $repositoryRoot -TimeoutSeconds 15
        if ($versionCheck.ExitCode -ne 0 -or $versionCheck.StandardOutput.Trim() -ne $toolchain.EngineVersion) {
            throw "Export template $name does not match engine $($toolchain.EngineVersion): $($versionCheck.StandardOutput.Trim())"
        }
    }
    Write-Host "Verified local Windows x86_64 templates: $templateRoot"
    if ($PrepareTemplatesOnly) { return }

    $artifactsRoot = Join-Path $repositoryRoot 'artifacts'
    $publishedExportRoot = Join-Path $artifactsRoot 'windows'
    $exportRoot = Join-Path $artifactsRoot ('.windows-staging-' + [Guid]::NewGuid().ToString('N'))
    [void] [System.IO.Directory]::CreateDirectory($exportRoot)
    # Godot publishes a self-contained .NET application. A clean machine may need
    # the matching Microsoft runtime pack in addition to Godot's bundled packages.
    $runtimeFeed = Join-Path $repositoryRoot '.build/templates/runtime-packages'
    [void] [System.IO.Directory]::CreateDirectory($runtimeFeed)
    foreach ($runtimePackage in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64')) {
        $runtimeFile = Join-Path $runtimeFeed "$runtimePackage.$runtimeVersion.nupkg"
        $cachedPackage = Join-Path $repositoryRoot ".nuget/packages/$runtimePackage/$runtimeVersion"
        if ($AllowDownloads -and -not (Test-Path -LiteralPath $runtimeFile -PathType Leaf) -and -not (Test-Path -LiteralPath $cachedPackage -PathType Container)) {
            $runtimeUrl = "https://api.nuget.org/v3-flatcontainer/$runtimePackage/$runtimeVersion/$runtimePackage.$runtimeVersion.nupkg"
            Write-Host "Caching official Microsoft runtime package $runtimePackage $runtimeVersion..."
            & curl.exe --location --fail --silent --show-error --connect-timeout 20 --max-time 180 --output ($runtimeFile + '.download') $runtimeUrl
            if ($LASTEXITCODE -ne 0) { throw "Official Microsoft runtime download failed for $runtimePackage." }
            Move-Item -LiteralPath ($runtimeFile + '.download') -Destination $runtimeFile
        }
    }
    $restoreArguments = @('restore', 'VerticalDistrict.Game.csproj', '--runtime', 'win-x64', '--source', $toolchain.PackageFeed, '--source', $runtimeFeed)
    $restore = Invoke-VerticalDistrictProcess -Executable $toolchain.DotnetPath -Arguments $restoreArguments -WorkingDirectory $repositoryRoot -TimeoutSeconds 180
    $restoreText = Write-ProcessLog -Path (Join-Path $artifactsRoot 'export-restore.log') -Result $restore
    if ($restore.ExitCode -ne 0) { throw "Export runtime restore failed. See artifacts/export-restore.log. On a clean cache use -AllowDownloads to fetch Microsoft's runtime packages from NuGet." }

    Write-Host 'Building the game and simulation in ExportRelease configuration...'
    $build = Invoke-VerticalDistrictProcess -Executable $toolchain.DotnetPath -Arguments @('build', 'VerticalDistrict.Game.csproj', '--configuration', 'ExportRelease', '--nologo') -WorkingDirectory $repositoryRoot -TimeoutSeconds 180
    $buildText = Write-ProcessLog -Path (Join-Path $artifactsRoot 'export-build.log') -Result $build
    if ($build.ExitCode -ne 0) { throw 'ExportRelease build failed. See artifacts/export-build.log.' }
    Assert-NoEngineErrors -Text $buildText -Context 'ExportRelease build'

    $executable = Join-Path $exportRoot 'VerticalDistrict.exe'
    Write-Host 'Exporting Windows Desktop with the matching .NET release template...'
    $export = Invoke-ExportEngineProcess -Executable $toolchain.GodotPath -Arguments @('--headless', '--path', $repositoryRoot, '--export-release', 'Windows Desktop', $executable) -WorkingDirectory $repositoryRoot -TimeoutSeconds 180
    $exportText = Write-ProcessLog -Path (Join-Path $artifactsRoot 'export-windows.log') -Result $export
    if ($export.ExitCode -ne 0) { throw "Windows export exited with code $($export.ExitCode). See artifacts/export-windows.log." }
    Assert-NoEngineErrors -Text $exportText -Context 'Windows export'
    foreach ($expected in @($executable, (Join-Path $exportRoot 'VerticalDistrict.pck'))) {
        if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) { throw "Export did not produce $expected." }
    }
    $assemblies = @(Get-ChildItem -LiteralPath $exportRoot -Filter 'VerticalDistrict.Game.dll' -Recurse -File)
    if ($assemblies.Count -ne 1) { throw 'Export did not produce exactly one game assembly in its runtime directory.' }

    Write-Host 'Running the exported executable outside the editor (60-second limit)...'
    $smokeLog = Join-Path $artifactsRoot 'export-smoke.log'
    [System.IO.File]::WriteAllText($smokeLog, '', $utf8)
    $smoke = Invoke-ExportEngineProcess -Executable $executable -Arguments @('--headless', '--log-file', $smokeLog, '--', '--construction-smoke') -WorkingDirectory $exportRoot -TimeoutSeconds 60
    $smokeOutput = $smoke.StandardOutput + [Environment]::NewLine + $smoke.StandardError
    [System.IO.File]::WriteAllText((Join-Path $artifactsRoot 'export-smoke-process.log'), $smokeOutput, $utf8)
    if ($smoke.ExitCode -ne 0) { throw "Exported smoke exited with code $($smoke.ExitCode). See artifacts/export-smoke-process.log." }
    Assert-NoEngineErrors -Text $smokeOutput -Context 'Exported smoke'
    if ($smokeOutput -notmatch '(?m)^CONSTRUCTION_SMOKE_PASS:') { throw 'Exported executable did not report CONSTRUCTION_SMOKE_PASS.' }
    $smokeEvidenceRoot = Join-Path $exportRoot 'artifacts'
    $retainedEvidenceRoot = Join-Path $artifactsRoot ('export-smoke-evidence-' + [Guid]::NewGuid().ToString('N'))
    # Only publish the freshly smoke-tested directory. Preserve any prior export
    # and explicitly verify both directory move targets stay inside artifacts.
    $artifactPrefix = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd([char]92) + [char]92
    $previousExportRoot = Join-Path $artifactsRoot ('windows.previous-' + [Guid]::NewGuid().ToString('N'))
    foreach ($checkedPath in @($exportRoot, $publishedExportRoot, $previousExportRoot, $smokeEvidenceRoot, $retainedEvidenceRoot)) {
        if (-not [System.IO.Path]::GetFullPath($checkedPath).StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing export directory move outside artifacts: $checkedPath"
        }
    }
    if (Test-Path -LiteralPath $smokeEvidenceRoot -PathType Container) {
        Move-Item -LiteralPath $smokeEvidenceRoot -Destination $retainedEvidenceRoot
    }
    if (Test-Path -LiteralPath $publishedExportRoot) {
        Move-Item -LiteralPath $publishedExportRoot -Destination $previousExportRoot
    }
    try { Move-Item -LiteralPath $exportRoot -Destination $publishedExportRoot }
    catch {
        if ((Test-Path -LiteralPath $previousExportRoot) -and -not (Test-Path -LiteralPath $publishedExportRoot)) {
            Move-Item -LiteralPath $previousExportRoot -Destination $publishedExportRoot
        }
        throw
    }
    Write-Host "Windows export and exported smoke passed: $(Join-Path $publishedExportRoot 'VerticalDistrict.exe')"
    Write-Host 'Distribute the entire artifacts/windows folder, including its .pck and .NET runtime data directory.'
}
finally { Pop-Location }
