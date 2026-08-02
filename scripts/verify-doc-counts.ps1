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

.PARAMETER SelfTest
    Build a throwaway repository whose ground-truth files and documentation are known, plant each
    way a citation has escaped this check, and assert the check's exit code for each. Touches
    nothing outside a temporary directory.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $SelfTest
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

function Get-Utf16DecodedLine {
    # Yields "path:lineno:text" -- the shape `git grep -n` emits -- for every line of every file in
    # $Paths that is actually UTF-16 encoded, and nothing at all for the rest.
    #
    # This exists because `git grep` matches raw BYTES. The -a flag on the scan below stops git
    # SKIPPING a file, which is a different problem from being able to READ one: a file saved as
    # UTF-16LE stores every ASCII character as two bytes, so an ASCII pattern simply does not occur
    # anywhere in it. Confirmed by direct testing -- a tracked file holding the literal marker text
    # saved as UTF-16LE produced no match under -a, under -I, or with no flag at all. "Save as
    # Unicode" in a Windows editor is one click, so a file the scan cannot read is not exotic.
    #
    # Deliberately narrow, so that adding this cannot introduce a false positive: a file qualifies
    # only on a byte-order mark, or on ASCII-in-UTF-16's unmistakable every-other-byte-is-NUL
    # signature (half the sampled bytes NUL on one parity, essentially none on the other). A binary
    # file has NUL bytes on both parities and is not matched -- measured across all 307 tracked
    # files in this repository, including the two tracked .se1 ephemeris binaries: none qualify, so
    # this yields nothing at all here today and can only start yielding when such a file appears.
    #
    # scripts/verify-no-tooling-attribution.ps1 carries its own copy of this function, for the same
    # reason and against the same measurement; the two are independent on purpose, since neither
    # script takes a dependency on the other.
    param([string] $RepoRoot, [string[]] $Paths)

    foreach ($rel in $Paths) {
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $full = Join-Path $RepoRoot $rel
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

        $stream = [System.IO.File]::OpenRead($full)
        try {
            $buffer = New-Object byte[] 4096
            $read = $stream.Read($buffer, 0, $buffer.Length)
        }
        finally { $stream.Dispose() }
        if ($read -lt 2) { continue }

        $encoding = $null
        if ($buffer[0] -eq 0xFF -and $buffer[1] -eq 0xFE) { $encoding = [System.Text.Encoding]::Unicode }
        elseif ($buffer[0] -eq 0xFE -and $buffer[1] -eq 0xFF) { $encoding = [System.Text.Encoding]::BigEndianUnicode }
        else {
            $pairs = [Math]::Floor($read / 2)
            if ($pairs -lt 8) { continue }
            $evenNuls = 0
            $oddNuls = 0
            for ($i = 0; $i -lt $pairs * 2; $i += 2) {
                if ($buffer[$i] -eq 0) { $evenNuls++ }
                if ($buffer[$i + 1] -eq 0) { $oddNuls++ }
            }
            if ($oddNuls -ge $pairs * 0.5 -and $evenNuls -le $pairs * 0.02) { $encoding = [System.Text.Encoding]::Unicode }
            elseif ($evenNuls -ge $pairs * 0.5 -and $oddNuls -le $pairs * 0.02) { $encoding = [System.Text.Encoding]::BigEndianUnicode }
        }
        if (-not $encoding) { continue }

        $lineNumber = 0
        foreach ($line in ([System.IO.File]::ReadAllText($full, $encoding) -split "`r`n|`n|`r")) {
            $lineNumber++
            Write-Output "${rel}:${lineNumber}:$line"
        }
    }
}

function Invoke-DocCountCheck {
    # The whole check, in a function so -SelfTest below can drive it against a throwaway repository.
    # The `exit` statements inside are unchanged and still terminate the script, so CI sees exactly
    # the codes it saw before.
    param([Parameter(Mandatory)][string] $RepoRoot)

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

    # -- nutation-path split: how many rows carry SEFLG_NONUT (64, swephexp.h:193) in their iflag
    # column, opting out of the default nutation path, versus how many do not. A row whose func has
    # no iflag column at all (HOUSES, GET_PLANET_NAME, AYANAMSA, SIDTIME, ...) leaves the field
    # empty, which counts toward "default nutation path" here -- this is a coarse classification
    # ("did this row opt out via SEFLG_NONUT" vs not), not a claim that every counted row actually
    # exercises nutation code internally. docs/compliance-2.10.03.md's own prose describes exactly
    # this classification, not a narrower one.
    function Get-GridNonutOptOutCount {
        param([Parameter(Mandatory)][string] $Path)
        $lines = @(Get-DataRows $Path)
        $header = $lines[0] -split "`t"
        $iflagIdx = Get-TsvColumnIndex -HeaderCells $header -ColumnName 'iflag'
        $rows = @($lines[1..($lines.Count - 1)])
        $optOut = 0
        foreach ($row in $rows) {
            $iflagStr = $row.Split("`t")[$iflagIdx]
            if ($iflagStr -eq '') { continue }
            if (([int64]$iflagStr) -band 64) { $optOut++ }
        }
        return $optOut
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
    # swe_fixstar family subtotal: the -like 'FIXSTAR*' glob below matches all six funcs this grid
    # carries under that name -- FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT, FIXSTAR_MAG and
    # FIXSTAR2_MAG (the glob catches FIXSTAR2_MAG too; an earlier version of this comment named
    # only the first five and said "200", which was the five-func subtotal, not what this glob
    # actually computes) -- cited as "208 across the swe_fixstar family" rather than as six
    # separate inline numbers.
    $GroundTruth['grid-files-fixstar-family-total'] = ($filesGrid.ByFunc.Keys |
        Where-Object { $_ -like 'FIXSTAR*' } | ForEach-Object { $filesGrid.ByFunc[$_] } | Measure-Object -Sum).Sum

    $GroundTruth['grid-total-combined'] = $analyticGrid.Total + $filesGrid.Total

    # -- nutation-path split: see Get-GridNonutOptOutCount's own comment for exactly what "opt out"
    # means here. grid-files.tsv has no row carrying SEFLG_NONUT today, but this is still computed
    # from the file rather than hardcoded to 0, so a future files-grid addition that does carry it
    # fails this check instead of leaving a silently wrong "0 opt out" claim in prose.
    $analyticNonutOptOut = Get-GridNonutOptOutCount (Join-Path $RepoRoot 'Tools/OracleGrid/grid-analytic.tsv')
    $filesNonutOptOut = Get-GridNonutOptOutCount (Join-Path $RepoRoot 'Tools/OracleGrid/grid-files.tsv')
    $GroundTruth['grid-analytic-nonut-optout'] = $analyticNonutOptOut
    $GroundTruth['grid-analytic-default-nutation'] = $analyticGrid.Total - $analyticNonutOptOut
    $GroundTruth['grid-files-nonut-optout'] = $filesNonutOptOut
    $GroundTruth['grid-files-default-nutation'] = $filesGrid.Total - $filesNonutOptOut
    # No grid-total-nonut-optout: today it would always equal grid-analytic-nonut-optout (the files
    # grid opts out 0 rows), so prose cites the analytic-only id directly rather than carrying a
    # second id with no distinct claim behind it -- add one back if a files-grid row ever does
    # carry SEFLG_NONUT and prose needs the combined figure.
    $GroundTruth['grid-total-default-nutation'] = ($analyticGrid.Total + $filesGrid.Total) - ($analyticNonutOptOut + $filesNonutOptOut)

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
    # @(), because a pipeline yielding exactly one surviving path returns that path as a bare
    # string rather than a one-element array, and `Set-StrictMode -Version Latest` makes .Count on
    # a bare string throw -- so this script died with "The property 'Count' cannot be found on this
    # object" instead of reporting anything, in the one case where exactly one of the seven
    # allowlisted documents exists. Found by the self-test below, whose lab has only README.md.
    $docFiles = @($docFileRelPaths | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })

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
    # One pathspec, shared by the byte-level `git grep` below and the UTF-16 pass after it, so the two
    # cannot drift into scanning different sets of files.
    $reversePathspec = @('--', '.', ':!external/*')
    foreach ($rel in $docFileRelPaths) { $reversePathspec += ":!$rel" }
    # Only excludable when this script actually lives inside $RepoRoot -- see the identical guard (and
    # the reason for it) in scripts/verify-no-tooling-attribution.ps1.
    if (-not $selfRelPath.StartsWith('..')) { $reversePathspec += ":!$selfRelPath" }
    $reverseGrepArgs = @('-C', $RepoRoot, 'grep', '-nPa', '--no-color', '-e', '(?i)doccount\s*:') + $reversePathspec

    $reverseGrepOutput = & git @reverseGrepArgs
    if ($LASTEXITCODE -eq 128) {
        throw "git grep exited 128 (not a git repository, or an invalid pathspec) while scanning for doccount: markers outside the allowlist -- output above."
    }
    # git grep exits 1 when nothing matched, which is the expected, good outcome here.

    # The same scan again over the tracked files git grep cannot read at all: a UTF-16-encoded file
    # stores every ASCII character as two bytes, so the pattern above never occurs in its bytes no
    # matter which flags git is given (-a fixes skipping, not decoding -- see Get-Utf16DecodedLine).
    # A marker parked in one was invisible to the check whose entire job is finding markers parked
    # where nothing reads them. Yields nothing for every tracked file in this repository today.
    $reverseScanPaths = & git -C $RepoRoot ls-files @reversePathspec
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files exited $LASTEXITCODE while listing files for the UTF-16 marker scan -- output above."
    }
    $reverseUtf16Output = @(Get-Utf16DecodedLine -RepoRoot $RepoRoot -Paths @($reverseScanPaths) |
        Where-Object { [regex]::IsMatch($_, '(?i)doccount\s*:') })

    foreach ($line in (@($reverseGrepOutput) + $reverseUtf16Output)) {
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
}

# ---------------------------------------------------------------------------------------------

if (-not $SelfTest) {
    Invoke-DocCountCheck -RepoRoot $RepoRoot
    # Unreachable: Invoke-DocCountCheck always exits. Present so that a future edit turning one of
    # those exits into a return cannot make this script pass by falling off the end.
    exit 1
}

# ---------------------------------------------------------------------------------------------
# Self-test. Each case is a citation shape that has escaped this check, or one it must keep
# accepting; each was planted, run, and SEEN to produce the stated exit code AND the stated
# failure message.
#
# The message matters as much as the code here, more than in most self-tests: nearly every plant
# below would still exit 1 if the marker stopped being recognized at all, because an unrecognized
# marker leaves its id with no citation, which is itself a failure. Asserting only the exit code
# would let "the marker is read and its number is wrong" and "the marker is no longer read"
# report identically -- and the second is the silent-defeat mode this check exists to prevent.

$failures = 0
$pwshExe = (Get-Process -Id $PID).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("doc-counts-selftest-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Set-LabFile {
    # Writes a file into the lab and stages it. Staging, not committing: `git grep` and
    # `git ls-files` both read the index, so the lab needs no commit and therefore no identity.
    # Named paths only, never `git add -A`.
    param([string] $LabRoot, [string] $RelPath, [string[]] $Lines, [System.Text.Encoding] $Encoding)

    if (-not $Encoding) { $Encoding = $utf8NoBom }
    $full = Join-Path $LabRoot $RelPath
    New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
    [System.IO.File]::WriteAllText($full, (($Lines -join "`n") + "`n"), $Encoding)
    & git -C $LabRoot add -- $RelPath 2>&1 | Out-Null
}

# The ground-truth files the lab computes its counts from. Small, but the same shape as the real
# ones: a '#' comment block, a header row, then data rows.
$knownFailTsv = @(
    '# lab known-fail rows',
    "iteration`tcategory`tnote",
    "1`tVALUE-MISMATCH`ta",
    "2`tVALUE-MISMATCH`tb",
    "3`tDATA-MISSING`tc")
$oracleKnownDiffTsv = @('# lab', "func`tnote", "CALC`ta", "CALC`tb")
$oracleKnownDiffFilesTsv = @('# lab', "func`tnote", "CALC`ta")
$swetestKnownDiffTsv = @('# lab', "func`tnote", "CALC`ta", "CALC`tb", "CALC`tc", "CALC`td")

# iflag carries SEFLG_NONUT (64) on exactly one row per grid, and one files-grid row leaves iflag
# empty -- the same "no iflag column value for this row's func" shape HOUSES/GET_PLANET_NAME/etc.
# leave in the real grids -- so Get-GridNonutOptOutCount's empty-string tolerance is exercised too.
$gridAnalyticTsv = @(
    '# lab', "func`targ`tiflag",
    "CALC`t1`t0", "CALC`t2`t64",
    "SOLCROSS`t1`t0", "SOLCROSS`t2`t0", "SOLCROSS`t3`t0")
$gridFilesTsv = @(
    '# lab', "func`targ`tiflag",
    "CALC`t1`t0",
    "FIXSTAR`t1`t", "FIXSTAR`t2`t64",
    "MOONCROSS`t1`t0")

# A document citing every id the lab's ground truth defines, each with the value those files
# actually produce. An id with no marker anywhere is a failure in its own right, so this has to be
# complete for the clean case to pass at all.
$readmeLines = @(
    '# Lab',
    '',
    '- known-fail rows: 3<!--doccount:known-fail-total-->',
    '- of them VALUE-MISMATCH: 2<!--doccount:known-fail-value-mismatch-->',
    '- of them DATA-MISSING: 1<!--doccount:known-fail-data-missing-->',
    '- of them ERROR: 0<!--doccount:known-fail-error-->',
    '- of them UNREPRODUCIBLE: 0<!--doccount:known-fail-unreproducible-->',
    '- of them NOT-IMPLEMENTED: 0<!--doccount:known-fail-not-implemented-->',
    '- analytic oracle known-diff rows: 2<!--doccount:oracle-known-diff-analytic-->',
    '- file-backed oracle known-diff rows: 1<!--doccount:oracle-known-diff-files-->',
    '- swetest known-diff rows: 4<!--doccount:swetest-known-diff-->',
    '- analytic grid rows: 5<!--doccount:grid-analytic-total-->',
    '- of them CALC: 2<!--doccount:grid-analytic-func-calc-->',
    '- of them SOLCROSS: 3<!--doccount:grid-analytic-func-solcross-->',
    '- analytic crossing rows: 3<!--doccount:grid-analytic-crossing-total-->',
    '- file-backed grid rows: 4<!--doccount:grid-files-total-->',
    '- of them CALC: 1<!--doccount:grid-files-func-calc-->',
    '- of them FIXSTAR: 2<!--doccount:grid-files-func-fixstar-->',
    '- of them MOONCROSS: 1<!--doccount:grid-files-func-mooncross-->',
    '- file-backed crossing rows: 1<!--doccount:grid-files-crossing-total-->',
    '- the swe_fixstar family: 2<!--doccount:grid-files-fixstar-family-total-->',
    '- both grids together: 9<!--doccount:grid-total-combined-->',
    '- analytic rows opting out of nutation via SEFLG_NONUT: 1<!--doccount:grid-analytic-nonut-optout-->',
    '- analytic rows on the default nutation path: 4<!--doccount:grid-analytic-default-nutation-->',
    '- file-backed rows opting out of nutation via SEFLG_NONUT: 1<!--doccount:grid-files-nonut-optout-->',
    '- file-backed rows on the default nutation path: 3<!--doccount:grid-files-default-nutation-->',
    '- both grids together on the default nutation path: 7<!--doccount:grid-total-default-nutation-->')

# The one line most cases rewrite, matched by its id rather than by index so reordering the
# document above cannot silently point a case at the wrong line.
$totalLine = '- known-fail rows: 3<!--doccount:known-fail-total-->'

function New-DocCountLab {
    # A throwaway repository: the ground-truth files, and a README.md that is the only allowlisted
    # document present. $Readme replaces the document wholesale; $ExtraPath/$ExtraLines add one
    # more tracked file, which is how the reverse-check cases plant a marker where nothing reads it.
    param(
        [string] $Name,
        [string[]] $Readme = $readmeLines,
        [string] $ExtraPath,
        [string[]] $ExtraLines,
        [System.Text.Encoding] $ExtraEncoding)

    $dir = Join-Path $root $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    & git init -q $dir 2>&1 | Out-Null

    Set-LabFile $dir 'Tests/conformance/known-fail.tsv' $knownFailTsv
    Set-LabFile $dir 'Tests/oracle/known-diff.tsv' $oracleKnownDiffTsv
    Set-LabFile $dir 'Tests/oracle/known-diff-files.tsv' $oracleKnownDiffFilesTsv
    Set-LabFile $dir 'Tests/swetest/known-diff.tsv' $swetestKnownDiffTsv
    Set-LabFile $dir 'Tools/OracleGrid/grid-analytic.tsv' $gridAnalyticTsv
    Set-LabFile $dir 'Tools/OracleGrid/grid-files.tsv' $gridFilesTsv
    Set-LabFile $dir 'README.md' $Readme
    if ($ExtraPath) {
        Set-LabFile $dir $ExtraPath $ExtraLines $ExtraEncoding
    }
    return $dir
}

function Assert-Gate {
    # Runs this script's own normal path in a CHILD process, the way CI invokes it, and asserts the
    # exit code -- read straight from $LASTEXITCODE with no pipeline in between, which would report
    # the pipe's last stage instead of the gate's own code. -Matching additionally requires the
    # failure output to say what the case claims it says; see the note at the top of this block for
    # why an exit code alone is not enough evidence here.
    param(
        [string] $Case,
        [ValidateSet('fails', 'passes')][string] $Expect,
        [string] $LabRoot,
        [string] $Matching)

    $output = & $pwshExe -NoProfile -File $PSCommandPath -RepoRoot $LabRoot *>&1
    $code = $LASTEXITCODE
    $text = (@($output) -join "`n")

    $problem = $null
    if ($Expect -eq 'fails' -and $code -eq 0) { $problem = "expected the gate to fail, got exit 0" }
    elseif ($Expect -eq 'passes' -and $code -ne 0) { $problem = "expected the gate to pass, got exit $code" }
    elseif ($Matching -and $text -notmatch $Matching) {
        $problem = "gate exited $code as expected, but for the wrong reason: nothing in its output matched /$Matching/"
    }

    if (-not $problem) {
        Write-Host ("  PASS  {0} (gate {1}, exit {2})" -f $Case, $Expect, $code)
    }
    else {
        Write-Host ("  FAIL  {0}`n          {1}" -f $Case, $problem)
        foreach ($line in @($output)) { Write-Host "            | $line" }
        $script:failures++
    }
}

function Copy-WithTotalLine {
    # The document with its known-fail-total citation rewritten to $Replacement.
    param([string] $Replacement)
    return @($readmeLines | ForEach-Object { if ($_ -eq $totalLine) { $Replacement } else { $_ } })
}

Write-Host 'verify-doc-counts self-test'
Write-Host ''

# 1. The defect this check exists for: a cited number that no longer matches the file it describes.
Assert-Gate 'a cited number that disagrees with the file' 'fails' `
    (New-DocCountLab 'wrong-number' (Copy-WithTotalLine '- known-fail rows: 4<!--doccount:known-fail-total-->')) `
    'doccount:known-fail-total says 4 but the repository currently computes 3'

# 2-4. Markdown emphasis and ordinary prose between the number and its marker. Bolding a number
#      while editing a sentence must not detach it from the marker that checks it -- if it did, the
#      number would go unchecked while the marker sat right beside it looking like it was checked.
#      Each of these still has to report the NUMBER as wrong, not the id as uncited.
Assert-Gate 'a bolded number' 'fails' `
    (New-DocCountLab 'bolded' (Copy-WithTotalLine '- known-fail rows: **9**<!--doccount:known-fail-total-->')) `
    'doccount:known-fail-total says 9 but the repository currently computes 3'

Assert-Gate 'a backticked number' 'fails' `
    (New-DocCountLab 'backticked' (Copy-WithTotalLine '- known-fail rows: `9`<!--doccount:known-fail-total-->')) `
    'doccount:known-fail-total says 9 but the repository currently computes 3'

Assert-Gate 'a word between the number and its marker' 'fails' `
    (New-DocCountLab 'word-between' (Copy-WithTotalLine '- known-fail rows: 9 rows<!--doccount:known-fail-total-->')) `
    'doccount:known-fail-total says 9 but the repository currently computes 3'

# 5-6. Marker syntax written loosely. Neither is someone defeating the check on purpose; both are
#      what an editor's reformatting or a reflexive capitalisation produces, and either one used to
#      un-check the number beside it exactly as thoroughly as deleting the marker would have.
Assert-Gate 'a space after the marker colon' 'fails' `
    (New-DocCountLab 'spaced-colon' (Copy-WithTotalLine '- known-fail rows: 9<!--doccount: known-fail-total-->')) `
    'doccount:known-fail-total says 9 but the repository currently computes 3'

Assert-Gate 'an uppercase DOCCOUNT tag' 'fails' `
    (New-DocCountLab 'uppercase-tag' (Copy-WithTotalLine '- known-fail rows: 9<!--DOCCOUNT:known-fail-total-->')) `
    'doccount:known-fail-total says 9 but the repository currently computes 3'

# 7. An underscored id. The keyword and the colon's spacing are matched loosely; the id itself is
#    strict, so a typo is a hard failure rather than a marker that quietly matches no known id and
#    disappears. Added alongside the correct marker, so the failure can only be the malformed id.
Assert-Gate 'an underscored id' 'fails' `
    (New-DocCountLab 'underscored-id' ($readmeLines + '- known-fail rows: 3<!--doccount:known_fail_total-->')) `
    'malformed doccount marker'

# 8. A marker in a tracked file outside the allowlist. The forward scan only ever opens the
#    allowlisted documents, so a marker anywhere else is not merely unread -- it is invisible,
#    forever, while looking exactly like a checked number to anyone reading the document.
Assert-Gate 'a marker in a file outside the allowlist' 'fails' `
    (New-DocCountLab 'outside-allowlist' -ExtraPath 'docs/notes.md' -ExtraLines @('rows: 9<!--doccount:known-fail-total-->')) `
    "marker found outside the allowlisted document set"

# 9. The same marker in a UTF-16LE file. `git grep` matches bytes, and UTF-16 stores every ASCII
#    character as two of them, so the reverse scan found nothing no matter which flags git was
#    given -- the -a flag stops git skipping a file, which is not the same as being able to read
#    one. Measured before the decode pass was added: this exited 0.
Assert-Gate 'a marker in a UTF-16LE file outside the allowlist' 'fails' `
    (New-DocCountLab 'outside-allowlist-utf16' -ExtraPath 'docs/notes.md' `
        -ExtraLines @('rows: 9<!--doccount:known-fail-total-->') -ExtraEncoding ([System.Text.Encoding]::Unicode)) `
    "marker found outside the allowlisted document set"

# 10. A UTF-16LE file with no marker in it must still pass. The decode pass added for case 9 reads
#     such files; this is what keeps it a marker check rather than an encoding policy.
Assert-Gate 'a UTF-16LE file with no marker in it' 'passes' `
    (New-DocCountLab 'utf16-clean' -ExtraPath 'docs/notes.md' `
        -ExtraLines @('an ordinary note with no citation in it') -ExtraEncoding ([System.Text.Encoding]::Unicode))

# 11. A marker deleted rather than kept in sync. This is how the check would otherwise be defeated
#     without anyone touching this script: the prose silently goes back to being unchecked.
Assert-Gate 'a defined id whose only marker was deleted' 'fails' `
    (New-DocCountLab 'deleted-marker' @($readmeLines | Where-Object { $_ -ne $totalLine })) `
    'doccount:known-fail-total is defined in this script but has no marker'

# 12. The correct document. Without this every case above could be satisfied by a check that fails
#     on everything.
Assert-Gate 'the correct document' 'passes' (New-DocCountLab 'correct')

Write-Host ''
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    Write-Host "FAIL: $failures self-test case(s) failed."
    exit 1
}
Write-Host 'PASS: all verify-doc-counts self-test cases passed.'
exit 0
