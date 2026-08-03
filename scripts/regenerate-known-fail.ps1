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

.PARAMETER SelfTest
    Exercises this script's own pure functions (Read-KnownFailTable, Get-SurvivingLines,
    Get-KnownFailPruneOnlyRefusal) against synthetic fixtures, plus one real child-process
    invocation asserting -Reason is required outside -PruneOnly. Never builds
    Tools/ConformanceKnownFailGen, never dispatches the conformance corpus, and never touches
    Tests/conformance/known-fail.tsv.
#>

param(
    [string]$Reason,

    [switch]$PruneOnly,

    [string]$PR,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SelfTest -and -not $PruneOnly -and [string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required in default (full regenerate) mode. Use -PruneOnly if you only want to remove newly-passing rows."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$genProject = Join-Path $repoRoot 'Tools/ConformanceKnownFailGen/ConformanceKnownFailGen.csproj'
$conformanceDir = Join-Path $repoRoot 'Tests/conformance'
$knownFailPath = Join-Path $conformanceDir 'known-fail.tsv'
$logPath = Join-Path $conformanceDir 'regenerations.log'

# Below this ratio, -PruneOnly refuses rather than writing the prune -- HIGH 4's own fix. The prior
# floor only fired at exactly $fresh.Count -eq 0 (see Get-KnownFailPruneOnlyRefusal below), which a
# corpus shrunk out from under this script (SWISSEPH_CONFORMANCE_SUBMODULE pointed at a t.exp with
# the same 60 testcases but fewer iterations) sails straight past: $fresh becomes a strict, small,
# but nonzero subset of $current, so zero rows are added or recategorized and the exact-zero check
# never sees it -- demonstrated directly, a corpus shrunk this way pruned 1,422 of 1,423 rows with
# no -Reason, logged as an ordinary pure removal. Calibrated against this repository's own
# regenerations.log: the smallest survival ratio any legitimate -PruneOnly run has ever produced is
# 1441/2521 (~57.2%, the 2026-07-30 entry); 0.10 leaves that, and every other real prune in the
# log, comfortably clear while still catching a run that leaves only a sliver of the list standing
# (the demonstrated bypass survives at ~0.07%).
$PruneOnlySurvivalRatioFloor = 0.10

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
    if (-not $epheDir) { $epheDir = Join-Path $repoRoot 'external/swisseph/ephe' }
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
    # or magnitude_key changed, between two runs (see Get-KnownFailPruneOnlyRefusal below and item
    # 4's magnitude gate in Tests/SwissEphNet.Conformance.Tests/ConformanceReport.cs) -- reason
    # text is regenerated fresh every run and compared nowhere, the same posture
    # ConformanceReport.Build takes for the live gate.
    #
    # Keyed with an ordinal (case-sensitive) comparer, not PowerShell's `@{}` default
    # (case-insensitive, culture-aware) -- matching scripts/regenerate-oracle-known-diff.ps1's own
    # Read-KnownDiffTable and scripts/classify-oracle-versions.ps1's own Read-ClassificationTable,
    # both converted for the identical reason: measured against those scripts' own case_id
    # namespace, 396 case-only collisions collapsed last-write-wins under the default comparer.
    # known-fail.tsv's own key (suite/testcase/iteration) is numeric, not case-bearing, so this
    # table has no live collision to point to the way its two siblings do -- but every other table
    # keyed off a TSV in this codebase already made the ordinal switch, and a numeric key today
    # does not guarantee one forever.
    #
    # Column-count checked explicitly, matching scripts/regenerate-oracle-known-diff.ps1's own
    # Read-KnownDiffTable: PowerShell array indexing past the end returns $null rather than
    # throwing, so a truncated/malformed row silently read as $cols[3]/$cols[4] = $null before this
    # check existed, rather than failing loudly on a row this script's own -PruneOnly refusal guard
    # trusts to decide "did this get better or worse".
    param([string]$Path)
    $table = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path $Path)) { return $table }
    $lines = @(Get-Content $Path)
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        if ($cols.Count -ne 6) {
            throw "$Path`:$($i + 1): expected 6 tab-separated columns (suite, testcase, iteration, category, magnitude_key, reason), got $($cols.Count): '$line'"
        }
        $key = "$($cols[0])`t$($cols[1])`t$($cols[2])"
        $table[$key] = "$($cols[3])`t$($cols[4])"
    }
    return $table
}

# The surviving-lines computation a real -PruneOnly write performs, factored out so -SelfTest can
# exercise it directly against synthetic fixtures without building or running the generator. Pure:
# takes the raw current-file lines (element 0 is the header) and the fresh run's key table, returns
# the data lines (header excluded) that survive -- i.e. every line whose key is still present in
# $Fresh.
#
# HIGH 1 fix: $CurrentRawLines must already be an array by the time this function receives it --
# see this script's own real -PruneOnly call site below for why. This function does not re-wrap its
# own parameter, deliberately: a [string[]]-typed parameter already coerces a bare scalar string
# into a one-element array on the way in, so the header-only bug this function exists to fix would
# be invisible to a self-test that only ever calls this function directly. The wrap has to happen
# at the Get-Content call site, which is exactly where it is (and is exercised by the
# Get-Content-based self-test case below, not by this function's own signature).
function Get-SurvivingLines {
    param([string[]]$CurrentRawLines, [System.Collections.Generic.Dictionary[string, string]]$Fresh)
    $survivingLines = [System.Collections.Generic.List[string]]::new()
    for ($i = 1; $i -lt $CurrentRawLines.Count; $i++) {
        $line = $CurrentRawLines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        $key = "$($cols[0])`t$($cols[1])`t$($cols[2])"
        if ($Fresh.ContainsKey($key)) {
            [void]$survivingLines.Add($line)
        }
    }
    return , @($survivingLines.ToArray())
}

# The -PruneOnly refusal guard itself, factored out of the real run below so -SelfTest can exercise
# it directly. Returns a structured verdict: which rows would be added or recategorized (if any),
# whether the ratio floor fired, and the survival ratio itself for the message.
function Get-KnownFailPruneOnlyRefusal {
    param(
        [System.Collections.Generic.Dictionary[string, string]]$Current,
        [System.Collections.Generic.Dictionary[string, string]]$Fresh
    )

    # Ratio floor -- HIGH 4's own fix. $Fresh surviving at less than $PruneOnlySurvivalRatioFloor
    # of $Current's size looks, to the added/changed loop below, exactly like "everything on the
    # list is either gone or still exactly as recorded" -- nothing in that shrunken $Fresh is
    # flagged as added or recategorized, so the ordinary refusal never fires, and the surviving-row
    # computation prunes almost the entire list. That is indistinguishable, from this script's own
    # point of view, from the generator run having silently dispatched a smaller corpus than the
    # committed known-fail.tsv was measured against (SWISSEPH_CONFORMANCE_SUBMODULE redirecting
    # setest/t.exp to a smaller one; a crash partway through the corpus that still exits 0) -- see
    # this script's own $PruneOnlySurvivalRatioFloor comment above for how the threshold was
    # calibrated against this repository's real prune history. Only evaluated when $Current has
    # rows at all: a grid that legitimately already has zero known failures, freshly regenerated to
    # confirm it still has zero, must not trip this (division by zero aside, 0/0 is not a shrinkage).
    if ($Current.Count -gt 0) {
        $survivalRatio = $Fresh.Count / $Current.Count
        if ($survivalRatio -lt $PruneOnlySurvivalRatioFloor) {
            return [pscustomobject]@{
                Added        = @()
                Changed      = @()
                Any          = $true
                RatioFloor   = $true
                SurvivalRatio = $survivalRatio
                CurrentCount = $Current.Count
                FreshCount   = $Fresh.Count
            }
        }
    }

    $added = @()
    $changed = @()
    foreach ($key in $Fresh.Keys) {
        if (-not $Current.ContainsKey($key)) {
            $added += $key
        }
        elseif ($Current[$key] -ne $Fresh[$key]) {
            $changed += $key
        }
    }

    return [pscustomobject]@{
        Added         = $added
        Changed       = $changed
        Any           = ($added.Count -gt 0 -or $changed.Count -gt 0)
        RatioFloor    = $false
        SurvivalRatio = if ($Current.Count -eq 0) { 1.0 } else { $Fresh.Count / $Current.Count }
        CurrentCount  = $Current.Count
        FreshCount    = $Fresh.Count
    }
}

# ---------------------------------------------------------------------------------------------
# Self-test. Never builds Tools/ConformanceKnownFailGen, never dispatches the conformance corpus,
# never touches Tests/conformance/known-fail.tsv. Exercises the three pure functions above against
# synthetic fixtures, plus one real child-process invocation of the -Reason requirement.
# ---------------------------------------------------------------------------------------------

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ('regenerate-known-fail-selftest-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null

    function Assert-True {
        param([string] $Case, [bool] $Condition, [string] $Detail = '')
        if ($Condition) { Write-Host "  PASS  $Case" -ForegroundColor DarkGray }
        else { Write-Host "  FAIL  $Case`n          $Detail" -ForegroundColor Red; $script:failures++ }
    }

    try {
        Write-Host 'regenerate-known-fail self-test'
        Write-Host ''

        function New-FreshTable {
            param([hashtable] $Rows)
            $t = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
            foreach ($k in $Rows.Keys) { $t[$k] = $Rows[$k] }
            return $t
        }

        # 1. HIGH 1, reproduced against a REAL file read, not a hand-built array: a known-fail.tsv
        #    containing only the header line. Before the @()-wrap fix, `Get-Content` on a
        #    single-line file returns a bare [string], not a one-element array, so indexing it
        #    with [0] gives the first CHARACTER of the header line, not the header line itself,
        #    and iterating ".Count" times over it walks the header's own characters instead of
        #    zero data rows. @()-wrapped, a one-line file reads as a one-element array whose sole
        #    element is the header, exactly like a multi-line file's element 0.
        $headerOnlyPath = Join-Path $lab 'header-only-known-fail.tsv'
        $headerLine = 'suite	testcase	iteration	category	magnitude_key	reason'
        [System.IO.File]::WriteAllText($headerOnlyPath, "$headerLine`n", (New-Object System.Text.UTF8Encoding($false)))
        $rawLines = @(Get-Content -LiteralPath $headerOnlyPath)
        Assert-True 'a header-only file, @()-wrapped, reads as a one-element array' `
            ($rawLines.Count -eq 1 -and $rawLines[0] -eq $headerLine) `
            "got Count=$($rawLines.Count) element0='$($rawLines[0])'"

        $freshEmpty = New-FreshTable @{}
        $survivingFromHeaderOnly = Get-SurvivingLines -CurrentRawLines $rawLines -Fresh $freshEmpty
        Assert-True 'Get-SurvivingLines on a header-only current file yields zero surviving data lines (not the header exploded into characters)' `
            ($survivingFromHeaderOnly.Count -eq 0) "got Count=$($survivingFromHeaderOnly.Count): $($survivingFromHeaderOnly -join '|')"

        # 2. Control: a real multi-row file, some rows surviving, some not.
        $multiRowLines = @(
            $headerLine
            "1`t1`t1`tVALUE-MISMATCH`t-4`treason a"
            "1`t1`t2`tVALUE-MISMATCH`t-4`treason b"
            "2`t1`t1`tDATA-MISSING`tn/a`treason c"
        )
        $freshTwoSurvive = New-FreshTable @{
            "1`t1`t1" = "VALUE-MISMATCH`t-4"
            "2`t1`t1" = "DATA-MISSING`tn/a"
        }
        $surviving = Get-SurvivingLines -CurrentRawLines $multiRowLines -Fresh $freshTwoSurvive
        Assert-True 'Get-SurvivingLines keeps exactly the rows whose key is still in $Fresh' `
            ($surviving.Count -eq 2 -and $surviving[0] -match '^1\t1\t1\t' -and $surviving[1] -match '^2\t1\t1\t') `
            "got: $($surviving -join ' || ')"

        # 3. HIGH 4: the exact-zero floor's own blind spot. $Current has many rows; $Fresh is a
        #    small, strict, NONZERO subset -- zero added, zero changed, so the pre-fix exact-zero
        #    check ($Fresh.Count -eq 0) never fired. This is the corpus-shrinkage bypass: a t.exp
        #    with the same testcases but fewer iterations makes $fresh a strict subset of $current.
        $bigCurrent = New-FreshTable (@{}); for ($i = 1; $i -le 1423; $i++) { $bigCurrent["1`t1`t$i"] = "VALUE-MISMATCH`t-4" }
        $tinyFresh = New-FreshTable @{ "1`t1`t1" = "VALUE-MISMATCH`t-4" }
        $refusal = Get-KnownFailPruneOnlyRefusal -Current $bigCurrent -Fresh $tinyFresh
        Assert-True 'a strict, nonzero, near-total subset (1 of 1423 survives) is refused by the ratio floor, not silently accepted' `
            ($refusal.Any -and $refusal.RatioFloor) `
            "got Any=$($refusal.Any) RatioFloor=$($refusal.RatioFloor) SurvivalRatio=$($refusal.SurvivalRatio)"

        # 4. The original exact-zero case must still be refused (the ratio floor subsumes it: 0/N
        #    is always below any positive threshold).
        $refusalZero = Get-KnownFailPruneOnlyRefusal -Current $bigCurrent -Fresh (New-FreshTable @{})
        Assert-True 'the original exact-zero case is still refused (subsumed by the ratio floor)' `
            ($refusalZero.Any -and $refusalZero.RatioFloor)

        # 5. Control for both floors: both sides genuinely empty (a grid with zero outstanding
        #    known failures, freshly reconfirmed) must NOT trip the floor.
        $refusalBothEmpty = Get-KnownFailPruneOnlyRefusal -Current (New-FreshTable @{}) -Fresh (New-FreshTable @{})
        Assert-True 'both sides empty (already zero known failures) is accepted, not treated as vacuous' `
            (-not $refusalBothEmpty.Any)

        # 6. Calibration control: the smallest survival ratio any real -PruneOnly run in this
        #    repository's own regenerations.log has ever produced (1441/2521, ~57.2%, the
        #    2026-07-30 entry) must NOT trip the floor -- otherwise the floor would refuse
        #    legitimate, already-merged history along with the bypass it exists to catch.
        $calibCurrent = New-FreshTable (@{}); for ($i = 1; $i -le 2521; $i++) { $calibCurrent["1`t1`t$i"] = "VALUE-MISMATCH`t-4" }
        $calibFresh = New-FreshTable (@{}); for ($i = 1; $i -le 1441; $i++) { $calibFresh["1`t1`t$i"] = "VALUE-MISMATCH`t-4" }
        $refusalCalib = Get-KnownFailPruneOnlyRefusal -Current $calibCurrent -Fresh $calibFresh
        Assert-True 'a real, already-merged prune ratio (1441/2521, ~57.2%) is accepted, not refused' `
            (-not $refusalCalib.Any) "got Any=$($refusalCalib.Any) SurvivalRatio=$($refusalCalib.SurvivalRatio)"

        # 7. The ordinary refusal (a row added, or an existing row's category/magnitude_key
        #    changed) must still fire when the ratio floor itself is not implicated.
        $currentSmall = New-FreshTable @{ "1`t1`t1" = "VALUE-MISMATCH`t-4" }
        $freshAdded = New-FreshTable @{ "1`t1`t1" = "VALUE-MISMATCH`t-4"; "1`t1`t2" = "VALUE-MISMATCH`t-3" }
        $refusalAdded = Get-KnownFailPruneOnlyRefusal -Current $currentSmall -Fresh $freshAdded
        Assert-True 'a newly-added row is refused (ratio floor not implicated)' `
            ($refusalAdded.Any -and -not $refusalAdded.RatioFloor -and $refusalAdded.Added.Count -eq 1)

        $freshChanged = New-FreshTable @{ "1`t1`t1" = "VALUE-MISMATCH`t-3" }
        $refusalChanged = Get-KnownFailPruneOnlyRefusal -Current $currentSmall -Fresh $freshChanged
        Assert-True 'a recategorized/magnitude-changed row is refused (ratio floor not implicated)' `
            ($refusalChanged.Any -and -not $refusalChanged.RatioFloor -and $refusalChanged.Changed.Count -eq 1)

        # 8. MEDIUM 7: a truncated row (fewer than 6 tab-separated columns) must throw rather than
        #    silently reading as category/magnitude_key = $null.
        $truncatedPath = Join-Path $lab 'truncated-known-fail.tsv'
        [System.IO.File]::WriteAllText($truncatedPath, "$headerLine`n1`t1`t1`n", (New-Object System.Text.UTF8Encoding($false)))
        $threwOnTruncated = $false
        $truncatedMessage = $null
        try { Read-KnownFailTable -Path $truncatedPath | Out-Null }
        catch { $threwOnTruncated = $true; $truncatedMessage = $_.Exception.Message }
        Assert-True 'a truncated row (fewer than 6 columns) throws rather than silently reading as $null fields' `
            ($threwOnTruncated -and $truncatedMessage -match 'expected 6 tab-separated columns') `
            "threw=$threwOnTruncated message=$truncatedMessage"

        # 9. MEDIUM 7: -Reason is required outside -PruneOnly, refused before any build runs. Run
        #    as a real child-process invocation with neither -Reason nor -PruneOnly nor -SelfTest,
        #    matching scripts/regenerate-netstandard-compat-known-diff.ps1's own self-test pattern
        #    for the identical guard.
        $pwshExe = (Get-Process -Id $PID).Path
        $output = & $pwshExe -NoProfile -File $PSCommandPath *>&1
        $code = $LASTEXITCODE
        $text = (@($output) -join "`n")
        Assert-True '-Reason is required outside -PruneOnly and -SelfTest, refused before any build' `
            ($code -ne 0 -and $text -match '-Reason is required') "exit=$code output: $text"

        Write-Host ''
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all regenerate-known-fail self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------
# Real run.
# ---------------------------------------------------------------------------------------------

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

        # Refusal guard (HIGH 4's ratio floor, plus the ordinary added/recategorized check) --
        # factored into Get-KnownFailPruneOnlyRefusal above so -SelfTest can exercise it directly.
        $refusal = Get-KnownFailPruneOnlyRefusal -Current $current -Fresh $fresh

        if ($refusal.RatioFloor) {
            Write-Host ""
            Write-Host "-PruneOnly refuses: the current run produced $($refusal.FreshCount) known-fail row(s) against $($refusal.CurrentCount) currently on file -- a survival ratio of $([Math]::Round($refusal.SurvivalRatio * 100, 2))%, below this script's $($PruneOnlySurvivalRatioFloor * 100)% floor." -ForegroundColor Red
            Write-Host "Treating this as 'almost everything now passes' and pruning nearly the whole list is indistinguishable here from the generator having dispatched a smaller corpus than known-fail.tsv was measured against (SWISSEPH_CONFORMANCE_SUBMODULE pointed elsewhere; a truncated t.exp) -- see this script's own `$PruneOnlySurvivalRatioFloor comment above." -ForegroundColor Red
            Write-Host "known-fail.tsv was NOT modified. If the port genuinely has this few outstanding known failures now, use the full regenerate (scripts/regenerate-known-fail.ps1 -Reason `"...`") instead, which requires a human-reviewed reason for a change this size."
            exit 1
        }

        if ($refusal.Any) {
            $added = $refusal.Added
            $changed = $refusal.Changed
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
        #
        # HIGH 1 fix: @()-wrapped. Get-Content on a file with exactly one line (known-fail.tsv
        # header-only -- zero known failures, the port's own goal state, and the state
        # Tests/oracle/known-diff*.tsv is already in) returns a bare [string], not a one-element
        # array. Unwrapped, $currentRawLines[0] then indexes the STRING, giving its first
        # CHARACTER, and the loop below (bounded by .Count, which on a string is its character
        # length) walks the header's own characters as if they were data rows -- none of which
        # matches anything in $fresh, so $survivingLines stays empty and $outputLines collapses to
        # just that first character. Measured directly: this wrote a 471 KB golden down to the
        # single byte "s". @()-wrapping forces array shape regardless of line count, matching every
        # other Get-Content call site in this codebase that has already been bitten by this.
        $currentRawLines = @(Get-Content -LiteralPath $knownFailPath)
        $header = $currentRawLines[0]
        $survivingLines = Get-SurvivingLines -CurrentRawLines $currentRawLines -Fresh $fresh

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
