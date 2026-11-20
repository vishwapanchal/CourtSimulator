<#
.SYNOPSIS
    Drives the Unity Editor headlessly: compile, regenerate the CourtRoom scene, run EditMode/PlayMode tests.

.DESCRIPTION
    Runs Unity in -batchmode so nothing needs to be clicked. The Editor version comes from
    ProjectSettings/ProjectVersion.txt. Logs and test results go to Logs/batch/ (git-ignored).

    Tasks:
      compile   Import the project and compile all assemblies; fails on any compiler error.
      scene     Regenerate Assets/Scenes/CourtRoom.unity via CourtroomSceneGenerator.GenerateFromCommandLine.
      editmode  Run the EditMode test suite.
      playmode  Run the PlayMode test suite.
      test      editmode + playmode.
      all       compile + scene + test (default).
      live      Play one case against a real language model: the local Ollama model by default (free),
                or Claude with -LiveArgs '-liveBackend claude' (needs ANTHROPIC_API_KEY).
                Not part of 'all'. Pass runner options with -LiveArgs, e.g. '-liveChoices follow,follow,reject'.
                Transcript: Logs/batch/live/live-transcript.md.

    Batch mode cannot open a project that an interactive Editor already has open; close it first.

.PARAMETER Task
    One or more tasks to run, in order.

.PARAMETER UnityExe
    Path to Unity.exe. Defaults to $env:UNITY_EXE, then the standard Hub and standalone install folders.

.PARAMETER Coverage
    Collect code coverage for the CourtSim.* assemblies with com.unity.testtools.codecoverage.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/unity.ps1
    powershell -ExecutionPolicy Bypass -File tools/unity.ps1 -Task playmode -Coverage
#>
[CmdletBinding()]
param(
    # Comma-separated when invoked via `powershell -File`, which passes arrays as one string.
    [string[]] $Task = @('all'),
    [string] $UnityExe = $env:UNITY_EXE,
    [switch] $Coverage,
    # Line-coverage gate across the CourtSim.* assemblies, checked when -Coverage is set.
    [double] $MinCoverage = 80,
    # Extra arguments for the live task's LiveSessionRunner, as one string.
    [string] $LiveArgs = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'Logs/batch'
New-Item -ItemType Directory -Force $outDir | Out-Null

function Resolve-UnityExe {
    if ($UnityExe) {
        if (-not (Test-Path $UnityExe)) { throw "Unity executable not found at '$UnityExe'." }
        return $UnityExe
    }

    $versionLine = Get-Content (Join-Path $root 'ProjectSettings/ProjectVersion.txt') |
        Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "$env:ProgramFiles\Unity $version\Editor\Unity.exe",
        "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity",
        "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "Unity $version not found. Install it with Unity Hub or pass -UnityExe / set UNITY_EXE."
}

function Invoke-Unity([string] $name, [string[]] $arguments) {
    $log = Join-Path $outDir "$name.log"
    $allArgs = @('-batchmode', '-nographics', '-projectPath', "`"$root`"", '-logFile', "`"$log`"") + $arguments
    Write-Host "==> Unity $name" -ForegroundColor Cyan
    # WaitForExit rather than -Wait: -Wait also waits for helper processes Unity leaves running
    # (shader compiler, Bee), which can hang the script after Unity itself has exited.
    $process = Start-Process -FilePath $script:unity -ArgumentList $allArgs -PassThru -NoNewWindow
    $null = $process.Handle # keeps ExitCode readable after exit
    $process.WaitForExit()
    $errors = @(Select-String -Path $log -Pattern 'error CS\d+|Scripts have compiler errors' | ForEach-Object { $_.Line.Trim() } |
        Select-Object -Unique)
    if ($errors.Count -gt 0) {
        $errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "Unity $name reported compiler errors. See $log"
    }

    return $process.ExitCode
}

function Invoke-Tests([string] $platform) {
    $results = Join-Path $outDir "$platform-results.xml"
    if (Test-Path $results) { Remove-Item $results }
    $arguments = @('-runTests', '-testPlatform', $platform, '-testResults', "`"$results`"")
    if ($Coverage) {
        $arguments += @('-enableCodeCoverage', '-coverageResultsPath', "`"$(Join-Path $outDir 'coverage')`"",
            '-coverageOptions', '"generateAdditionalMetrics;generateHtmlReport;assemblyFilters:+CourtSim.*,-CourtSim.*.Tests"')
    }

    $exit = Invoke-Unity $platform $arguments
    if (-not (Test-Path $results)) { throw "$platform tests produced no results (exit code $exit). See Logs/batch/$platform.log" }

    [xml] $xml = Get-Content $results
    $run = $xml.'test-run'
    Write-Host ("    {0}: {1} total, {2} passed, {3} failed, {4} skipped" -f $platform, $run.total, $run.passed, $run.failed, $run.skipped)
    foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
        Write-Host "    FAILED $($case.fullname)" -ForegroundColor Red
        Write-Host ("      " + ($case.failure.message.InnerText -replace "`n", "`n      ")) -ForegroundColor Red
    }

    if ($exit -ne 0 -or [int] $run.failed -gt 0) { throw "$platform tests failed (exit code $exit)." }
}

$script:unity = Resolve-UnityExe
$lock = Join-Path $root 'Temp/UnityLockfile'
if ((Test-Path $lock) -and (Get-Process Unity -ErrorAction SilentlyContinue)) {
    Write-Warning 'Temp/UnityLockfile exists; if an Editor has this project open, batch mode will refuse to start.'
}

$valid = 'compile', 'scene', 'editmode', 'playmode', 'test', 'all', 'live'
$steps = foreach ($t in ($Task -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    if ($valid -notcontains $t) { throw "Unknown task '$t'. Valid tasks: $($valid -join ', ')." }
    switch ($t) {
        'all' { 'compile', 'scene', 'editmode', 'playmode' }
        'test' { 'editmode', 'playmode' }
        default { $t }
    }
}

$coverageDir = Join-Path $outDir 'coverage'
if ($Coverage -and (Test-Path $coverageDir)) {
    Remove-Item -Recurse -Force $coverageDir
}

foreach ($step in $steps) {
    switch ($step) {
        'compile' {
            $exit = Invoke-Unity 'compile' @('-quit')
            if ($exit -ne 0) { throw "Unity compile exited with $exit. See Logs/batch/compile.log" }
            Write-Host '    compiled cleanly'
        }
        'scene' {
            $exit = Invoke-Unity 'scene' @('-quit', '-executeMethod', 'CourtSim.EditorTools.CourtroomSceneGenerator.GenerateFromCommandLine')
            if ($exit -ne 0) { throw "Scene generation exited with $exit. See Logs/batch/scene.log" }
            Select-String -Path (Join-Path $outDir 'scene.log') -Pattern '^Generated ' | ForEach-Object { Write-Host "    $($_.Line)" }
        }
        'editmode' { Invoke-Tests 'EditMode' }
        'playmode' { Invoke-Tests 'PlayMode' }
        'live' {
            # A key set with `setx` is in the user environment but not yet in this shell's; pass it on.
            if (-not $env:ANTHROPIC_API_KEY) {
                $env:ANTHROPIC_API_KEY = [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY', 'User')
            }

            if ($LiveArgs -match '-liveBackend\s+claude' -and -not $env:ANTHROPIC_API_KEY) { throw 'ANTHROPIC_API_KEY is not set. Run: setx ANTHROPIC_API_KEY "<your key>"' }
            $liveArguments = @('-executeMethod', 'CourtSim.EditorTools.LiveSessionRunner.RunFromCommandLine')
            if ($LiveArgs) { $liveArguments += ($LiveArgs -split '\s+' | Where-Object { $_ }) }
            $exit = Invoke-Unity 'live' $liveArguments
            Select-String -Path (Join-Path $outDir 'live.log') -Pattern '^\[live\]' | ForEach-Object { Write-Host "    $($_.Line)" }
            if ($exit -ne 0) { throw "Live session failed (exit $exit). See Logs/batch/live.log" }
        }
    }
}

if ($Coverage -and ($steps -contains 'editmode' -or $steps -contains 'playmode')) {
    $summary = Join-Path $coverageDir 'Report/Summary.xml'
    if (-not (Test-Path $summary)) { throw "Coverage summary not found at $summary." }
    [xml] $report = Get-Content $summary
    foreach ($assembly in $report.CoverageReport.Coverage.Assembly) {
        Write-Host ("    {0}: {1}% lines" -f $assembly.name, $assembly.coverage)
    }

    $lines = [double] $report.CoverageReport.Summary.Linecoverage
    Write-Host ("==> Unity coverage: {0}% lines (gate: {1}%). HTML report: Logs/batch/coverage/Report/index.html" -f $lines, $MinCoverage) -ForegroundColor Cyan
    if ($lines -lt $MinCoverage) { throw "Line coverage $lines% is below the $MinCoverage% gate." }
}

Write-Host '==> Unity checks passed.' -ForegroundColor Green
