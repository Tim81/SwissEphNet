#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/conformance/known-fail.tsv from a live run of the
    correctness oracle (Tests/SwissEphNet.Conformance.Tests).

.DESCRIPTION
    Runs Tools/ConformanceKnownFailGen, which dispatches all 12,757 iterations
    in setest/t.exp against the current SwissEphNet build and writes one row
    per non-passing iteration.

    Two modes:

    Default (full regenerate): overwrites known-fail.tsv wholesale with
    whatever the current run produces -- rows can be removed (progress) or
    added (a regression, or an iteration newly covered) in the same run.
    Because it can add rows silently, it requires -Reason, and the row-count
    delta is appended to Tests/conformance/regenerations.log so a reviewer has
    a human-written explanation without re-deriving it from the diff. This is
    also the gate's own bypass: someone could use it to make a red gate green
    by writing the failure into the list instead of fixing it. Use -PruneOnly
    below when all you want to do is take newly-passing rows off the list --
    it cannot add anything, so it does not carry that risk and needs no
    -Reason.

    -PruneOnly: removes rows that now pass; refuses (non-zero exit, no file
    changes) if the current run would add or change the category of any row.
    "Adding a row is a deliberate, separate act" -- see CONTRIBUTING.md,
    "Correctness oracle known-fail list" -- and this mode is how that is
    enforced mechanically instead of by convention alone: a contributor who
    only wants to record progress cannot use this mode to also slip in an
    unreviewed new failure, because it will not run at all if one is present.

    Removing rows needs no special process or reason -- that's the gate
    finding progress and is expected to happen often. Adding a row (a
    regression, or an iteration newly covered) needs one, which is why this
    script is the only supported way to touch the file, and why it is a
    CODEOWNERS-protected path (see /Tests/conformance/ in CODEOWNERS and
    "Correctness oracle known-fail list" in CONTRIBUTING.md).

.PARAMETER Reason
    Required in default mode, ignored in -PruneOnly mode (pruning needs no
    justification). A short description of why known-fail.tsv is changing
    (what a reviewer needs to understand the diff without re-deriving it): a
    porting PR that fixed N iterations, a harness fix that corrected the
    tolerance or buffer sizing for a testcase, a newly-discovered port defect,
    etc.

.PARAMETER PruneOnly
    Only remove newly-passing rows; never add or recategorize one. Exits
    non-zero and leaves known-fail.tsv untouched if the current run would add
    a row or change an existing row's category -- see DESCRIPTION.

.PARAMETER PR
    Optional. The pull request number this regeneration belongs to, e.g. "16".
    release/2.10.03's convention is to cite PR numbers rather than commit SHAs
    in this log, because PRs here are squash-merged: a SHA captured while a
    branch is still open (as this script necessarily must, since it runs
    before the commit that carries the change exists) names a commit that
    will not exist once the PR merges, and worse, is trivially misread as
    "the commit this entry describes" when it is actually always the *parent*
    of that commit. A PR number does not have this problem -- it is assigned
    when the PR is opened and is stable across the squash. If you do not know
    it yet (e.g. regenerating locally before opening the PR), omit this and
    fill in the logged line by hand once you do, before the PR merges.
#>

param(
    [string]$Reason,

    [switch]$PruneOnly,

    [string]$PR
)

$ErrorActionPreference = 'Stop'

if (-not $PruneOnly -and [string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required in default (full regenerate) mode. Use -PruneOnly if you only want to remove newly-passing rows."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$genProject = Join-Path $repoRoot 'Tools\ConformanceKnownFailGen\ConformanceKnownFailGen.csproj'
$conformanceDir = Join-Path $repoRoot 'Tests\conformance'
$knownFailPath = Join-Path $conformanceDir 'known-fail.tsv'
$logPath = Join-Path $conformanceDir 'regenerations.log'

function Get-RowCount {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    return (Get-Content $Path | Measure-Object -Line).Lines - 1 # minus header
}

function Read-KnownFailTable {
    # Keyed by "suite`ttestcase`titeration" -> category. Plain tab-split, not
    # Import-Csv: the "reason" column can itself contain characters Import-Csv
    # would need quoting rules for that this TSV format does not use.
    param([string]$Path)
    $table = @{}
    if (-not (Test-Path $Path)) { return $table }
    $lines = Get-Content $Path
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        $key = "$($cols[0])`t$($cols[1])`t$($cols[2])"
        $table[$key] = $cols[3]
    }
    return $table
}

Write-Host "Building $genProject (Release)..."
dotnet build $genProject -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($PruneOnly) {
    $tempPath = [System.IO.Path]::GetTempFileName()
    try {
        Write-Host "Running the conformance oracle against the current build (dispatches all 12,757 iterations; expect a few minutes)..."
        dotnet run --project $genProject -c Release --no-build -- $tempPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $current = Read-KnownFailTable -Path $knownFailPath
        $fresh = Read-KnownFailTable -Path $tempPath

        $added = @()
        $changed = @()
        foreach ($key in $fresh.Keys) {
            if (-not $current.ContainsKey($key)) {
                $added += $key
            }
            elseif ($current[$key] -ne $fresh[$key]) {
                $changed += $key
            }
        }

        if ($added.Count -gt 0 -or $changed.Count -gt 0) {
            Write-Host ""
            Write-Host "-PruneOnly refuses: the current run would add or recategorize a row, and this mode can only remove rows." -ForegroundColor Red
            if ($added.Count -gt 0) {
                Write-Host ""
                Write-Host "Would ADD $($added.Count) row(s) (new failure, or an iteration not previously covered):"
                foreach ($key in $added | Select-Object -First 50) {
                    Write-Host "  $($key -replace "`t", '.')  [$($fresh[$key])]"
                }
            }
            if ($changed.Count -gt 0) {
                Write-Host ""
                Write-Host "Would RECATEGORIZE $($changed.Count) row(s) (category drift -- still failing, but not the same failure):"
                foreach ($key in $changed | Select-Object -First 50) {
                    Write-Host "  $($key -replace "`t", '.')  $($current[$key]) -> $($fresh[$key])"
                }
            }
            Write-Host ""
            Write-Host "known-fail.tsv was NOT modified. Adding or recategorizing a row is a deliberate, separate act:"
            Write-Host "run the full regenerate (scripts/regenerate-known-fail.ps1 -Reason `"...`") once you have understood and reviewed it."
            exit 1
        }

        $beforeCount = $current.Count
        Copy-Item -Path $tempPath -Destination $knownFailPath -Force
        $afterCount = $fresh.Count
        $removed = $beforeCount - $afterCount

        $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }
        $reasonText = if ($Reason) { $Reason } else { "Pruned $removed newly-passing row(s); no reason required for a pure removal." }
        $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $removed fewer rows): $reasonText"
        Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

        Write-Host ""
        Write-Host "Done (prune-only). $beforeCount -> $afterCount rows ($removed fewer)."
        Write-Host "Logged to $logPath"
    }
    finally {
        Remove-Item -Path $tempPath -ErrorAction SilentlyContinue
    }

    exit 0
}

$beforeCount = Get-RowCount -Path $knownFailPath

Write-Host "Running the conformance oracle against the current build (this dispatches all 12,757 iterations; expect a few minutes)..."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
dotnet run --project $genProject -c Release --no-build -- $knownFailPath
$exitCode = $LASTEXITCODE
$stopwatch.Stop()
if ($exitCode -ne 0) { exit $exitCode }

Write-Host ("Regeneration run took {0:F1}s wall-clock." -f $stopwatch.Elapsed.TotalSeconds)

$afterCount = Get-RowCount -Path $knownFailPath
$delta = $afterCount - $beforeCount
$deltaDescription = if ($delta -eq 0) { "no change in row count" }
elseif ($delta -lt 0) { "$([Math]::Abs($delta)) fewer rows" }
else { "$delta more rows" }

$date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
$prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }

$logEntry = "$date $prCitation ($beforeCount -> $afterCount, $deltaDescription): $Reason"
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
Write-Host "    explain it, and a reviewer to agree before this merges (CODEOWNERS). Prefer -PruneOnly instead"
Write-Host "    of this default mode when all you actually did was remove rows -- it cannot add one by accident."
