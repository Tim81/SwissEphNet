#Requires -Version 7
<#
.SYNOPSIS
    Recomputes the load-bearing counts this repository's documentation cites in prose, and fails
    when a cited number disagrees with what the repository's own files currently say.

.DESCRIPTION
    Prose drifts from the files it describes silently, because nothing re-reads the file when the
    prose is written and nothing re-reads the prose when the file changes. That happened
    repeatedly in this repository: README.md cited 1,435 known-fail rows when the file held 1,427;
    a compliance record said macOS coverage was unmeasured after it had been measured; the two
    bit-exact oracle grids were each described about 820 rows short of their true count;
    oracle.yml said Tests/swetest/known-diff.tsv held 24 rows when it held 21. Each was a number a
    person typed once and never re-checked, not a computation anything replayed.

    This script is the re-check. It computes ground truth directly from the tracked files that
    define each count (Tests/conformance/known-fail.tsv, Tests/oracle/known-diff*.tsv,
    Tests/swetest/known-diff.tsv, Tools/OracleGrid/grid-*.tsv) and compares it against every
    citation marker found in a fixed set of documentation files.

    Citation markers, not a shared manifest. A document declares a number as checkable by placing
    an HTML comment immediately after it: `1,427<!--doccount:known-fail-total-->`. The alternative
    -- a manifest file listing "known-fail-total = 1427" that prose is supposed to stay in sync
    with -- doubles the editing burden for exactly the person this script exists to help: someone
    writing a sentence around a number does not also want to open and hand-edit a second file, and
    a manifest nobody is looking at while writing prose is exactly as likely to drift as the prose
    itself. A marker sits at the point of use, survives a copy-paste of the sentence into another
    document, and is visible in the same diff as the number it guards -- a reviewer sees both
    change together or neither change at all.

    Two failure modes, both deliberate:
      - A marker's number disagrees with the computed value: FAIL, with both numbers shown.
      - A defined ID has zero markers anywhere in the scanned documents: FAIL. A marker deleted
        (rather than kept in sync) is how this check would otherwise be silently defeated -- the
        prose would go back to being unchecked without anyone having to touch this script.
    An ID this script does not define is simply not checked (a marker typo, or a number nobody has
    made checkable yet) -- a false negative, not a false positive. Extending coverage means adding
    an ID to $GroundTruth below and a matching marker in prose; it never means editing both a doc
    and a separate manifest to keep two hand-maintained copies of the same number aligned.

    Scope, "at minimum" per the class of defect this exists to catch: known-fail.tsv's total row
    count and its category split; known-diff.tsv's row count for both oracle grids and for
    swetest; and both oracle grid row counts together with their per-`func` breakdown. Markers are
    currently placed in README.md, CONTRIBUTING.md, docs/compliance-2.10.03.md,
    docs/known-issues.md and .github/workflows/oracle.yml -- wherever a load-bearing number from
    that list is currently written out. Not every mention of every number in the repository carries
    a marker (some are historical, e.g. "4,382 rows when the oracle was first wired up", which
    this script is not meant to re-derive); a mention without a marker is not gated, by design.

    docs/upstream/ and external/ are out of scope: the former is untracked scratch work, the
    latter is Astrodienst's own vendored source, not this repository's documentation.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DataRows {
    # Non-blank, non-comment ('#'-prefixed) lines. Every *.tsv this script reads starts with a
    # block of '#' commentary (see e.g. Tools/OracleGrid/grid-analytic.tsv's own header) followed
    # by a header row and then data rows.
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Ground-truth file not found: $Path"
    }
    return @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() -ne '' -and -not $_.StartsWith('#') })
}

function Get-TsvColumnIndex {
    param([string[]] $HeaderCells, [string] $ColumnName)
    $idx = [array]::IndexOf($HeaderCells, $ColumnName)
    if ($idx -lt 0) { throw "Column '$ColumnName' not found in header: $($HeaderCells -join ', ')" }
    return $idx
}

function ConvertTo-DocCountId {
    # HOUSES_ARMC -> houses-armc; matches the id vocabulary used in the marker comments below.
    param([string] $Name)
    return $Name.ToLowerInvariant().Replace('_', '-')
}

# ---------------------------------------------------------------------------
# Ground truth: computed directly from the files each count actually describes.
# ---------------------------------------------------------------------------
$GroundTruth = [ordered]@{}

# -- known-fail.tsv: total rows and the category split --------------------------------------
$knownFailPath = Join-Path $RepoRoot 'Tests/conformance/known-fail.tsv'
$knownFailLines = @(Get-DataRows $knownFailPath)
$knownFailHeader = $knownFailLines[0] -split "`t"
$knownFailRows = @($knownFailLines[1..($knownFailLines.Count - 1)])
$catIdx = Get-TsvColumnIndex -HeaderCells $knownFailHeader -ColumnName 'category'

$GroundTruth['known-fail-total'] = $knownFailRows.Count

# All five categories ConformanceReport can emit (see CONTRIBUTING.md, "Correctness oracle
# known-fail list"), listed explicitly so a category with zero current rows still has a
# ground-truth entry of 0 rather than silently having none at all.
foreach ($category in @('VALUE-MISMATCH', 'DATA-MISSING', 'ERROR', 'UNREPRODUCIBLE', 'NOT-IMPLEMENTED')) {
    $id = 'known-fail-' + (ConvertTo-DocCountId $category)
    $GroundTruth[$id] = @($knownFailRows | Where-Object { $_.Split("`t")[$catIdx] -eq $category }).Count
}

# -- known-diff.tsv row counts: both oracle grids and swetest --------------------------------
$GroundTruth['oracle-known-diff-analytic'] = @(Get-DataRows (Join-Path $RepoRoot 'Tests/oracle/known-diff.tsv')).Count - 1
$GroundTruth['oracle-known-diff-files'] = @(Get-DataRows (Join-Path $RepoRoot 'Tests/oracle/known-diff-files.tsv')).Count - 1
$GroundTruth['swetest-known-diff'] = @(Get-DataRows (Join-Path $RepoRoot 'Tests/swetest/known-diff.tsv')).Count - 1

# -- the two oracle grids: total rows and their per-func breakdown ---------------------------
function Get-GridFuncCounts {
    param([Parameter(Mandatory)][string] $Path)
    $lines = @(Get-DataRows $Path)
    $header = $lines[0] -split "`t"
    $funcIdx = Get-TsvColumnIndex -HeaderCells $header -ColumnName 'func'
    $rows = @($lines[1..($lines.Count - 1)])
    $byFunc = [ordered]@{}
    foreach ($row in $rows) {
        $func = $row.Split("`t")[$funcIdx]
        if (-not $byFunc.Contains($func)) { $byFunc[$func] = 0 }
        $byFunc[$func] = $byFunc[$func] + 1
    }
    return [pscustomobject]@{ Total = $rows.Count; ByFunc = $byFunc }
}

$analyticGrid = Get-GridFuncCounts (Join-Path $RepoRoot 'Tools/OracleGrid/grid-analytic.tsv')
$GroundTruth['grid-analytic-total'] = $analyticGrid.Total
foreach ($func in $analyticGrid.ByFunc.Keys) {
    $GroundTruth['grid-analytic-func-' + (ConvertTo-DocCountId $func)] = $analyticGrid.ByFunc[$func]
}
# Crossing-family subtotal (HELIO_CROSS[_UT], SOLCROSS[_UT], MOONCROSS[_UT], MOONCROSS_NODE[_UT]):
# docs/compliance-2.10.03.md cites this as "plus 600 crossing rows" rather than spelling out all
# eight counts inline a second time.
$GroundTruth['grid-analytic-crossing-total'] = ($analyticGrid.ByFunc.Keys |
    Where-Object { $_ -like '*CROSS*' } | ForEach-Object { $analyticGrid.ByFunc[$_] } | Measure-Object -Sum).Sum

$filesGrid = Get-GridFuncCounts (Join-Path $RepoRoot 'Tools/OracleGrid/grid-files.tsv')
$GroundTruth['grid-files-total'] = $filesGrid.Total
foreach ($func in $filesGrid.ByFunc.Keys) {
    $GroundTruth['grid-files-func-' + (ConvertTo-DocCountId $func)] = $filesGrid.ByFunc[$func]
}
$GroundTruth['grid-files-crossing-total'] = ($filesGrid.ByFunc.Keys |
    Where-Object { $_ -like '*CROSS*' } | ForEach-Object { $filesGrid.ByFunc[$_] } | Measure-Object -Sum).Sum
# swe_fixstar family subtotal (FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT, FIXSTAR_MAG): cited as
# "200 across the swe_fixstar family" rather than as five separate inline numbers.
$GroundTruth['grid-files-fixstar-family-total'] = ($filesGrid.ByFunc.Keys |
    Where-Object { $_ -like 'FIXSTAR*' } | ForEach-Object { $filesGrid.ByFunc[$_] } | Measure-Object -Sum).Sum

$GroundTruth['grid-total-combined'] = $analyticGrid.Total + $filesGrid.Total

# ---------------------------------------------------------------------------
# Scan the documents for citation markers and check each one.
# ---------------------------------------------------------------------------
$docFiles = @(
    'README.md',
    'CONTRIBUTING.md',
    'docs/compliance-2.10.03.md',
    'docs/known-issues.md',
    '.github/workflows/oracle.yml',
    '.github/workflows/conformance.yml',
    '.github/workflows/baseline.yml'
) | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

# A number, then a run of one or more marker tags. A run (not a single tag) is required because
# two documents cite one shared number for two different functions at once, e.g.
# "HELIO_CROSS/HELIO_CROSS_UT 192 each" -- both markers sit back-to-back after the one "192".
# The id itself is kebab-case: alphanumeric segments joined by single dashes. Written this way,
# not as the simpler-looking "[A-Za-z0-9-]+", specifically so the id can never swallow the "--"
# that opens the tag's own closing "-->" -- a greedy character class containing '-' does exactly
# that (matches "known-fail-total--" instead of "known-fail-total"), since '-' is a valid id
# character right up until two of them appear in a row, which only the closing delimiter does.
$idPattern = '[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*'
$markerRunPattern = "(?<num>[\d,]+)(?<tags>(?:\s*<!--\s*doccount:$idPattern\s*-->)+)"
$singleTagPattern = "doccount:(?<id>$idPattern)\s*-->"

$failures = [System.Collections.Generic.List[string]]::new()
$seenIds = [System.Collections.Generic.HashSet[string]]::new()

foreach ($docPath in $docFiles) {
    $relPath = [System.IO.Path]::GetRelativePath($RepoRoot, $docPath).Replace('\', '/')
    $text = Get-Content -LiteralPath $docPath -Raw

    foreach ($match in [regex]::Matches($text, $markerRunPattern)) {
        $docValue = [int] ($match.Groups['num'].Value -replace ',', '')
        # 1-based line number: count newlines in everything before the match.
        $lineNumber = ([regex]::Matches($text.Substring(0, $match.Index), "`n")).Count + 1

        foreach ($tagMatch in [regex]::Matches($match.Groups['tags'].Value, $singleTagPattern)) {
            $id = $tagMatch.Groups['id'].Value
            [void]$seenIds.Add($id)

            if (-not $GroundTruth.Contains($id)) {
                $failures.Add("${relPath}:${lineNumber}: doccount:$id is not a defined id (typo, or not yet added to the GroundTruth table in this script).")
                continue
            }

            $actual = $GroundTruth[$id]
            if ($docValue -ne $actual) {
                $failures.Add("${relPath}:${lineNumber}: doccount:$id says $docValue but the repository currently computes $actual.")
            }
        }
    }
}

foreach ($id in $GroundTruth.Keys) {
    if (-not $seenIds.Contains($id)) {
        $tag = '<!--doccount:' + $id + '-->'
        $failures.Add("doccount:$id is defined in this script but has no marker anywhere in the scanned documents -- either add $tag next to the number it checks, or remove the id from the GroundTruth table in this script if nothing cites it any more.")
    }
}

Write-Host "Checked $($seenIds.Count) distinct doccount id(s) across $($docFiles.Count) document(s)."

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) { Write-Host "  $failure" }
    Write-Host ''
    Write-Host 'FAIL: a documented count disagrees with what the repository currently computes (or a defined count has no citation to check). See scripts/regenerate-known-fail.ps1 / scripts/classify-oracle-versions.ps1 if the underlying files changed for a real reason; otherwise fix the prose.'
    exit 1
}

Write-Host 'PASS: every doccount marker matches the repository, and every defined count has at least one citation.'
exit 0
