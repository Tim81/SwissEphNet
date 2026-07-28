#Requires -Version 7
<#
.SYNOPSIS
    Per-file C diff between the Swiss Ephemeris 2.08 baseline and the 2.10.03 upstream vendored
    as a submodule, for a porting reviewer to check a C# hunk citation against.

.DESCRIPTION
    Compares external/pyswisseph-2.08/<file> (the 2.08 baseline; see
    scripts/fetch-2.08-baseline.ps1) against external/swisseph/<file> (the v2.10.3final
    submodule). Those two fixed paths are the ONLY 2.08 and 2.10.3 inputs this script has --
    there is no parameter that accepts a different 2.08 source, and in particular no parameter
    or code path that can point at the aloistr/swisseph `v2.08.00a` git tag. That tag is an
    incomplete snapshot (missing swecl.c, swehouse.c, swehel.c entirely, and a truncated
    swephexp.h) and diffing against it silently produces a wrong work queue. See
    CONTRIBUTING.md and scripts/fetch-2.08-baseline.ps1.

    Two things make the output usable for review instead of just noisy:

    * License-noise filter (on by default, -IncludeLicenseHunks to see it anyway). Every file
      Astrodienst re-licensed from GPL-2 to AGPL-3 carries the same header rewrite -- the
      copyright year, "GNU public license" -> "GNU Affero General Public License", the GPL/AGPL
      URL, and so on. Hunks whose every changed line matches one of a fixed set of known license
      phrases are dropped from the reported diff and counted separately, so what is left is the
      part a porting reviewer actually has to read.

    * Comments-stripped variant for headers (-File *.h, on by default). Header files are mostly
      doc comments; a raw diff over-counts because the license rewrite and other comment-only
      edits sit close enough to real declaration changes that they land in the same hunk (unlike
      in the .c files, where the header is usually its own isolated hunk). Stripping /* ... */
      comments from both sides before diffing isolates the actual code change -- the real
      `#define`/prototype/struct-field delta -- from prose noise. Reported alongside the raw
      diff, not instead of it.

.PARAMETER File
    A single file name, e.g. sweph.c. If omitted, every file present on both sides is processed
    and a one-line summary is printed for each; the full diff body is only printed when -File
    names exactly one file.

.PARAMETER IncludeLicenseHunks
    Do not filter out the GPL-2 -> AGPL-3 header rewrite hunks -- prints the RAW, unfiltered
    diff (every hunk, license noise included), the same as if the filter did not exist.

.PARAMETER ShowDroppedLicenseHunks
    Print exactly the hunks the license-noise filter dropped (and nothing else), so a reviewer
    can audit what was excluded instead of trusting only the license-noise count. Different
    from -IncludeLicenseHunks, which prints everything unfiltered; this prints only what was
    filtered out.

.PARAMETER NoCommentStrip
    Skip the comments-stripped variant even for header files.
#>
[CmdletBinding()]
param(
    [string] $File,
    [switch] $IncludeLicenseHunks,
    [switch] $ShowDroppedLicenseHunks,
    [switch] $NoCommentStrip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Fixed, not parameterized -- see the guard-rail note in the synopsis above. The 2.08 side is
# always what scripts/fetch-2.08-baseline.ps1 produced and verified; the 2.10.3 side is always
# the pinned submodule checkout. Neither is ever a git tag reference.
$baselineDir = Join-Path $repoRoot 'external/pyswisseph-2.08'
$submoduleDir = Join-Path $repoRoot 'external/swisseph'
$manifestPath = Join-Path $PSScriptRoot 'pyswisseph-2.08.manifest.tsv'

# "Is a directory containing at least one file" only proves something was fetched once; it
# says nothing about whether it still matches scripts/pyswisseph-2.08.manifest.tsv as it
# stands today. fetch-2.08-baseline.ps1 writes a stamp (the manifest's own sha256) only after
# every file passes verification against that same manifest; requiring the stamp to match the
# CURRENT manifest re-couples "verified" to "about to be consumed" without re-hashing all 31
# files on every gen-delta.ps1 invocation. A manifest that changed since the directory was
# last fetched (e.g. rows quietly removed) is treated the same as "never fetched".
function Test-BaselineVerified {
    param([string] $BaselineDir, [string] $ManifestPath)
    $stampPath = Join-Path $BaselineDir '.manifest-sha256'
    if (-not (Test-Path -LiteralPath $stampPath -PathType Leaf) -or -not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return $false
    }
    $stamped = (Get-Content -LiteralPath $stampPath -Raw).Trim()
    $current = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    return $stamped -eq $current
}

if (-not (Test-Path -LiteralPath $baselineDir -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $baselineDir -File -ErrorAction SilentlyContinue) -or
    -not (Test-BaselineVerified -BaselineDir $baselineDir -ManifestPath $manifestPath)) {
    Write-Host "2.08 baseline not found or not verified against the current manifest at $baselineDir -- fetching it."
    & (Join-Path $PSScriptRoot 'fetch-2.08-baseline.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: could not prepare the 2.08 baseline.'
        exit 1
    }
}

if (-not (Test-Path -LiteralPath $submoduleDir -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $submoduleDir -Filter '*.c' -File -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: $submoduleDir has no C sources checked out."
    Write-Host 'Run: git submodule update --init external/swisseph'
    exit 1
}

# "Has at least one .c file" also passes for a submodule checked out at the wrong commit --
# in particular the aloistr/swisseph `v2.08.00a` tag, an incomplete snapshot missing
# swecl.c/swehouse.c/swehel.c entirely and truncating swephexp.h, but which still contains
# plenty of *other* .c files and so clears that bar easily. `git -C $repoRoot rev-parse
# HEAD:external/swisseph` reads the commit the superproject's own tree pins the submodule to
# (the gitlink) -- not a second hardcoded copy of the pinned SHA -- and this asserts the
# actual checkout matches it exactly.
$expectedSubmoduleCommit = & git -C $repoRoot rev-parse 'HEAD:external/swisseph' 2>$null
if ($LASTEXITCODE -ne 0 -or -not $expectedSubmoduleCommit) {
    Write-Host "FAIL: could not resolve the pinned commit for external/swisseph from the superproject's tree (git rev-parse HEAD:external/swisseph)."
    exit 1
}
$expectedSubmoduleCommit = $expectedSubmoduleCommit.Trim()

$actualSubmoduleCommit = & git -C $submoduleDir rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or -not $actualSubmoduleCommit) {
    Write-Host "FAIL: could not resolve external/swisseph's currently checked-out commit (git -C external/swisseph rev-parse HEAD). Is it a valid git checkout?"
    exit 1
}
$actualSubmoduleCommit = $actualSubmoduleCommit.Trim()

if ($actualSubmoduleCommit -ne $expectedSubmoduleCommit) {
    Write-Host "FAIL: external/swisseph is checked out at $actualSubmoduleCommit, but the superproject pins it at $expectedSubmoduleCommit."
    Write-Host 'This is exactly the failure mode this script exists to prevent: a checkout of the wrong commit (e.g. the aloistr/swisseph v2.08.00a tag, an incomplete snapshot missing swecl.c/swehouse.c/swehel.c entirely) passes the "has some .c files" check above and silently produces a wrong work queue.'
    Write-Host "Run: git -C external/swisseph fetch origin && git -C external/swisseph checkout $expectedSubmoduleCommit"
    exit 1
}
Write-Host "PASS: external/swisseph is checked out at the pinned commit ($expectedSubmoduleCommit)."

# Known GPL-2 -> AGPL-3 header-rewrite phrases. A hunk is license noise only if every one of its
# changed (+/-) lines matches at least one of these -- a hunk that mixes a license-text change
# with a real code change is deliberately NOT filtered.
$licensePatterns = @(
    'Copyright \(C\) 1997 - \d{4} Astrodienst AG'
    'GNU public license version 2 or later'
    'GNU Affero General Public License \(AGPL\)'
    'GNU GPL software license'
    'AGPL software license'
    'GNU GPL or a compatible license'
    'AGPL or a compatible license'
    'gpl-2\.0\.html'
    'agpl-3\.0\.html'
    '\$Header: /home/dieter/sweph/RCS/'
    '^\+?-?\s*\*+/?\s*$'                       # bare comment border lines ( /*, */, blank-ish )
    '^\s*$'                                    # blank / trailing-whitespace-only lines inside
                                                # the license comment block getting trimmed
)
$licenseRegex = ($licensePatterns | ForEach-Object { "($_)" }) -join '|'

function Get-NormalizedLines {
    param([string] $Path)
    $text = [System.IO.File]::ReadAllText($Path)
    $text = $text -replace "`r`n", "`n" -replace "`r", "`n"
    return $text -split "`n"
}

function Write-NormalizedTemp {
    param([string] $Path, [string] $TempPath)
    $lines = Get-NormalizedLines -Path $Path
    [System.IO.File]::WriteAllText($TempPath, ($lines -join "`n"))
}

function Strip-CComments {
    param([string] $Path, [string] $TempPath)
    $text = [System.IO.File]::ReadAllText($Path)
    $text = $text -replace "`r`n", "`n" -replace "`r", "`n"
    $stripped = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $lines = $stripped -split "`n" | Where-Object { $_.Trim() -ne '' }
    [System.IO.File]::WriteAllText($TempPath, ($lines -join "`n"))
}

function Get-Hunks {
    param([string] $DiffText)
    # Each hunk is a { Header; Lines } object rather than a flat list of changed lines. Header
    # keeps the raw '@@ -a,b +c,d @@ ...' text -- the only place the file's line numbers survive
    # -- and Lines keeps every context/add/del line inside the hunk, each tagged with its Type
    # so a hunk reads as a hunk (see CONTRIBUTING.md's citation requirement and the splicing bug
    # this replaces). The Type tag, not the leading character, is what Test-LicenseHunk filters
    # on below, so a context line can never be mistaken for a change line by that classifier.
    $hunks = [System.Collections.Generic.List[object]]::new()
    if ($DiffText) {
        $lines = $DiffText -split "`n"
        $current = $null
        foreach ($line in $lines) {
            if ($line.StartsWith('@@')) {
                if ($current) { $hunks.Add($current) }
                $current = [pscustomobject]@{
                    Header = $line
                    Lines  = [System.Collections.Generic.List[object]]::new()
                }
                continue
            }
            if ($null -eq $current) { continue }
            # No `--- a/file` / `+++ b/file` skip here, deliberately. Those headers precede the
            # first @@, so the line above has already skipped them while $current is null. A
            # skip at this point can therefore only ever fire *inside* a hunk, where the only
            # things matching are real source lines: a deleted line whose own text starts with
            # `--`, or an added one starting with `++`. A column-0 `----------------` separator
            # comment is ordinary C, and the check silently removed it from both the rendered
            # body and the +/- counts, so the hunk read as "nothing changed here" with a
            # citation vouching for it. Verified by deletion: output is byte-identical across
            # all 31 files with the check gone, because it never had a header to catch.
            if ($line.StartsWith('+')) {
                $current.Lines.Add([pscustomobject]@{ Type = 'add'; Text = $line.Substring(1) })
            }
            elseif ($line.StartsWith('-')) {
                $current.Lines.Add([pscustomobject]@{ Type = 'del'; Text = $line.Substring(1) })
            }
            elseif ($line.StartsWith(' ')) {
                $current.Lines.Add([pscustomobject]@{ Type = 'context'; Text = $line.Substring(1) })
            }
            # Anything else here ('\ No newline at end of file', a stray blank split artifact) is
            # diff metadata, not a source line -- same as before, it is not stored.
        }
        if ($current) { $hunks.Add($current) }
    }
    # Write-Output -NoEnumerate, not a plain `return $hunks`: a List[object] with zero
    # elements, returned normally, is enumerated by PowerShell's output pipeline into zero
    # emitted objects -- the caller's `$rawHunks = Invoke-...` then binds to $null, not an
    # empty collection, and every later `$rawHunks.Count` throws under Set-StrictMode
    # ("cannot call a method on a null-valued expression" / "property 'Count' cannot be
    # found"). Confirmed live: two files identical after CRLF/LF normalization (e.g.
    # seleapsec.txt, LF on the 2.08 side vs CRLF in the submodule) produce an empty diff and
    # crash all-files mode entirely on exactly this. -NoEnumerate always passes the List
    # itself through as one object, empty or not.
    Write-Output -NoEnumerate $hunks
}

# A line that would otherwise match one of the license phrases above (in particular the two
# bare URL substrings, 'gpl-2\.0\.html' / 'agpl-3\.0\.html', which say nothing about WHERE on
# the line they appear) must never be treated as license noise if it also looks like real code.
# The concrete failure this guards: a hunk line like
#   -#define NDIURN 1 /* ...gpl-2.0.html */
#   +#define NDIURN 2 /* ...agpl-3.0.html */
# carries a real value change (1 -> 2) riding along with the relicensing URL rewrite that
# genuinely does touch every file -- the URL substring matching is not wrong to have, but
# letting it swallow a line with a preprocessor directive or a statement on it is. The
# multi-line block-comment header itself (the common, legitimate case this filter exists for)
# is prose with no preprocessor directives or statement terminators on any of its lines, so this
# veto does not affect it.
#
# The double quote is in the veto for a case the directive/semicolon pair does not cover: a
# licence URL rewritten inside a string literal. Such a line carries neither a preprocessor
# directive nor a statement terminator, so without this it matches the licence patterns, the
# hunk classifies as noise, and a real source change disappears with nothing printed but a
# count. No occurrence exists in 2.10.3 -- every gpl-2.0.html/agpl-3.0.html sits in a block
# comment -- so this is a guard against a future upstream release, not a live defect. It costs
# nothing to carry: none of the 241 changed lines currently classified as licence noise
# contains a double quote, so the 450/47/403 split is unaffected.
$codeVetoRegex = '#\s*(define|include|if|ifdef|ifndef|else|elif|endif|undef|pragma)\b|;|"'

function Test-LicenseHunk {
    param($Hunk)
    # Only the add/del lines decide license-noise classification -- a retained context line
    # must never make a license hunk look like real code (it has no codeVetoRegex hit to give)
    # and must never veto a genuine license hunk either (it has no licenseRegex hit to give).
    $changeLines = @($Hunk.Lines | Where-Object { $_.Type -ne 'context' })
    # A hunk with no changed lines cannot be license noise, because there is nothing to
    # classify; falling through the loop would return $true and drop it.
    #
    # An earlier version of this comment called that unreachable, on the grounds that git does
    # not emit a change-line-free hunk from `diff --no-index --unified=3` on two text files.
    # The claim about git is true and the inference was wrong: what reaches here is the
    # *parser's* output, not git's. While Get-Hunks carried a `--`/`++` skip, a hunk whose
    # every changed line began with those characters arrived here empty and was dropped
    # silently. That skip is gone, so the route is closed at its source -- but the guard stays,
    # because the default belongs on the keeping side. In a tool whose whole purpose is to stop
    # losing lines quietly, an input nobody anticipated should surface to a human rather than
    # vanish into a counter.
    if ($changeLines.Count -eq 0) { return $false }
    foreach ($line in $changeLines) {
        if ($line.Text -match $codeVetoRegex) { return $false }
        if ($line.Text -notmatch $licenseRegex) { return $false }
    }
    return $true
}

function Get-Diff {
    param([string] $OldPath, [string] $NewPath)
    # `git diff --no-index` uses exit 1 for two different situations: "differences found"
    # (unified diff written to stdout) and "trouble reading one of the paths" (stdout is
    # empty, the message goes to stderr only). Exit 0 always means "no differences", with
    # empty stdout -- which is exactly the same *stdout* a path error produces. Silently
    # discarding stderr (as this used to do with `2>$null`) made those two cases
    # indistinguishable: Get-Hunks then treats a git failure exactly like "no changes",
    # which is precisely the failure mode this script exists to avoid (see the synopsis).
    # A real diff always has at least a hunk header once files differ, so exit 1 with
    # empty stdout can only be the error case, never a legitimate empty diff.
    $stderrCapture = [System.Collections.Generic.List[string]]::new()
    $diffLines = & git -C $repoRoot diff --no-index --no-color --unified=3 -- $OldPath $NewPath 2>&1 |
        ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { $stderrCapture.Add($_.ToString()) }
            else { $_ }
        }
    $exitCode = $LASTEXITCODE
    $diffText = ($diffLines -join "`n")

    if ($exitCode -gt 1 -or ($exitCode -eq 1 -and -not $diffText)) {
        $stderrText = ($stderrCapture -join "`n")
        throw "git diff --no-index failed comparing '$OldPath' and '$NewPath' (exit $exitCode): $stderrText"
    }

    return $diffText
}

function Invoke-FileDelta {
    param([string] $Name)

    $oldPath = Join-Path $baselineDir $Name
    $newPath = Join-Path $submoduleDir $Name

    $hasOld = Test-Path -LiteralPath $oldPath -PathType Leaf
    $hasNew = Test-Path -LiteralPath $newPath -PathType Leaf

    if (-not $hasNew) {
        return [pscustomobject]@{
            File = $Name; Status = 'pyswisseph-only (no 2.10.3 counterpart)'
            RawHunks = 0; FilteredHunks = 0; LicenseHunks = 0
            RawPlus = 0; RawMinus = 0; StrippedPlus = 0; StrippedMinus = 0
        }
    }
    if (-not $hasOld) {
        return [pscustomobject]@{
            File = $Name; Status = 'new in 2.10.3 (no 2.08 baseline)'
            RawHunks = 0; FilteredHunks = 0; LicenseHunks = 0
            RawPlus = 0; RawMinus = 0; StrippedPlus = 0; StrippedMinus = 0
        }
    }

    $tmp = [System.IO.Path]::GetTempPath()
    # Per-invocation suffix, not a fixed name. These land in the shared system temp
    # directory, and this repo routinely has dozens of git worktrees active at once. Two
    # concurrent runs asking about the same filename used to overwrite each other's inputs:
    # one interleaving crashes loudly, but the other returns a plausible, wrong diff at
    # exit 0 -- one worktree's question answered with another's content. Silently producing
    # a wrong work queue is the single failure this tool exists to prevent, so it must not
    # be reachable by running the tool twice.
    $runId = [System.Guid]::NewGuid().ToString('N').Substring(0, 12)
    $tmpOld = Join-Path $tmp "gen-delta-old-$runId-$Name"
    $tmpNew = Join-Path $tmp "gen-delta-new-$runId-$Name"
    Write-NormalizedTemp -Path $oldPath -TempPath $tmpOld
    Write-NormalizedTemp -Path $newPath -TempPath $tmpNew

    $diffText = Get-Diff -OldPath $tmpOld -NewPath $tmpNew
    $rawHunks = Get-Hunks -DiffText $diffText
    $licenseHunks = @($rawHunks | Where-Object { Test-LicenseHunk -Hunk $_ })
    $filteredHunks = @($rawHunks | Where-Object { -not (Test-LicenseHunk -Hunk $_) })

    $rawPlus = @($diffText -split "`n" | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }).Count
    $rawMinus = @($diffText -split "`n" | Where-Object { $_.StartsWith('-') -and -not $_.StartsWith('---') }).Count

    $strippedPlus = 0
    $strippedMinus = 0
    $isHeader = $Name.EndsWith('.h')
    if ($isHeader -and -not $NoCommentStrip) {
        $tmpOldStripped = Join-Path $tmp "gen-delta-old-stripped-$runId-$Name"
        $tmpNewStripped = Join-Path $tmp "gen-delta-new-stripped-$runId-$Name"
        Strip-CComments -Path $oldPath -TempPath $tmpOldStripped
        Strip-CComments -Path $newPath -TempPath $tmpNewStripped
        $strippedDiff = Get-Diff -OldPath $tmpOldStripped -NewPath $tmpNewStripped
        $strippedPlus = @($strippedDiff -split "`n" | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }).Count
        $strippedMinus = @($strippedDiff -split "`n" | Where-Object { $_.StartsWith('-') -and -not $_.StartsWith('---') }).Count
        Remove-Item -LiteralPath $tmpOldStripped, $tmpNewStripped -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $tmpOld, $tmpNew -ErrorAction SilentlyContinue

    [pscustomobject]@{
        File          = $Name
        Status        = 'ok'
        RawHunks      = $rawHunks.Count
        FilteredHunks = $filteredHunks.Count
        LicenseHunks  = $licenseHunks.Count
        RawPlus       = $rawPlus
        RawMinus      = $rawMinus
        StrippedPlus  = $strippedPlus
        StrippedMinus = $strippedMinus
        DiffText      = $diffText
        FilteredLines = $filteredHunks
        LicenseLines  = $licenseHunks
    }
}

# The '+' side of a hunk header ('@@ -a,b +c,d @@ ...') gives the line range in the 2.10.3 file --
# the side CONTRIBUTING.md's required citation (e.g. 'sweph.c:2310-2358') actually names. A count
# of 1 is written by git as '+c' with no ',d' at all, so that case defaults to a one-line range.
function Get-HunkNewRange {
    param([string] $Header)
    if ($Header -match '\+(\d+)(?:,(\d+))?') {
        $start = [int]$Matches[1]
        $count = if ($Matches[2]) { [int]$Matches[2] } else { 1 }
        if ($count -le 0) { return "$start" }
        return "$start-$($start + $count - 1)"
    }
    return $null
}

function Write-Hunk {
    param($Hunk, [string] $FileName)
    $range = Get-HunkNewRange -Header $Hunk.Header
    $citation = if ($range) { "$($FileName):$range" } else { '(no line range)' }
    Write-Output "# $citation -- $($Hunk.Header)"
    foreach ($entry in $Hunk.Lines) {
        $prefix = switch ($entry.Type) { 'add' { '+' } 'del' { '-' } default { ' ' } }
        Write-Output "$prefix$($entry.Text)"
    }
}

# --- Single-file mode: print the (filtered, unless -IncludeLicenseHunks) diff plus a summary ---

if ($File) {
    $result = Invoke-FileDelta -Name $File
    if ($result.Status -ne 'ok') {
        Write-Host "$($result.File): $($result.Status)"
        exit 0
    }

    if ($IncludeLicenseHunks) {
        Write-Output $result.DiffText
    }
    elseif ($ShowDroppedLicenseHunks) {
        # The dropped set itself, not the raw diff and not the kept hunks -- this is what makes
        # a dropped hunk auditable instead of just a count. If this is empty, the file's
        # license-noise count above is necessarily zero too.
        $i = 0
        foreach ($hunk in $result.LicenseLines) {
            $i++
            Write-Output "--- dropped (license-noise) hunk $i ---"
            Write-Hunk -Hunk $hunk -FileName $result.File
            Write-Output ''
        }
    }
    else {
        # Reconstruct a diff body containing the non-license hunks in full -- header, context and
        # changed lines -- so a hunk reads as a hunk instead of a bag of spliced +/- lines. This is
        # a reviewer-facing listing, not a byte-identical re-diff.
        $i = 0
        foreach ($hunk in $result.FilteredLines) {
            $i++
            Write-Output "--- hunk $i ---"
            Write-Hunk -Hunk $hunk -FileName $result.File
            Write-Output ''
        }
    }

    Write-Host ''
    Write-Host "# $($result.File): hunks raw=$($result.RawHunks) filtered=$($result.FilteredHunks) license-noise=$($result.LicenseHunks)"
    Write-Host "#   raw lines: +$($result.RawPlus) -$($result.RawMinus)"
    if ($File.EndsWith('.h') -and -not $NoCommentStrip) {
        Write-Host "#   comments-stripped lines: +$($result.StrippedPlus) -$($result.StrippedMinus)"
    }
    exit 0
}

# --- All-files mode: one summary row per file, plus totals -------------------------------------

$names = @(
    Get-ChildItem -LiteralPath $baselineDir -File | ForEach-Object { $_.Name }
    Get-ChildItem -LiteralPath $submoduleDir -File | ForEach-Object { $_.Name }
) | Sort-Object -Unique

$rows = foreach ($name in $names) { Invoke-FileDelta -Name $name }

$rows | Select-Object File, Status, RawHunks, FilteredHunks, LicenseHunks, RawPlus, RawMinus, StrippedPlus, StrippedMinus |
    Format-Table -AutoSize | Out-String -Width 240 | Write-Host

$ok = @($rows | Where-Object { $_.Status -eq 'ok' })
$totalRaw = ($ok | Measure-Object -Property RawHunks -Sum).Sum
$totalFiltered = ($ok | Measure-Object -Property FilteredHunks -Sum).Sum
$totalLicense = ($ok | Measure-Object -Property LicenseHunks -Sum).Sum
Write-Host "TOTAL over $($ok.Count) files with both a 2.08 and 2.10.3 side: raw hunks=$totalRaw filtered=$totalFiltered license-noise=$totalLicense"
exit 0
