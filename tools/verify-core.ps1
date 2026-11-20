<#
.SYNOPSIS
    Builds, style-checks and tests CourtSim.Core outside Unity, and enforces the coverage gate.

.DESCRIPTION
    1. dotnet format --verify-no-changes  (style from .editorconfig)
    2. dotnet build                       (warnings are errors, .NET Standard 2.1 API surface)
    3. dotnet test with coverlet          (Assets/Tests/EditMode suite)
    4. Fails if line coverage of CourtSim.Core is below -MinCoverage percent.

.PARAMETER Dotnet
    Path to the dotnet executable. Defaults to `dotnet` on PATH.

.PARAMETER MinCoverage
    Minimum line coverage percentage. Defaults to 80.

.EXAMPLE
    pwsh tools/verify-core.ps1
    powershell -ExecutionPolicy Bypass -File tools/verify-core.ps1 -Dotnet C:\sdk\dotnet\dotnet.exe
#>
[CmdletBinding()]
param(
    [string] $Dotnet = 'dotnet',
    [double] $MinCoverage = 80
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $PSScriptRoot 'CoreVerification'
$testProject = Join-Path $projectDir 'Tests/CourtSim.Core.Tests.csproj'
$results = Join-Path $projectDir 'TestResults'

function Invoke-Step([string] $name, [scriptblock] $body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) {
        throw "$name failed (exit code $LASTEXITCODE)."
    }
}

Push-Location $root
try {
    if (Test-Path $results) {
        Remove-Item -Recurse -Force $results
    }

    Invoke-Step 'Restore' { & $Dotnet restore $testProject --nologo }
    Invoke-Step 'Style check (dotnet format)' {
        & $Dotnet format $testProject --verify-no-changes --no-restore --severity warn
    }
    Invoke-Step 'Build' { & $Dotnet build $testProject --nologo --no-restore -c Release }
    Invoke-Step 'Test' {
        & $Dotnet test $testProject --nologo --no-build -c Release `
            --collect:'XPlat Code Coverage' --results-directory $results `
            -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include='[CourtSim.Core]*'
    }

    $report = Get-ChildItem $results -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    if (-not $report) {
        throw 'Coverage report was not produced.'
    }

    [xml] $xml = Get-Content $report.FullName
    $coverage = [math]::Round([double] $xml.coverage.'line-rate' * 100, 2)
    $branch = [math]::Round([double] $xml.coverage.'branch-rate' * 100, 2)
    Write-Host ("==> CourtSim.Core coverage: {0}% lines, {1}% branches (gate: {2}% lines)" -f $coverage, $branch, $MinCoverage) -ForegroundColor Cyan
    if ($coverage -lt $MinCoverage) {
        throw "Line coverage $coverage% is below the $MinCoverage% gate."
    }

    Write-Host '==> All core checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
