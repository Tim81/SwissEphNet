#Requires -Version 7
<#
.SYNOPSIS
    Builds BaselineVerify in Release and runs it against the committed baseline.

.DESCRIPTION
    Verification always uses local mode: BaselineVerify's ProjectReference to
    SwissEphNet resolves the in-repo library, never the reference NuGet package
    (the UseReferencePackage property is never passed here). Comparisons must run
    -c Release -- see Tools/BaselineGen/README.md for why Debug is not equivalent.

    Exit code is 0 only if every area passes (exact match, within tolerance, or
    within the angle-wraparound allowance -- see Comparer.cs) after applying
    Tools/BaselineVerify/waivers.tsv.

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

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runArgs = @('run', '--project', $project, '-c', 'Release', '--no-build', '--')
if ($ReportOnly) { $runArgs += '--report-only' }

dotnet @runArgs
exit $LASTEXITCODE
