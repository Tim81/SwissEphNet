#Requires -Version 7
<#
.SYNOPSIS
    Regenerates Tests/oracle/version-classification.tsv and version-classification-files.tsv: a
    three-way classification of every oracle case_id against both Swiss Ephemeris C versions this
    repo can build, not just the 2.10.03 target scripts/verify-oracle.ps1 checks the port against.

.DESCRIPTION
    Tests/oracle/known-diff.tsv's PORT-VERSION category only ever means "the port differs from
    2.10.03 C". It says nothing about whether the same case_id also differs from 2.08 C, the
    version the port actually tracks -- and those are different problems with different owners.
    A row that matches 2.08 C but differs from 2.10.03 C is ordinary porting work outstanding,
    exactly what the 2.10.03 porting effort is for. A row that differs from 2.08 C is a
    transliteration defect already shipping in the library today, whether or not the 2.10.03
    upgrade also touches that code path -- CONTRIBUTING.md's "2.08 baseline trap" section is the
    same distinction, applied to which C tree a diff is taken against rather than which C tree a
    comparison is taken against, but it is the same principle: conflating the two hides which
    problem a given row actually is.

    Runs scripts/run-oracle-dump.ps1 once to rebuild all six dumps fresh (2.10.03 C, 2.08 C and
    the port, for both grids), then Tools/OracleVerify in "classify" mode for each grid -- see
    Tools/OracleVerify/ThreeWayClassification.cs for the four-way split (AGREES-BOTH,
    TRACKS-2.08, TRACKS-2.10.03, TRACKS-NEITHER) and Program.cs's RunClassify.

    Unlike scripts/regenerate-oracle-known-diff.ps1, this is not a gate's bypass and carries no
    -PruneOnly/-Reason distinction. Both output files are gated: .github/workflows/oracle.yml
    regenerates them from that run's own dumps and fails on any difference from what is committed.
    What that gate asserts is that the committed measurement is current, not that any particular
    classification is acceptable, so re-running this script and committing the result is the
    intended answer to that failure rather than a way around it. That is the difference from
    known-diff.tsv, where adding a row suppresses a real difference and a reviewed -Reason exists
    to guard against exactly that. It always overwrites both files wholesale with
    whatever the current run measures. What -Reason (below) buys instead is a readable history of
    *when* the measurement changed and *why someone re-ran it* -- e.g. "after PR #40 fixed the
    swe_fixstar2 path" reads a lot better in Tests/oracle/version-classification-regenerations.log
    than a bare timestamp would.

.PARAMETER Reason
    Optional. Recorded in the log alongside the row-count delta for each grid. Defaults to a
    generic "routine re-measurement" note if omitted -- unlike known-diff.tsv's regeneration
    script, this is not required, because nothing about a plain re-run needs to be justified to a
    reviewer here.

.PARAMETER PR
    Optional. Same convention as scripts/regenerate-known-fail.ps1 and
    scripts/regenerate-oracle-known-diff.ps1's own -PR: this repo squash-merges PRs, so a PR
    number survives the merge in a way a commit SHA captured on an open branch does not. If you do
    not know it yet, omit this and fill in the logged line by hand once you do, before the PR
    merges. Restricted to bare digits (MEDIUM 7's own review): this value is interpolated
    directly into the log entry written to version-classification-regenerations.log, and an
    unvalidated value could carry a newline that forges a second, backdated-looking log entry --
    the append-only log gates (scripts/verify-oracle-log.ps1) read entries by a "YYYY-MM-DD "
    line-start prefix, so a crafted -PR value starting a new line with one would be indistinguishable
    from a real second entry.
#>

param(
    [string]$Reason,
    [ValidatePattern('^\d+$')]
    [string]$PR
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -Reason is optional (see its own parameter help), but if given it must not carry a newline for
# the same reason -PR is now restricted to bare digits: it is interpolated directly into the log
# entry, and a newline inside it would start what Get-DateLogEntries (scripts/lib/DateLogGate.ps1)
# reads as a second entry the moment it happens to begin with something matching
# "^\d{4}-\d{2}-\d{2}\s" -- or, short of that, would still let a reviewer-facing log line carry
# content that was never actually reviewed as a single line.
if ($Reason -and ($Reason -match "`r" -or $Reason -match "`n")) {
    Write-Error "-Reason must not contain a newline: it is written directly into version-classification-regenerations.log, and a newline could be read as the start of a second, forged log entry."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$verifyProject = Join-Path $repoRoot 'Tools/OracleVerify/OracleVerify.csproj'
$dumpScript = Join-Path $repoRoot 'scripts/run-oracle-dump.ps1'
$oracleDir = Join-Path $repoRoot 'Tests/oracle'
$logPath = Join-Path $oracleDir 'version-classification-regenerations.log'

function Get-GridPaths {
    param([string]$GridName)
    if ($GridName -eq 'Analytic') {
        return [pscustomobject]@{
            Name             = 'Analytic'
            OutputPath       = Join-Path $oracleDir 'version-classification.tsv'
            C210DumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03.tsv'
            C208DumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.08.tsv'
            NetDumpPath      = Join-Path $repoRoot 'external/.c-reference/dump-net.tsv'
        }
    }
    return [pscustomobject]@{
        Name             = 'Files'
        OutputPath       = Join-Path $oracleDir 'version-classification-files.tsv'
        C210DumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-files.tsv'
        C208DumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.08-files.tsv'
        NetDumpPath      = Join-Path $repoRoot 'external/.c-reference/dump-net-files.tsv'
    }
}

# case_id -> classification name only, ignoring the three describe-columns: those are diagnostic
# detail regenerated fresh every run (like known-diff.tsv's reason column), never themselves the
# reason a re-run happened.
#
# Mirrors Tools/OracleVerify/ThreeWayClassification.cs's ThreeWayClassificationFile.Load: skip the
# leading '#'-prefixed comment block (whatever its current length -- this counts it dynamically,
# never a fixed number of lines), then the column header row itself, before treating anything as
# data. An earlier version of this function started at a fixed index (1) instead, which counted
# every comment line after the first, plus the header row, as a phantom "case". Measured against
# the file as it stood before this fix (27 comment+header lines ahead of the data): that inflated
# every count this function produced, and therefore every count logged to
# version-classification-regenerations.log, by exactly 27.
#
# The table is keyed with an ordinal (case-sensitive) comparer, not PowerShell's `@{}` default
# (case-insensitive, culture-aware). case_id legitimately differs only by case for some rows --
# e.g. HOUSESARMC|I|... vs HOUSESARMC|i|... -- and a case-insensitive table silently collapses
# those into one entry. Measured against that same pre-fix file: 14,220 real data rows, but only
# 13,824 distinct keys under the default comparer -- 396 case-only collisions, coincidentally close
# enough to the index-1 bug's own 27-row inflation to look like the same problem from the logged
# counts alone. Tools/OracleVerify/ThreeWayClassification.cs's C# side never had this bug; .NET's
# Dictionary<string, T> defaults to ordinal, unlike PowerShell's `@{}`.
function Read-ClassificationTable {
    param([string]$Path)
    $table = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path $Path)) { return $table }
    $lines = Get-Content $Path
    $i = 0
    while ($i -lt $lines.Count -and $lines[$i].StartsWith('#')) { $i++ }
    if ($i -lt $lines.Count) { $i++ } # skip the column header row itself
    for (; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $cols = $line -split "`t"
        $table[$cols[0]] = $cols[1]
    }
    return $table
}

function Invoke-GridClassification {
    param([pscustomobject]$Paths)

    Write-Host "--- $($Paths.Name) grid ---" -ForegroundColor Cyan

    $before = Read-ClassificationTable -Path $Paths.OutputPath
    $beforeCount = $before.Count

    # Staged to a temp file, then moved over $Paths.OutputPath only once OracleVerify has exited 0
    # -- matching scripts/regenerate-oracle-known-diff.ps1's own Invoke-GridRegeneration. Passing
    # $Paths.OutputPath directly meant the committed golden was the tool's own output target:
    # Tools/OracleVerify's classify writer opens its destination with append: false, truncating it
    # immediately on open, before a single row had been written back and long before this
    # function's own $LASTEXITCODE check below runs. A crash partway through (an unhandled
    # exception, an OOM, a killed process) left the committed
    # Tests/oracle/version-classification*.tsv truncated or empty with the original content already
    # destroyed and nothing to fall back to.
    $tempOutputPath = [System.IO.Path]::GetTempFileName()
    try {
        $classifyOutput = dotnet run --project $verifyProject -c Release --no-build -- classify `
            $Paths.C210DumpPath $Paths.C208DumpPath $Paths.NetDumpPath $tempOutputPath
        $classifyOutput | Write-Host
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $after = Read-ClassificationTable -Path $tempOutputPath
        $afterCount = $after.Count

        $changed = 0
        foreach ($key in $after.Keys) {
            if (-not $before.ContainsKey($key) -or $before[$key] -ne $after[$key]) { $changed++ }
        }

        # Only now, with a complete and exit-0 classify run sitting safely in a temp file, does the
        # committed golden get touched.
        Copy-Item -LiteralPath $tempOutputPath -Destination $Paths.OutputPath -Force
    }
    finally {
        Remove-Item -LiteralPath $tempOutputPath -ErrorAction SilentlyContinue
    }

    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $prCitation = if ($PR) { "PR #$PR" } else { "(no PR yet -- fill in `"PR #N`" before merging, per CONTRIBUTING.md)" }
    $reasonText = if ($Reason) { $Reason } else { 'Routine re-measurement; no reason given.' }
    $logEntry = "$date $prCitation ($beforeCount -> $afterCount case(s), $changed classification(s) changed): $reasonText"
    Add-Content -Path $logPath -Value $logEntry -Encoding utf8NoBOM

    Write-Host "Logged to $logPath"
}

Write-Host 'Rebuilding all six dumps (2.10.03 C, 2.08 C, the port; both grids -- scripts/run-oracle-dump.ps1)...'
& $dumpScript
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host "Building $verifyProject (Release)..."
dotnet build $verifyProject -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($g in @('Analytic', 'Files')) {
    Write-Host ''
    Invoke-GridClassification -Paths (Get-GridPaths -GridName $g)
}

Write-Host ''
Write-Host 'Review the diff (git diff Tests/oracle/version-classification.tsv Tests/oracle/version-classification-files.tsv) before committing.'
Write-Host 'This script has no PASS/FAIL of its own to satisfy -- judge whether the new counts make sense given what changed'
Write-Host 'since the last run. CI does gate both files: oracle.yml regenerates them and fails if what you commit differs from'
Write-Host 'what its own dumps produce, so commit this run''s output rather than an edited copy of it.'
