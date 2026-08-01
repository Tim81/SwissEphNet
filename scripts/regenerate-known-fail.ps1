#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/conformance/known-fail.tsv from a live run of the
    correctness oracle (Tests/SwissEphNet.Conformance.Tests).

.DESCRIPTION
    Runs Tools/ConformanceKnownFailGen, which dispatches all 12,757 iterations
    in setest/t.exp against the current SwissEphNet build and writes one row
    per non-passing iteration.

    Every regeneration runs Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisManifest's
    check first (via ConformanceRunner.Run, shared with the actual test run): if the resolved
    ephemeris directory does not contain exactly the files
    Tests/conformance/required-ephemeris-files.tsv declares -- no fewer, no more -- this
    script refuses outright before touching known-fail.tsv. A full, non-sparse
    'git submodule update --init external/swisseph' (378 MB, every era file) is the most
    likely way to trip this: some iterations only pass because of a file the manifest does
    not declare, so regenerating against that tree produces a list that looks right locally
    and is wrong for CI and everyone else. Each log entry below also records how many
    ephemeris files were present, for the same reason the PR-number convention exists: a
    future reader needs to know what this list was generated against, not just when.

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
    changes) if the current run would add or change the category or
    magnitude_key of any row. "Adding a row is a deliberate, separate act" --
    see CONTRIBUTING.md, "Correctness oracle known-fail list" -- and this
    mode is how that is enforced mechanically instead of by convention
    alone: a contributor who only wants to record progress cannot use this
    mode to also slip in an unreviewed new failure, because it will not run
    at all if one is present.

    A row this mode keeps is written back exactly as it already reads in
    known-fail.tsv -- category, magnitude_key and reason all untouched --
    never from the fresh run's own output. Earlier versions of this mode
    copied the fresh run's file over known-fail.tsv wholesale once the
    added/changed check passed, which is not the same thing: reason text is
    regenerated fresh on every run and its wording drifts even when nothing
    about the underlying failure changed, so that copy silently rewrote
    every surviving row's reason on every prune (see commit 2bf3396: 12
    deletions and 8 insertions where only 4 rows were pruned). A prune is
    now genuinely only a removal -- no surviving row's reason or
    magnitude_key can change through this mode, by construction, not by a
    comparison this mode might get wrong.

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

function Get-EpheDescription {
    # Records which ephemeris data set this regeneration ran against, in the log entry
    # itself -- see Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisManifest.cs's
    # remarks for why this matters: a full, non-sparse submodule checkout (378 MB, every
    # era file) silently changes some iterations' outcome relative to the declared ~4.2 MB
    # core set, so "the list was generated against the core set" is provenance a future
    # reader needs, not an implementation detail. ConformanceRunner.Run already refused
    # (before this function is ever called) if EpheDir did not match the manifest exactly,
    # so by the time a log entry is written, it is always describing the declared set --
    # this just names the count for a reader who has not read that source.
    $epheDir = $env:SWISSEPH_CONFORMANCE_EPHE
    if (-not $epheDir) { $epheDir = Join-Path $repoRoot 'external\swisseph\ephe' }
    $count = 0
    if (Test-Path $epheDir) {
        $count = (Get-ChildItem -Path $epheDir -File | Measure-Object).Count
    }
    return "ephe: $count files matching Tests/conformance/required-ephemeris-files.tsv"
}

function Read-KnownFailTable {
    # Keyed by "suite`ttestcase`titeration" -> "category`tmagnitude_key". Plain tab-split, not
    # Import-Csv: the "reason" column can itself contain characters Import-Csv would need quoting
    # rules for that this TSV format does not use. Deliberately excludes the reason column from
    # the value: this table is only ever used to detect whether a row was added, or its category
    # or magnitude_key changed, between two runs (see the -PruneOnly block below and item 4's
    # magnitude gate in Tests/SwissEphNet.Conformance.Tests/ConformanceReport.cs) -- reason text
    # is regenerated fresh every run and compared nowhere, the same posture ConformanceReport.Build
    # takes for the live gate.
    param([string]$Path)
    $table = @{}
    if (-not (Test-Path $Path)) { return $table }
    $lines = Get-Content $Path
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        $key = "$($cols[0])`t$($cols[1])`t$($cols[2])"
        $table[$key] = "$($cols[3])`t$($cols[4])"
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

        # Vacuity floor: $fresh with zero rows and $current with more than zero looks exactly like
        # "every known failure now passes" to the logic below -- $added and $changed both stay
        # empty (nothing in $fresh to iterate), so the refusal a few lines down never fires, and
        # every surviving-row check afterward finds nothing in $fresh to survive, pruning the
        # entire list. That is indistinguishable, from this script's point of view, from the
        # generator having silently failed to dispatch the corpus at all (a crash before the first
        # entry was added, a misconfigured environment producing zero results) -- and the whole
        # point of -PruneOnly is that it should never need a human to eyeball the result before
        # trusting it. Refusing here is a deliberate choice, not an oversight: if the port
        # genuinely reaches zero outstanding known failures, that transition is worth a
        # human-reviewed -Reason in the default (full regenerate) mode below, the same way any
        # other change this large already requires one, rather than passing silently through the
        # one mode designed to need no review at all.
        if ($fresh.Count -eq 0 -and $current.Count -gt 0) {
            Write-Host ""
            Write-Host "-PruneOnly refuses: the current run produced zero known-fail rows across the entire corpus dispatch, while $knownFailPath currently has $($current.Count)." -ForegroundColor Red
            Write-Host "Treating this as 'everything now passes' and pruning the whole list is indistinguishable here from the generator run having failed to actually dispatch the corpus -- see this script's own comment above this check." -ForegroundColor Red
            Write-Host "known-fail.tsv was NOT modified. If the port genuinely has zero outstanding known failures now, use the full regenerate (scripts/regenerate-known-fail.ps1 -Reason `"...`") instead, which requires a human-reviewed reason for a change this size."
            exit 1
        }

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
                    $freshCategory, $freshMagnitude = $fresh[$key] -split "`t"
                    Write-Host "  $($key -replace "`t", '.')  [$freshCategory magnitude_key=$freshMagnitude]"
                }
            }
            if ($changed.Count -gt 0) {
                Write-Host ""
                Write-Host "Would RECATEGORIZE $($changed.Count) row(s) (category or magnitude_key drift -- still failing, but not the same failure):"
                foreach ($key in $changed | Select-Object -First 50) {
                    $currentCategory, $currentMagnitude = $current[$key] -split "`t"
                    $freshCategory, $freshMagnitude = $fresh[$key] -split "`t"
                    Write-Host "  $($key -replace "`t", '.')  $currentCategory magnitude_key=$currentMagnitude -> $freshCategory magnitude_key=$freshMagnitude"
                }
            }
            Write-Host ""
            Write-Host "known-fail.tsv was NOT modified. Adding or recategorizing a row is a deliberate, separate act:"
            Write-Host "run the full regenerate (scripts/regenerate-known-fail.ps1 -Reason `"...`") once you have understood and reviewed it."
            exit 1
        }

        # Every surviving row is written back exactly as it already reads in $knownFailPath, not
        # from $tempPath: $fresh (the live run) is consulted only for its key set above, never for
        # its own reason or magnitude_key text. ConformanceKnownFailGen regenerates reason text
        # fresh on every run -- wording drifts even when nothing about the underlying failure
        # changed -- so copying $tempPath wholesale, as this block used to, silently rewrote every
        # surviving row's reason on every prune (and would now do the same to magnitude_key). A
        # prune genuinely only removes lines: no row this block keeps is rewritten in any column.
        $currentRawLines = Get-Content -LiteralPath $knownFailPath
        $header = $currentRawLines[0]
        $survivingLines = [System.Collections.Generic.List[string]]::new()
        for ($i = 1; $i -lt $currentRawLines.Count; $i++) {
            $line = $currentRawLines[$i]
            if ([string]::IsNullOrEmpty($line)) { continue }
            $cols = $line -split "`t"
            $key = "$($cols[0])`t$($cols[1])`t$($cols[2])"
            if ($fresh.ContainsKey($key)) {
                [void]$survivingLines.Add($line)
            }
        }

        $beforeCount = $current.Count
        $afterCount = $survivingLines.Count
        $removed = $beforeCount - $afterCount
        $outputLines = @($header) + $survivingLines
        [System.IO.File]::WriteAllText($knownFailPath, (($outputLines -join "`n") + "`n"))

        $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }
        $reasonText = if ($Reason) { $Reason } else { "Pruned $removed newly-passing row(s); no reason required for a pure removal." }
        $epheDescription = Get-EpheDescription
        $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $removed fewer rows) [$epheDescription]: $reasonText"
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

# Staged to a temp file, then moved over $knownFailPath only once the generator has exited 0 --
# matching -PruneOnly's own $tempPath pattern above, rather than the generator writing straight
# over the committed file the way this block used to. dotnet run passed $knownFailPath directly
# meant the committed file was the generator's own output target: a crash partway through writing
# it (an unhandled exception after KnownFailList.Save has started, an OOM, a killed process, a
# disk-full condition) left a truncated or corrupted file sitting in the working tree with no
# original content to fall back to -- there was nothing to revert to, because the original had
# already been overwritten in place. Generating into a temp path first means a failed run leaves
# $knownFailPath completely untouched, exactly like -PruneOnly already guarantees.
$tempKnownFailPath = [System.IO.Path]::GetTempFileName()
try {
    Write-Host "Running the conformance oracle against the current build (this dispatches all 12,757 iterations; expect a few minutes)..."
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet run --project $genProject -c Release --no-build -- $tempKnownFailPath
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    if ($exitCode -ne 0) { exit $exitCode }

    Write-Host ("Regeneration run took {0:F1}s wall-clock." -f $stopwatch.Elapsed.TotalSeconds)

    $afterCount = Get-RowCount -Path $tempKnownFailPath
    $delta = $afterCount - $beforeCount
    $deltaDescription = if ($delta -eq 0) { "no change in row count" }
    elseif ($delta -lt 0) { "$([Math]::Abs($delta)) fewer rows" }
    else { "$delta more rows" }

    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }
    $epheDescription = Get-EpheDescription

    # Only now, with a complete and exit-0 generator run sitting safely in a temp file, does
    # anything under Tests/conformance/ get touched.
    Copy-Item -LiteralPath $tempKnownFailPath -Destination $knownFailPath -Force

    $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $deltaDescription) [$epheDescription]: $Reason"
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
}
finally {
    Remove-Item -Path $tempKnownFailPath -ErrorAction SilentlyContinue
}
