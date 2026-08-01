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
    'Analytic', 'Files' or 'Both' (default). Selects which known-diff list(s) to regenerate.

.PARAMETER PR
    Optional. The pull request number this regeneration belongs to, e.g. "34". Same convention as
    scripts/regenerate-known-fail.ps1's -PR: this repo squash-merges PRs, so a PR number survives
    the merge in a way a commit SHA captured on an open branch does not. If you do not know it yet,
    omit this and fill in the logged line by hand once you do, before the PR merges.
#>

param(
    [string]$Reason,

    [switch]$PruneOnly,

    [ValidateSet('Analytic', 'Files', 'Both')]
    [string]$Grid = 'Both',

    [string]$PR
)

$ErrorActionPreference = 'Stop'

if (-not $PruneOnly -and [string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required in default (full regenerate) mode. Use -PruneOnly if you only want to remove newly-passing rows or accept max_ulp improvements."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$verifyProject = Join-Path $repoRoot 'Tools\OracleVerify\OracleVerify.csproj'
$dumpScript = Join-Path $repoRoot 'scripts\run-oracle-dump.ps1'
$oracleDir = Join-Path $repoRoot 'Tests\oracle'

function Get-GridPaths {
    param([string]$GridName)
    if ($GridName -eq 'Analytic') {
        return [pscustomobject]@{
            Name          = 'Analytic'
            KnownDiffPath = Join-Path $oracleDir 'known-diff.tsv'
            LogPath       = Join-Path $oracleDir 'regenerations.log'
            CDumpPath     = Join-Path $repoRoot 'external\.c-reference\dump-c-2.10.03.tsv'
            NetDumpPath   = Join-Path $repoRoot 'external\.c-reference\dump-net.tsv'
        }
    }
    return [pscustomobject]@{
        Name          = 'Files'
        KnownDiffPath = Join-Path $oracleDir 'known-diff-files.tsv'
        LogPath       = Join-Path $oracleDir 'regenerations-files.log'
        CDumpPath     = Join-Path $repoRoot 'external\.c-reference\dump-c-2.10.03-files.tsv'
        NetDumpPath   = Join-Path $repoRoot 'external\.c-reference\dump-net-files.tsv'
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

            $added = @()
            $recategorized = @()
            $categoricalFlipped = @()
            $grew = @()
            foreach ($key in $fresh.Keys) {
                if (-not $current.ContainsKey($key)) {
                    $added += $key
                }
                elseif ($current[$key].Category -ne $fresh[$key].Category) {
                    $recategorized += $key
                }
                elseif ($current[$key].IsCategorical -ne $fresh[$key].IsCategorical) {
                    # A row's categorical/numeric state flipping either way has no magnitude to compare
                    # -- same reasoning as Tools/OracleVerify/OracleVerifyReport.cs's
                    # RegressionKind.CategoricalStateChanged -- so -PruneOnly must not silently accept
                    # it in either direction, exactly like a recategorization.
                    $categoricalFlipped += $key
                }
                elseif (-not $fresh[$key].IsCategorical -and $fresh[$key].MaxUlp -gt $current[$key].MaxUlp) {
                    $grew += $key
                }
            }

            if ($added.Count -gt 0 -or $recategorized.Count -gt 0 -or $categoricalFlipped.Count -gt 0 -or $grew.Count -gt 0) {
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

    Write-Host 'Running OracleVerify in generate mode against the freshly built dumps...'
    $generateOutput = dotnet run --project $verifyProject -c Release --no-build -- generate $cDumpPath $netDumpPath $knownDiffPath
    $generateOutput | Write-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $afterCount = Get-RowCount -Path $knownDiffPath
    $delta = $afterCount - $beforeCount
    $deltaDescription = if ($delta -eq 0) { 'no change in row count' }
    elseif ($delta -lt 0) { "$([Math]::Abs($delta)) fewer rows" }
    else { "$delta more rows" }

    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }

    $logEntry = "$date $prCitation ($beforeCount -> $afterCount, $deltaDescription): $Reason"
    Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

    Write-Host ''
    Write-Host "Done. $beforeCount -> $afterCount rows ($deltaDescription)."
    Write-Host "Logged to $logPath"
    return $true
}

Write-Host 'Rebuilding both sides of the oracle harness, both grids (scripts/run-oracle-dump.ps1)...'
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
