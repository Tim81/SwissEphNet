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
    swetest; and both oracle grid row counts together with their per-`func` breakdown. $docFiles
    below -- README.md, CONTRIBUTING.md, docs/compliance-2.10.03.md, docs/known-issues.md,
    .github/workflows/oracle.yml, .github/workflows/conformance.yml and .github/workflows/
    baseline.yml -- is the allowlist of files a marker is permitted to live in; a marker anywhere
    else is a failure in its own right (see "Reverse check" below), not merely unread. As of this
    writing, markers actually appear in README.md, CONTRIBUTING.md, docs/compliance-2.10.03.md and
    .github/workflows/oracle.yml; docs/known-issues.md, conformance.yml and baseline.yml are
    allowlisted destinations that happen to hold none today, not files this script currently reads
    a number out of. Not every mention of every number in the repository carries a marker (some
    are historical, e.g. "4,382 rows when the oracle was first wired up", which this script is not
    meant to re-derive); a mention without a marker is not gated, by design.

    Reverse check: a marker outside $docFiles is invisible to every check above by construction --
    the loop above only ever opens the files in that list, so `9,999<!--doccount:known-fail-total-->`
    pasted into a new, un-allowlisted document (or a workflow file not in the list) was previously
    unchecked, silently, forever, which is the opposite of this script's own selling point that a
    marker "survives a copy-paste into another document". This script also greps every tracked file
    outside $docFiles (and outside its own path, which explains the marker syntax in prose above)
    for the literal marker delimiter and fails if it finds one -- a marker has to be moved into an
    allowlisted document or deleted, not merely left where nothing reads it.

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
$docFileRelPaths = @(
    'README.md',
    'CONTRIBUTING.md',
    'docs/compliance-2.10.03.md',
    'docs/known-issues.md',
    '.github/workflows/oracle.yml',
    '.github/workflows/conformance.yml',
    '.github/workflows/baseline.yml'
)
$docFiles = $docFileRelPaths | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

# Marker detection is deliberately loose (case-insensitive "doccount", optional whitespace
# around the colon and inside the delimiters) because the failure mode this guards against is
# not "someone writes a marker wrong on purpose" but "someone reformats prose near a marker
# without noticing the marker has syntax". `<!--DOCCOUNT: known-fail-total-->` and
# `<!--doccount:known-fail-total-->` must be equally visible to this script, or the case/spacing
# accident silently un-checks the number next to it -- exactly as invisible as no marker at all.
# The id capture itself stays strict (kebab-case only, matching $GroundTruth's own key shape) so
# a malformed id -- `known_fail_total` with underscores, a typo -- is a hard failure below rather
# than a run that quietly fails to match anything and vanishes the same way.
$idPattern = '[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*'
$markerPattern = '(?i)<!--\s*doccount\s*:\s*(?<idraw>[^>]*?)\s*-->'

$failures = [System.Collections.Generic.List[string]]::new()
$seenIds = [System.Collections.Generic.HashSet[string]]::new()

foreach ($docPath in $docFiles) {
    $relPath = [System.IO.Path]::GetRelativePath($RepoRoot, $docPath).Replace('\', '/')
    $text = Get-Content -LiteralPath $docPath -Raw

    foreach ($match in [regex]::Matches($text, $markerPattern)) {
        # 1-based line number, and the start/prefix of that same line up to the marker itself.
        $beforeMatch = $text.Substring(0, $match.Index)
        $lastNewline = $beforeMatch.LastIndexOf("`n")
        $lineStart = $lastNewline + 1
        $lineNumber = ([regex]::Matches($beforeMatch, "`n")).Count + 1
        $linePrefix = $text.Substring($lineStart, $match.Index - $lineStart)

        $idRaw = $match.Groups['idraw'].Value
        if ($idRaw.Trim() -eq '') {
            # `<!--doccount:-->` with nothing between the colon and the close: this script's own
            # documentation (CONTRIBUTING.md) illustrates the marker syntax generically this way,
            # with no id at all, not even a malformed one -- not an attempted citation of any
            # number, so there is nothing to validate or bind a number to. Treated the same as "no
            # marker here" rather than as a malformed one.
            continue
        }
        if ($idRaw -notmatch "^$idPattern$") {
            $failures.Add("${relPath}:${lineNumber}: malformed doccount marker '$idRaw' -- an id must be lowercase letters, digits and single dashes only (e.g. 'known-fail-total'); underscores, spaces or mixed case inside the id are never valid, even though the 'doccount' keyword and the colon's spacing are matched case- and whitespace-insensitively above.")
            continue
        }
        $id = $idRaw
        [void]$seenIds.Add($id)

        # The number this marker cites: the last run of digits/commas on the same line before the
        # marker's own opening delimiter, with any earlier marker tags on that line stripped out
        # first. Stripping matters when two or more markers chain back-to-back after one shared
        # number (e.g. "48<!--doccount:...fixstar--><!--doccount:...fixstar2-->" for the second
        # marker) -- an id containing a digit itself, like "fixstar2", would otherwise be picked up
        # as the "number" instead of the real, earlier "48". Deliberately NOT anchored to require
        # the number be immediately adjacent: "**9,999**<!--...-->", "`9,999`<!--...-->" and
        # "9,999 rows<!--...-->" must all still resolve to 9,999 -- markdown emphasis, inline-code
        # backticks and ordinary prose words between the number and its marker are exactly the kind
        # of incidental edit (bolding a number while editing a sentence) that must not silently
        # detach the marker from the number it is checking.
        $strippedPrefix = [regex]::Replace($linePrefix, '<!--.*?-->', '')
        $numMatches = [regex]::Matches($strippedPrefix, '[\d,]+')
        if ($numMatches.Count -eq 0) {
            $failures.Add("${relPath}:${lineNumber}: doccount:$id has no number anywhere earlier on its line to check against.")
            continue
        }
        $docValue = [int] ($numMatches[$numMatches.Count - 1].Value -replace ',', '')

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

foreach ($id in $GroundTruth.Keys) {
    if (-not $seenIds.Contains($id)) {
        $tag = '<!--doccount:' + $id + '-->'
        $failures.Add("doccount:$id is defined in this script but has no marker anywhere in the scanned documents -- either add $tag next to the number it checks, or remove the id from the GroundTruth table in this script if nothing cites it any more.")
    }
}

# ---------------------------------------------------------------------------
# Reverse check: no doccount: marker outside the $docFileRelPaths allowlist.
# ---------------------------------------------------------------------------
# The loop above only ever opens the files in $docFileRelPaths, so a marker anywhere else is
# invisible to every check in this script by construction -- not a false negative, a silent
# non-check. `9,999<!--doccount:known-fail-total-->` pasted into a brand-new document, or into a
# workflow file that is not already one of the seven above, previously exited 0 forever. This
# greps every other tracked file for the marker text and fails if it finds one; a marker has to
# live in an allowlisted document (move it) or not exist (delete it), never sit somewhere nothing
# reads it.
#
# -P (PCRE) with an explicit "(?i)" -- git grep's default POSIX BRE dialect has no case-insensitive
# mode, and the loop above now recognizes "doccount:" case-insensitively (`<!--DOCCOUNT:...-->`),
# so a marker written that way in a non-allowlisted file must be just as visible here or this
# reverse check misses exactly the case variant the forward check was widened to catch.
#
# -a ("--text"), not -I: -I skips any file git's own content heuristic calls binary AND any file
# an applicable .gitattributes entry marks `binary` or `-diff` -- confirmed by direct testing, both
# independently make a real text file (one with a single stray embedded NUL byte, or one merely
# tagged `binary` in .gitattributes despite being ordinary UTF-8) invisible to -I regardless of
# what it actually contains. "Save as Unicode"/UTF-16 in a Windows editor produces exactly the
# first case; a future .gitattributes entry (this file already ships commented-out `binary`
# template blocks) produces the second. -a forces every tracked file to be scanned as text
# regardless of either signal, closing both at once; the only tracked binaries in this repository
# (Tests/SwissEphNet.Tests/files/*.se1) were confirmed by direct testing to produce no match and
# no error under -a, so nothing legitimate is lost.
$selfRelPath = [System.IO.Path]::GetRelativePath($RepoRoot, $PSCommandPath).Replace('\', '/')
$reverseGrepArgs = @('-C', $RepoRoot, 'grep', '-nPa', '--no-color', '-e', '(?i)doccount\s*:', '--', '.', ':!external/*')
foreach ($rel in $docFileRelPaths) { $reverseGrepArgs += ":!$rel" }
# Only excludable when this script actually lives inside $RepoRoot -- see the identical guard (and
# the reason for it) in scripts/verify-no-tooling-attribution.ps1.
if (-not $selfRelPath.StartsWith('..')) { $reverseGrepArgs += ":!$selfRelPath" }

$reverseGrepOutput = & git @reverseGrepArgs
if ($LASTEXITCODE -eq 128) {
    throw "git grep exited 128 (not a git repository, or an invalid pathspec) while scanning for doccount: markers outside the allowlist -- output above."
}
# git grep exits 1 when nothing matched, which is the expected, good outcome here.

foreach ($line in @($reverseGrepOutput)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $failures.Add(
        "$line -- 'doccount:' marker found outside the allowlisted document set " +
        "($($docFileRelPaths -join ', ')); every check above only reads those files, so this " +
        "marker is invisible to them. Move it into an allowlisted document, or remove it.")
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
