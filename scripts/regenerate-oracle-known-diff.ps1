#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/oracle/known-diff.tsv and/or Tests/oracle/known-diff-files.tsv from a live
    run of the bit-exact oracle harness.

.DESCRIPTION
    Runs scripts/run-oracle-dump.ps1 once to rebuild all four dumps fresh (the current in-repo
    port against the current sedump.c/libswe build, for both grids), then Tools/OracleVerify in
    "generate" mode for each selected grid, which writes one row per case_id that does not match
    outright -- see Tools/OracleVerify/Program.cs's RunGenerate and Tests/oracle/known-diff.tsv's
    own header for the category and max_ulp scheme. Tests/oracle/known-diff-files.tsv (the
    file-backed grid's list) follows the identical scheme; nothing about the format differs
    between the two, only which dumps and which log they are regenerated against.

    Two modes, the same shape as scripts/regenerate-known-fail.ps1:

    Default (full regenerate): overwrites the selected list(s) wholesale with whatever the
    current run produces -- rows can be removed (progress), added (a regression, or a case_id
    newly covered by the grid), recategorized, or have their recorded max_ulp move in either
    direction, all in the same run. Because it can add or worsen rows silently, it requires
    -Reason, and the row-count delta is appended to that grid's regenerations log
    (Tests/oracle/regenerations.log for Analytic, Tests/oracle/regenerations-files.log for
    Files). This is also the gate's own bypass -- someone could use it to make a red
    scripts/verify-oracle.ps1 run green by writing the failure into the list instead of fixing
    it, or by recording a bigger max_ulp than the row actually needs. Use -PruneOnly below when
    all you want is to take newly-passing rows off the list -- it cannot add or worsen anything,
    so it does not carry that risk and needs no -Reason.

    -PruneOnly: removes rows that now pass, and silently accepts a shrinking (or unchanged)
    max_ulp on a row that is still listed -- both are strict improvements. Refuses (non-zero
    exit, no file changes for that grid) if the current run would add a row, change an existing
    row's category, or record a LARGER max_ulp than what is currently on file for an existing
    row: growth is exactly the case scripts/verify-oracle.ps1 exists to catch (see
    Tools/OracleVerify/OracleVerifyReport.cs's RegressionKind.UlpGrew), so writing it into a
    known-diff list without review would be the same kind of silent gate bypass as adding a
    brand new row, just recorded as an update to an existing one instead of a new line.

    Removing rows, or improving (shrinking) a recorded max_ulp, needs no special process or
    reason -- that's the gate finding progress and is expected to happen often. Adding a row,
    recategorizing one, or recording a larger max_ulp needs one, which is why this script is the
    only supported way to touch either file.

    -Grid Both (the default) regenerates both lists in the same run, under the same -Reason --
    appropriate when a single porting PR changes behavior both grids can see (e.g. a sweph.c fix
    that touches both SEFLG_MOSEPH and SEFLG_SWIEPH paths). Pass a single grid name when a change
    can only affect one of them (e.g. a fixed-star fix only grid-files.tsv exercises at all).

.PARAMETER Reason
    Required in default mode unless every selected grid uses -PruneOnly (ignored then). A short
    description of why the known-diff list is changing: a porting PR that fixed N case ids, a
    newly-discovered LIBM-RESIDUAL root cause reclassifying some PORT-VERSION rows, a grid change
    that added or removed case ids, etc.

.PARAMETER PruneOnly
    Only remove newly-passing rows and accept max_ulp improvements; never add a row, recategorize
    one, or accept a larger max_ulp than what is currently recorded. Exits non-zero and leaves
    every selected grid's known-diff list untouched if the current run would do any of those for
    that grid -- see DESCRIPTION.

.PARAMETER Grid
    'Analytic', 'Files', 'Jpl' or 'Both' (default). Selects which known-diff list(s) to
    regenerate. 'Both' means Analytic and Files only -- it does not include Jpl, matching
    scripts/verify-oracle.ps1's own -Grid Both, because that grid's dumps only exist when
    scripts/run-oracle-dump.ps1 was opted in with a DE file this repo does not ship. Selecting Jpl
    therefore requires SWISSEPH_ORACLE_JPL_FILE to be set before this script runs, and refuses
    outright if it is not: without it the dump run this script kicks off first would skip the JPL
    leg entirely, and this script would then regenerate a known-diff list from whatever JPL dumps
    happened to be left on disk from some earlier run.

.PARAMETER PR
    Optional. The pull request number this regeneration belongs to, e.g. "34". Same convention as
    scripts/regenerate-known-fail.ps1's -PR: this repo squash-merges PRs, so a PR number survives
    the merge in a way a commit SHA captured on an open branch does not. If you do not know it yet,
    omit this and fill in the logged line by hand once you do, before the PR merges.

.PARAMETER SelfTest
    Assert the two things about this script that a live run cannot practically check: that case_ids
    differing only in case stay distinct, and that -PruneOnly refuses every change it is not allowed
    to make. Runs against scratch known-diff files in a temporary directory -- it never rebuilds a
    dump, never invokes OracleVerify, and never reads or writes Tests/oracle/.
#>

param(
    [string]$Reason,

    [switch]$PruneOnly,

    [ValidateSet('Analytic', 'Files', 'Jpl', 'Both')]
    [string]$Grid = 'Both',

    [string]$PR,

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

if (-not $SelfTest -and -not $PruneOnly -and [string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required in default (full regenerate) mode. Use -PruneOnly if you only want to remove newly-passing rows or accept max_ulp improvements."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$verifyProject = Join-Path $repoRoot 'Tools/OracleVerify/OracleVerify.csproj'
$dumpScript = Join-Path $repoRoot 'scripts/run-oracle-dump.ps1'
$oracleDir = Join-Path $repoRoot 'Tests/oracle'

function Get-GridPaths {
    param([string]$GridName)
    if ($GridName -eq 'Analytic') {
        return [pscustomobject]@{
            Name          = 'Analytic'
            KnownDiffPath = Join-Path $oracleDir 'known-diff.tsv'
            LogPath       = Join-Path $oracleDir 'regenerations.log'
            # Forward slashes, like the three paths above. A backslash is an ordinary filename
            # character on Linux rather than a separator, so each of these would resolve to one
            # file literally named "external\.c-reference\dump-c-2.10.03.tsv" there. The commit
            # that converted this script said it fixed "all three of its path literals"; it fixed
            # the three above and left these four, so the file was half-converted. Unlike the
            # sidecar-path defect in verify-baseline-log.ps1 this is latent rather than live --
            # Get-GridPaths is only reached in the real regeneration mode, which -SelfTest exits
            # before, so no self-test case here can see it either way.
            CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03.tsv'
            NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net.tsv'
        }
    }
    if ($GridName -eq 'Jpl') {
        return [pscustomobject]@{
            Name          = 'Jpl'
            KnownDiffPath = Join-Path $oracleDir 'known-diff-jpl.tsv'
            LogPath       = Join-Path $oracleDir 'regenerations-jpl.log'
            CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-jpl.tsv'
            NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net-jpl.tsv'
        }
    }
    return [pscustomobject]@{
        Name          = 'Files'
        KnownDiffPath = Join-Path $oracleDir 'known-diff-files.tsv'
        LogPath       = Join-Path $oracleDir 'regenerations-files.log'
        CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-files.tsv'
        NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net-files.tsv'
    }
}

function Get-RowCount {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    return (Get-Content $Path | Measure-Object -Line).Lines - 1 # minus header
}

function Read-KnownDiffTable {
    # Keyed by case_id -> @{ Category; MaxUlp; IsCategorical }. Plain tab-split, not Import-Csv:
    # the "reason" column can contain characters Import-Csv would need quoting rules for that this
    # TSV format does not use, same reasoning as regenerate-known-fail.ps1's Read-KnownFailTable.
    # The reason column itself is deliberately not read here: it is regenerated diagnostic detail
    # (a short summary of the differing fields), not an editorial claim, so a change to it alone
    # is never treated as an add or a recategorization.
    #
    # max_ulp is "categorical" (a literal, non-numeric marker -- see
    # Tools/OracleVerify/KnownDiffList.cs) for a row where at least one field differs by a NaN on
    # one side and a finite value on the other. That state has no magnitude to compare against a
    # later run's, so it is tracked separately as IsCategorical rather than coerced to a number.
    #
    # Keyed with an ordinal (case-sensitive) comparer, not PowerShell's `@{}` default
    # (case-insensitive, culture-aware) -- the same case_id namespace classify-oracle-versions.ps1's
    # Read-ClassificationTable already keys this way, for the same reason: case_id legitimately
    # differs only by case for some rows (e.g. HOUSESARMC|I|... vs HOUSESARMC|i|...), and a
    # case-insensitive table silently collapses those into one, last-write-wins. Measured against
    # Tests/oracle/grid-analytic.tsv: 15,916 ordinal-distinct case_ids collapse to 15,520 under the
    # default comparer -- 396 case-only collisions. This table backs -PruneOnly's refusal guard
    # (added/recategorized/worsened rows), so a collapsed key means a row that should have blocked
    # -PruneOnly silently doesn't, or a row's real prior state is compared against the wrong sibling.
    param([string]$Path)
    $table = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path $Path)) { return $table }
    $lines = Get-Content $Path
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        $isCategorical = $cols[2] -eq 'categorical'
        $maxUlp = if ($isCategorical) { [uint64]0 } else { [uint64]$cols[2] }
        $table[$cols[0]] = [pscustomobject]@{ Category = $cols[1]; MaxUlp = $maxUlp; IsCategorical = $isCategorical }
    }
    return $table
}

# The -PruneOnly refusal guard itself, factored out of Invoke-GridRegeneration below so it can be
# exercised directly. A live run cannot practically check it: reaching this comparison requires
# both dumps rebuilt from a C build and OracleVerify run over both grids, and it only refuses when
# the port happens to have regressed, which is precisely the situation nobody can produce on
# demand. Returns the four disjoint kinds of change this mode is not allowed to write, each a list
# of case_ids, plus whether any of them fired.
function Get-PruneOnlyRefusals {
    param($Current, $Fresh)

    # Vacuity floor: $Fresh with zero rows and $Current with more than zero looks exactly like
    # "every known difference now matches outright" to the loop below -- $added, $recategorized,
    # $categoricalFlipped and $grew all stay empty (nothing in $Fresh to iterate), so the refusal
    # never fires and every surviving-row check afterward finds nothing in $Fresh to survive,
    # pruning the entire list. That is indistinguishable, from this script's point of view, from
    # the freshly rebuilt dumps having silently failed to compare anything (both dumps run against
    # an empty/truncated grid, LoadAndCompare's own "zero rows were compared" floor notwithstanding
    # a bug upstream of it) -- see scripts/regenerate-known-fail.ps1's identical floor, added by
    # commit 849599b for exactly this shape of defect. Refusing here is deliberate: if the port
    # genuinely reaches zero outstanding differences for a grid, that transition is worth a
    # human-reviewed -Reason in the default (full regenerate) mode, the same way any other change
    # this size already requires one, rather than passing silently through the one mode designed to
    # need no review at all.
    if ($Fresh.Count -eq 0 -and $Current.Count -gt 0) {
        return [pscustomobject]@{
            Added              = @()
            Recategorized      = @()
            CategoricalFlipped = @()
            Grew               = @()
            Any                = $true
            Vacuous            = $true
            CurrentCount       = $Current.Count
        }
    }

    $added = @()
    $recategorized = @()
    $categoricalFlipped = @()
    $grew = @()
    foreach ($key in $Fresh.Keys) {
        if (-not $Current.ContainsKey($key)) {
            $added += $key
        }
        elseif ($Current[$key].Category -ne $Fresh[$key].Category) {
            $recategorized += $key
        }
        elseif ($Current[$key].IsCategorical -ne $Fresh[$key].IsCategorical) {
            # A row's categorical/numeric state flipping either way has no magnitude to compare
            # -- same reasoning as Tools/OracleVerify/OracleVerifyReport.cs's
            # RegressionKind.CategoricalStateChanged -- so -PruneOnly must not silently accept
            # it in either direction, exactly like a recategorization.
            $categoricalFlipped += $key
        }
        elseif (-not $Fresh[$key].IsCategorical -and $Fresh[$key].MaxUlp -gt $Current[$key].MaxUlp) {
            $grew += $key
        }
    }

    return [pscustomobject]@{
        Added              = $added
        Recategorized      = $recategorized
        CategoricalFlipped = $categoricalFlipped
        Grew               = $grew
        Any                = ($added.Count -gt 0 -or $recategorized.Count -gt 0 -or $categoricalFlipped.Count -gt 0 -or $grew.Count -gt 0)
        Vacuous            = $false
        CurrentCount       = $Current.Count
    }
}

# Runs one grid's regeneration (either mode). Returns $true on success, $false on a -PruneOnly
# refusal (already reported to the console by this point).
function Invoke-GridRegeneration {
    param([pscustomobject]$Paths)

    $knownDiffPath = $Paths.KnownDiffPath
    $logPath = $Paths.LogPath
    $cDumpPath = $Paths.CDumpPath
    $netDumpPath = $Paths.NetDumpPath

    Write-Host "--- $($Paths.Name) grid ---" -ForegroundColor Cyan

    if ($PruneOnly) {
        $tempPath = [System.IO.Path]::GetTempFileName()
        try {
            Write-Host 'Running OracleVerify in generate mode against the freshly built dumps...'
            # Captured, then explicitly written to host: a bare, uncaptured native-command call
            # inside a PowerShell function folds its stdout into the function's own return value
            # alongside whatever this function later `return`s, silently turning the caller's
            # boolean success check into a truthy non-empty array regardless of what actually
            # happened. Every dotnet run call in this function follows the same pattern.
            $generateOutput = dotnet run --project $verifyProject -c Release --no-build -- generate $cDumpPath $netDumpPath $tempPath
            $generateOutput | Write-Host
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

            $current = Read-KnownDiffTable -Path $knownDiffPath
            $fresh = Read-KnownDiffTable -Path $tempPath

            $refusals = Get-PruneOnlyRefusals -Current $current -Fresh $fresh
            $added = $refusals.Added
            $recategorized = $refusals.Recategorized
            $categoricalFlipped = $refusals.CategoricalFlipped
            $grew = $refusals.Grew

            if ($refusals.Vacuous) {
                Write-Host ''
                Write-Host "-PruneOnly refuses: the freshly built dumps produced ZERO differing rows for the $($Paths.Name) grid, while $knownDiffPath currently has $($refusals.CurrentCount)." -ForegroundColor Red
                Write-Host 'Treating this as "every row now matches" and pruning the whole list is indistinguishable here from the dump run having silently failed to compare anything for this grid -- see this function''s own comment above Get-PruneOnlyRefusals.' -ForegroundColor Red
                Write-Host "$($Paths.Name) known-diff list was NOT modified. If the port genuinely has zero outstanding differences for this grid now, use the full regenerate (scripts/regenerate-oracle-known-diff.ps1 -Reason `"...`") instead, which requires a human-reviewed reason for a change this size."
                return $false
            }

            if ($refusals.Any) {
                Write-Host ''
                Write-Host '-PruneOnly refuses: the current run would add, recategorize, or worsen a row, and this mode can only remove rows or improve them.' -ForegroundColor Red
                if ($added.Count -gt 0) {
                    Write-Host ''
                    Write-Host "Would ADD $($added.Count) row(s) (a new differing case_id, e.g. one the grid newly covers):"
                    foreach ($key in $added | Select-Object -First 50) {
                        Write-Host "  $key  [$($fresh[$key].Category), max_ulp=$($fresh[$key].MaxUlp)]"
                    }
                }
                if ($recategorized.Count -gt 0) {
                    Write-Host ''
                    Write-Host "Would RECATEGORIZE $($recategorized.Count) row(s):"
                    foreach ($key in $recategorized | Select-Object -First 50) {
                        Write-Host "  $key  $($current[$key].Category) -> $($fresh[$key].Category)"
                    }
                }
                if ($categoricalFlipped.Count -gt 0) {
                    Write-Host ''
                    Write-Host "Would flip the categorical/numeric state of $($categoricalFlipped.Count) already-listed row(s):"
                    foreach ($key in $categoricalFlipped | Select-Object -First 50) {
                        $from = if ($current[$key].IsCategorical) { 'categorical' } else { $current[$key].MaxUlp }
                        $to = if ($fresh[$key].IsCategorical) { 'categorical' } else { $fresh[$key].MaxUlp }
                        Write-Host "  $key  max_ulp $from -> $to"
                    }
                }
                if ($grew.Count -gt 0) {
                    Write-Host ''
                    Write-Host "Would record a LARGER max_ulp for $($grew.Count) already-listed row(s):"
                    foreach ($key in $grew | Select-Object -First 50) {
                        Write-Host "  $key  max_ulp $($current[$key].MaxUlp) -> $($fresh[$key].MaxUlp)"
                    }
                }
                Write-Host ''
                Write-Host "$($Paths.Name) known-diff list was NOT modified. Adding, recategorizing, or worsening a row is a deliberate, separate act:"
                Write-Host 'run the full regenerate (scripts/regenerate-oracle-known-diff.ps1 -Reason "...") once you have understood and reviewed it.'
                return $false
            }

            $beforeCount = $current.Count
            Copy-Item -Path $tempPath -Destination $knownDiffPath -Force
            $afterCount = $fresh.Count
            $removed = $beforeCount - $afterCount

            $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
            $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }
            $reasonText = if ($Reason) { $Reason } else { "Pruned $removed newly-passing row(s); no reason required for a pure removal or a max_ulp improvement." }
            $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $removed fewer rows): $reasonText"
            Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

            Write-Host ''
            Write-Host "Done (prune-only). $beforeCount -> $afterCount rows ($removed fewer)."
            Write-Host "Logged to $logPath"
            return $true
        }
        finally {
            Remove-Item -Path $tempPath -ErrorAction SilentlyContinue
        }
    }

    $beforeCount = Get-RowCount -Path $knownDiffPath

    # Staged to a temp file, then moved over $knownDiffPath only once OracleVerify has exited 0 --
    # the same pattern -PruneOnly already uses above, rather than "generate" writing straight over
    # the committed file the way this block used to. KnownDiffList.Save (Tools/OracleVerify/
    # KnownDiffList.cs:85) opens the destination with append:false, truncating it immediately on
    # open -- so passing $knownDiffPath directly meant the committed file was gone from the moment
    # the process started, before a single row had been written back, let alone before the exit
    # code below was checked. A crash partway through (an unhandled exception, an OOM, a killed
    # process) left $knownDiffPath empty or truncated with the original content already destroyed
    # and the $LASTEXITCODE check at the end never reached in time to stop it. Matches
    # scripts/regenerate-known-fail.ps1's identical fix from commit 849599b, which this script's own
    # -PruneOnly mode already mirrored but this default mode did not.
    $tempKnownDiffPath = [System.IO.Path]::GetTempFileName()
    try {
        Write-Host 'Running OracleVerify in generate mode against the freshly built dumps...'
        $generateOutput = dotnet run --project $verifyProject -c Release --no-build -- generate $cDumpPath $netDumpPath $tempKnownDiffPath
        $generateOutput | Write-Host
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $afterCount = Get-RowCount -Path $tempKnownDiffPath
        $delta = $afterCount - $beforeCount
        $deltaDescription = if ($delta -eq 0) { 'no change in row count' }
        elseif ($delta -lt 0) { "$([Math]::Abs($delta)) fewer rows" }
        else { "$delta more rows" }

        # Only now, with a complete and exit-0 generator run sitting safely in a temp file, does
        # anything under Tests/oracle/ get touched.
        Copy-Item -LiteralPath $tempKnownDiffPath -Destination $knownDiffPath -Force

        $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }

        $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $deltaDescription): $Reason"
        Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

        Write-Host ''
        Write-Host "Done. $beforeCount -> $afterCount rows ($deltaDescription)."
        Write-Host "Logged to $logPath"
        return $true
    }
    finally {
        Remove-Item -Path $tempKnownDiffPath -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------
# Self-test. Everything below the functions is a live run -- two dumps rebuilt from a C build,
# OracleVerify over both grids, minutes of work -- so this block exits before any of it. It
# exercises the two pieces that decide whether a row can be written into a known-diff list without
# review: how the list is keyed, and what -PruneOnly refuses.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("regenerate-oracle-known-diff-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    function New-Row {
        param([string] $CaseId, [string] $Category, [string] $MaxUlp, [string] $Reason = 'lon differs')
        return "$CaseId`t$Category`t$MaxUlp`t$Reason"
    }

    function New-KnownDiffFile {
        param([string] $Path, [string[]] $Rows)
        $text = "case_id`tcategory`tmax_ulp`treason`n" + (($Rows -join "`n") + "`n")
        [System.IO.File]::WriteAllText($Path, ($text -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
    }

    function Read-Pair {
        # Round-trips both sides through real files, not hand-built hashtables: the keying is the
        # thing under test here, and building the tables directly would build them with whatever
        # comparer this test chose rather than the one Read-KnownDiffTable actually uses.
        param([string[]] $CurrentRows, [string[]] $FreshRows)
        $currentPath = Join-Path $root 'current.tsv'
        $freshPath = Join-Path $root 'fresh.tsv'
        New-KnownDiffFile $currentPath $CurrentRows
        New-KnownDiffFile $freshPath $FreshRows
        return [pscustomobject]@{
            Current = (Read-KnownDiffTable -Path $currentPath)
            Fresh   = (Read-KnownDiffTable -Path $freshPath)
        }
    }

    function Assert-Equal {
        param([string] $Case, $Expected, $Actual)
        if ($Expected -eq $Actual) {
            Write-Host ("  PASS  {0}" -f $Case)
        }
        else {
            Write-Host ("  FAIL  {0}`n          expected {1}`n          actual   {2}" -f $Case, $Expected, $Actual)
            $script:failures++
        }
    }

    function Assert-Refuses {
        param([string] $Case, [string[]] $CurrentRows, [string[]] $FreshRows, [string] $Bucket)
        $pair = Read-Pair $CurrentRows $FreshRows
        $refusals = Get-PruneOnlyRefusals -Current $pair.Current -Fresh $pair.Fresh
        if ($refusals.Any -and $refusals.$Bucket.Count -gt 0) {
            Write-Host ("  PASS  {0} (refused: {1} = {2})" -f $Case, $Bucket, ($refusals.$Bucket -join ', '))
        }
        else {
            Write-Host ("  FAIL  {0}`n          expected a refusal in {1}, got Any={2} Added={3} Recategorized={4} CategoricalFlipped={5} Grew={6}" -f
                $Case, $Bucket, $refusals.Any, $refusals.Added.Count, $refusals.Recategorized.Count, $refusals.CategoricalFlipped.Count, $refusals.Grew.Count)
            $script:failures++
        }
    }

    function Assert-Accepts {
        param([string] $Case, [string[]] $CurrentRows, [string[]] $FreshRows)
        $pair = Read-Pair $CurrentRows $FreshRows
        $refusals = Get-PruneOnlyRefusals -Current $pair.Current -Fresh $pair.Fresh
        if (-not $refusals.Any) {
            Write-Host ("  PASS  {0} (accepted)" -f $Case)
        }
        else {
            Write-Host ("  FAIL  {0}`n          expected no refusal, got Added={1} Recategorized={2} CategoricalFlipped={3} Grew={4}" -f
                $Case, ($refusals.Added -join ', '), ($refusals.Recategorized -join ', '), ($refusals.CategoricalFlipped -join ', '), ($refusals.Grew -join ', '))
            $script:failures++
        }
    }

    Write-Host 'regenerate-oracle-known-diff self-test'
    Write-Host ''

    # 1. Control: a pure prune plus a shrinking max_ulp, which is everything -PruneOnly exists to
    #    allow. Without this, a refusal case passing would prove only that the guard refuses
    #    everything.
    Assert-Accepts 'a pure removal plus a shrinking max_ulp is accepted' `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4'), (New-Row 'CALC|2' 'PORT-VERSION' '9')) `
        @((New-Row 'CALC|1' 'PORT-VERSION' '2'))

    # 2. The mandated refusal: a row the current run produces that is not on the list is a
    #    currently-failing case, and writing it in without review is exactly the gate bypass
    #    -PruneOnly refuses to be used for.
    Assert-Refuses 'a row that would be newly written into the list is refused' `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4')) `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4'), (New-Row 'CALC|2' 'PORT-VERSION' '7')) 'Added'

    # 3. A recategorization is an editorial claim about why a row differs, so it needs -Reason.
    Assert-Refuses 'a recategorized row is refused' `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4')) `
        @((New-Row 'CALC|1' 'LIBM-RESIDUAL' '4')) 'Recategorized'

    # 4. A larger max_ulp on a row already listed. Recorded as an update to an existing line rather
    #    than a new one, it is the same silent widening as an added row.
    Assert-Refuses 'a larger max_ulp on an already-listed row is refused' `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4')) `
        @((New-Row 'CALC|1' 'PORT-VERSION' '5')) 'Grew'

    # 5. Numeric to categorical, and back. Neither direction has a magnitude to compare, so
    #    -PruneOnly cannot call either one an improvement.
    Assert-Refuses 'a numeric row turning categorical is refused' `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4')) `
        @((New-Row 'CALC|1' 'PORT-VERSION' 'categorical')) 'CategoricalFlipped'
    Assert-Refuses 'a categorical row turning numeric is refused' `
        @((New-Row 'CALC|1' 'PORT-VERSION' 'categorical')) `
        @((New-Row 'CALC|1' 'PORT-VERSION' '4')) 'CategoricalFlipped'

    # 6. The 'categorical' marker is a literal, not a number: coercing it would need [uint64]
    #    'categorical', which throws, and treating it as 0 would make every later run look like
    #    growth. Read as a flag, with no magnitude of its own.
    $categoricalPath = Join-Path $root 'categorical.tsv'
    New-KnownDiffFile $categoricalPath @((New-Row 'CALC|1' 'PORT-VERSION' 'categorical'))
    $categoricalTable = Read-KnownDiffTable -Path $categoricalPath
    Assert-Equal "the 'categorical' max_ulp marker is read as a flag, not a number" $true $categoricalTable['CALC|1'].IsCategorical

    # 7. case_ids differing only in case must stay distinct. Measured against
    #    Tests/oracle/grid-analytic.tsv: 15,916 ordinal-distinct case_ids collapse to 15,520 under
    #    PowerShell's default @{} comparer -- 396 case-only collisions merged last-write-wins.
    $casePath = Join-Path $root 'case-collision.tsv'
    New-KnownDiffFile $casePath @((New-Row 'HOUSESARMC|I|1' 'PORT-VERSION' '4'), (New-Row 'HOUSESARMC|i|1' 'PORT-VERSION' '9'))
    $caseTable = Read-KnownDiffTable -Path $casePath
    Assert-Equal 'two case_ids differing only in case stay two rows, not one' 2 $caseTable.Count
    Assert-Equal 'the upper-case case_id keeps its own max_ulp' ([uint64]4) $caseTable['HOUSESARMC|I|1'].MaxUlp
    # Stated as "the two lookups disagree" rather than as a second per-row expectation: a collapse
    # is last-write-wins, so exactly one of the two rows' values survives, and a per-row assertion
    # naming that survivor's value would pass under the collapse it is supposed to catch. This
    # form has no such survivor to agree with.
    Assert-Equal 'the two case-only siblings do not resolve to one shared row' $false `
        ($caseTable['HOUSESARMC|I|1'].MaxUlp -eq $caseTable['HOUSESARMC|i|1'].MaxUlp)

    # 8. What the collapse costs the refusal guard, part one: a case-only sibling of a listed row
    #    is a genuinely new row. Under a case-insensitive table it looks like one already on the
    #    list, so it is never reported as added and -PruneOnly writes it in silently.
    Assert-Refuses 'a case-only sibling of a listed row is still an added row' `
        @((New-Row 'HOUSESARMC|I|1' 'PORT-VERSION' '20')) `
        @((New-Row 'HOUSESARMC|I|1' 'PORT-VERSION' '20'), (New-Row 'HOUSESARMC|i|1' 'PORT-VERSION' '3')) 'Added'

    # 9. Part two, and the reason a case-insensitive table is not merely imprecise but actively
    #    hides growth: with the colliding rows written in this order, both sides collapse
    #    last-write-wins onto the same surviving value (20), the lower-case row's 5 -> 12 growth
    #    disappears entirely, and -PruneOnly records the worsened row as though nothing moved.
    Assert-Refuses 'a case-only collision cannot hide a max_ulp that grew' `
        @((New-Row 'HOUSESARMC|i|1' 'PORT-VERSION' '5'), (New-Row 'HOUSESARMC|I|1' 'PORT-VERSION' '20')) `
        @((New-Row 'HOUSESARMC|i|1' 'PORT-VERSION' '12'), (New-Row 'HOUSESARMC|I|1' 'PORT-VERSION' '20')) 'Grew'

    # 10. MEDIUM 4's vacuity floor: a fresh run producing ZERO rows while the committed list has
    #     some looks, to the loop above, exactly like "every row now matches" -- $Fresh has nothing
    #     to iterate, so $added/$recategorized/$categoricalFlipped/$grew all stay empty and the
    #     ordinary refusal never fires. Without the floor, this pruned the whole list to nothing and
    #     logged it as "no reason required for a pure removal", indistinguishable from the dump run
    #     having silently failed to compare anything for this grid. Matches
    #     scripts/regenerate-known-fail.ps1's identical floor (commit 849599b).
    $pair = Read-Pair @((New-Row 'CALC|1' 'PORT-VERSION' '4'), (New-Row 'CALC|2' 'PORT-VERSION' '9')) @()
    $refusals = Get-PruneOnlyRefusals -Current $pair.Current -Fresh $pair.Fresh
    if ($refusals.Any -and $refusals.Vacuous -and $refusals.CurrentCount -eq 2) {
        Write-Host ("  PASS  {0} (refused: Vacuous, CurrentCount={1})" -f 'a fresh run producing zero rows while the committed list has some is refused, not pruned to empty', $refusals.CurrentCount)
    }
    else {
        Write-Host ("  FAIL  {0}`n          expected Vacuous=true and CurrentCount=2, got Vacuous={1} CurrentCount={2} Any={3}" -f
            'a fresh run producing zero rows while the committed list has some is refused, not pruned to empty', $refusals.Vacuous, $refusals.CurrentCount, $refusals.Any)
        $script:failures++
    }

    # 11. Control for case 10: both sides genuinely empty (a grid with zero outstanding
    #     differences, freshly regenerated to confirm it still has zero) must NOT trip the vacuity
    #     floor -- it only fires when $Current had rows and $Fresh lost all of them, not when both
    #     start and stay empty. Tests/oracle/regenerations.log's own 2026-07-31 entry (0 -> 0) is
    #     exactly this shape.
    Assert-Accepts 'both sides empty (already zero outstanding differences) is accepted, not vacuous' @() @()

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all regenerate-oracle-known-diff self-test cases passed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

# -Grid Jpl reads dumps only an opted-in run produces -- see that parameter's own help. Checked
# before the dump run rather than after, so the refusal costs nothing.
if ($Grid -eq 'Jpl' -and [string]::IsNullOrWhiteSpace($env:SWISSEPH_ORACLE_JPL_FILE)) {
    Write-Error "-Grid Jpl needs SWISSEPH_ORACLE_JPL_FILE set to a JPL DE file, or scripts/run-oracle-dump.ps1 below will skip the JPL leg and this script would regenerate Tests/oracle/known-diff-jpl.tsv from stale dumps."
    exit 1
}

Write-Host 'Rebuilding both sides of the oracle harness (scripts/run-oracle-dump.ps1)...'
& $dumpScript
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host "Building $verifyProject (Release)..."
dotnet build $verifyProject -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$grids = if ($Grid -eq 'Both') { @('Analytic', 'Files') } else { @($Grid) }
$allSucceeded = $true
foreach ($g in $grids) {
    Write-Host ''
    $ok = Invoke-GridRegeneration -Paths (Get-GridPaths -GridName $g)
    if (-not $ok) { $allSucceeded = $false }
}

if (-not $allSucceeded) { exit 1 }

if (-not $PruneOnly) {
    Write-Host ''
    Write-Host 'Review the diff (git diff Tests/oracle/known-diff.tsv Tests/oracle/known-diff-files.tsv) before committing:'
    Write-Host '  - Rows removed only, or max_ulp only shrinking: progress. Confirm the removed case ids actually match now, not that'
    Write-Host '    scripts/run-oracle-dump.ps1 quietly emitted fewer rows than the grid (it refuses outright if it does).'
    Write-Host '  - Rows added, recategorized, or a larger max_ulp: a regression, a newly-covered case_id, or a deliberate'
    Write-Host '    reclassification (e.g. tracing a PORT-VERSION row to a named libm function and its pinned ULP bound). Needs'
    Write-Host '    -Reason above to already explain it, and a reviewer to agree before this merges. Prefer -PruneOnly instead of'
    Write-Host '    this default mode when all you actually did was remove rows or improve existing ones -- it cannot add or worsen by accident.'
}
