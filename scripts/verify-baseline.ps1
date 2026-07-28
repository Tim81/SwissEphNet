#Requires -Version 7
<#
.SYNOPSIS
    Builds BaselineVerify in Release and runs it against the committed baseline,
    once per TFM it targets.

.DESCRIPTION
    Verification always uses local mode: BaselineVerify's ProjectReference to
    SwissEphNet resolves the in-repo library, never the reference NuGet package
    (the UseReferencePackage property is never passed here). Comparisons must run
    -c Release -- see Tools/BaselineGen/README.md for why Debug is not equivalent.

    SwissEphNet ships three assets (netstandard2.0, net8.0, net10.0), and they are
    not guaranteed to behave identically -- see the "Why net8.0 and net10.0" note
    in Tools/BaselineGen/README.md. BaselineVerify and BaselineMatrix both
    multi-target net8.0;net10.0 so this script exercises both of the modern-.NET
    assets, not just whichever one a plain `dotnet build`/`dotnet run` would have
    resolved. Each TFM is built and run separately and reported as its own
    section; the overall exit code is 0 only if every TFM passes.

    Exit code is 0 only if every area, for every TFM, passes (exact match, within
    tolerance, or within the angle-wraparound allowance -- see Comparer.cs) after
    applying Tests/baseline/waivers.tsv.

    The baseline was generated on Windows and this gate is locked to Windows; see
    Tools/BaselineGen/README.md for the measured cross-platform divergence and why
    the fix was a platform lock, not a looser tolerance.

.PARAMETER ReportOnly
    Runs the same comparison but never fails: prints a divergence distribution
    (fields differing, relative-difference median/p90/p99/max, per-area exact vs
    within-tolerance vs beyond breakdown) and always exits 0. Used by the
    non-blocking cross-platform CI job to track drift over time without gating on
    it -- not for local verification.
#>

param(
    [switch]$ReportOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Tools\BaselineVerify\BaselineVerify.csproj'

# Keep in sync with BaselineVerify.csproj's <TargetFrameworks>. netstandard2.0
# is not included: any modern host resolves net8.0 or net10.0 in preference to
# it, so there is no way to make `dotnet run` actually execute the
# netstandard2.0 asset without a separate .NET Framework (or similarly old)
# host leg. That would cost more to build and maintain right now than the
# coverage is worth; see Tools/BaselineGen/README.md.
$tfms = @('net8.0', 'net10.0')

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$overallExitCode = 0
$results = @()

foreach ($tfm in $tfms) {
    Write-Host ""
    Write-Host "================================================================================"
    Write-Host " TFM: $tfm"
    Write-Host "================================================================================"

    $runArgs = @('run', '--project', $project, '-c', 'Release', '-f', $tfm, '--no-build', '--')
    if ($ReportOnly) { $runArgs += '--report-only' }

    dotnet @runArgs
    $tfmExitCode = $LASTEXITCODE

    $results += [pscustomobject]@{ TargetFramework = $tfm; ExitCode = $tfmExitCode }
    if ($tfmExitCode -ne 0) { $overallExitCode = 1 }
}

Write-Host ""
Write-Host "================================================================================"
Write-Host " Per-TFM summary"
Write-Host "================================================================================"
foreach ($result in $results) {
    $status = if ($result.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0,-12} {1}" -f $result.TargetFramework, $status)
}

exit $overallExitCode
