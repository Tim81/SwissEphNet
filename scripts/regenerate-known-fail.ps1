#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/conformance/known-fail.tsv from a live run of the
    correctness oracle (Tests/SwissEphNet.Conformance.Tests).

.DESCRIPTION
    Runs Tools/ConformanceKnownFailGen, which dispatches all 12,757 iterations
    in setest/t.exp against the current SwissEphNet build and writes one row
    per non-passing iteration. Requires -Reason and appends a dated,
    commit-stamped entry to Tests/conformance/regenerations.log -- the same
    shape as scripts/regenerate-baseline.ps1's provenance log, for the same
    reason: known-fail.tsv is a gate, and every time its contents change there
    must be a human-written explanation a reviewer can read without
    re-deriving it from the diff alone.

    Removing rows needs no special process or reason -- that's the gate
    finding progress and is expected to happen often. Adding a row (a
    regression, or an iteration newly covered) needs one, which is why this
    script is the only supported way to touch the file, and why it is a
    CODEOWNERS-protected path (see /Tests/conformance/ in CODEOWNERS and
    "Correctness oracle known-fail list" in CONTRIBUTING.md).

.PARAMETER Reason
    Required. A short description of why known-fail.tsv is changing (what a
    reviewer needs to understand the diff without re-deriving it): a porting
    PR that fixed N iterations, a harness fix that corrected the tolerance or
    buffer sizing for a testcase, a newly-discovered port defect, etc.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Reason
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required and must not be blank."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$genProject = Join-Path $repoRoot 'Tools\ConformanceKnownFailGen\ConformanceKnownFailGen.csproj'
$conformanceDir = Join-Path $repoRoot 'Tests\conformance'
$knownFailPath = Join-Path $conformanceDir 'known-fail.tsv'
$logPath = Join-Path $conformanceDir 'regenerations.log'

$beforeCount = 0
if (Test-Path $knownFailPath) {
    $beforeCount = (Get-Content $knownFailPath | Measure-Object -Line).Lines - 1 # minus header
}

Write-Host "Building $genProject (Release)..."
dotnet build $genProject -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Running the conformance oracle against the current build (this dispatches all 12,757 iterations; expect a few minutes)..."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
dotnet run --project $genProject -c Release --no-build -- $knownFailPath
$exitCode = $LASTEXITCODE
$stopwatch.Stop()
if ($exitCode -ne 0) { exit $exitCode }

Write-Host ("Regeneration run took {0:F1}s wall-clock." -f $stopwatch.Elapsed.TotalSeconds)

$afterCount = (Get-Content $knownFailPath | Measure-Object -Line).Lines - 1
$delta = $afterCount - $beforeCount
$deltaDescription = if ($delta -eq 0) { "no change in row count" }
elseif ($delta -lt 0) { "$([Math]::Abs($delta)) fewer rows" }
else { "$delta more rows" }

$commit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = '(uncommitted)' } else { $commit = $commit.Trim() }
$date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

$logEntry = "$date $commit ($beforeCount -> $afterCount, $deltaDescription): $Reason"
Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

Write-Host ""
Write-Host "Done. $beforeCount -> $afterCount rows ($deltaDescription)."
Write-Host "Logged to $logPath"
Write-Host ""
Write-Host "Review the diff (git diff Tests/conformance/known-fail.tsv) before committing:"
Write-Host "  - Rows removed only: progress. Confirm the removed iterations actually pass now, not that a"
Write-Host "    Check* call quietly stopped comparing them (dotnet test Tests/SwissEphNet.Conformance.Tests"
Write-Host "    would already have failed on that -- see the completeness guard in ConformanceRunner.Run)."
Write-Host "  - Rows added: a regression, or an iteration this run newly covers. Needs -Reason above to already"
Write-Host "    explain it, and a reviewer to agree before this merges (CODEOWNERS)."
