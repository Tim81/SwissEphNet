#Requires -Version 7
<#
.SYNOPSIS
    Fails if Tests/conformance/known-fail.tsv changed between -BaseRef and HEAD without
    Tests/conformance/regenerations.log gaining at least one new entry.

.DESCRIPTION
    scripts/regenerate-known-fail.ps1 is the only script that writes known-fail.tsv, and its
    default mode already requires -Reason before it will touch the file. But nothing before this
    script stopped a committed change to known-fail.tsv from landing without ever going through
    that script at all: a hand edit to the tracked TSV bypasses -Reason entirely, and this is the
    port's own work queue -- Tests/baseline/*.tsv and scripts/freeze-manifest.tsv both have this
    same failure mode and are closed by scripts/verify-baseline-log.ps1 and
    scripts/verify-freeze-log.ps1 respectively. This is the identical mechanism for known-fail.tsv,
    which had none of it.

    Tests/conformance/regenerations.log already exists (regenerate-known-fail.ps1 appends to it
    directly), but its entries are not numbered "1. ", "2. ", ... the way
    scripts/freeze-manifest-log.txt's and Tests/baseline/baseline-*.env.txt's own logs are. Each
    entry instead starts with a line beginning "YYYY-MM-DD " -- some entries are one line, others
    (see entries logged 2026-07-31, the Phase 6 probe and its correction) run to several paragraphs
    separated by blank lines, with no marker distinguishing "still the same entry" from "a new
    entry" other than the next YYYY-MM-DD-prefixed line. Get-LogEntries below anchors on that date
    prefix instead of a numbered-list marker, but requires the same two things as the numbered
    sidecars do, not one:

      1. Every entry present at -BaseRef must still be present, verbatim and in the same order, at
         HEAD -- the base entry list must be a prefix of the head entry list. A count-only
         comparison ("did the number go up") is satisfied by deleting an old entry and adding two
         new ones, which destroys history while reporting progress -- the exact bypass
         scripts/verify-freeze-log.ps1's own header documents being demonstrated against an
         earlier version of that script. Comparing entries, not just counting them, is what makes
         this log actually append-only rather than append-only in name; the log's own convention
         already does this by hand (2026-07-31's correction entry is a new entry noting an earlier
         one was wrong, not an edit to it in place), which this check now enforces mechanically.
      2. Every entry added since -BaseRef must have real content: a bare date with nothing readable
         after it satisfies a presence check but gives a reviewer nothing to read. See
         $MinNewEntrySubstanceChars below.

    Needs enough history to resolve -BaseRef (fetch-depth: 0, or an explicit fetch of the base
    commit) -- a shallow checkout will make this fail with a clear message rather than silently
    comparing against nothing.

.PARAMETER BaseRef
    Commit-ish to diff HEAD against: the PR's base SHA for pull_request events, or the previous
    commit for push events. Resolved by the caller (see .github/workflows/conformance.yml), not by
    this script, since only the workflow knows which GitHub event triggered it.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.
#>
param(
    [Parameter(Mandatory)]
    [string] $BaseRef,
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$manifestRelPath = 'Tests/conformance/known-fail.tsv'
$sidecarRelPath = 'Tests/conformance/regenerations.log'
$sidecarFullPath = Join-Path $RepoRoot 'Tests/conformance/regenerations.log'

git -C $RepoRoot rev-parse --verify "$BaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot resolve base ref '$BaseRef' as a commit. The workflow must check out enough history (fetch-depth: 0, or an explicit fetch of the base commit) for this check to diff against it."
    exit 1
}

$changed = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- $manifestRelPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "git diff between '$BaseRef' and HEAD failed."
    exit 1
}

if (-not $changed) {
    Write-Host "No $manifestRelPath changes between $BaseRef and HEAD. Nothing to check."
    exit 0
}

Write-Host "$manifestRelPath changed between $BaseRef and HEAD."
Write-Host ""

# Below this many letters/digits (the date prefix and punctuation stripped), a new entry is
# rejected as having no real content -- see the header comment's point 2. Chosen well under every
# genuine entry in Tests/conformance/regenerations.log today (the shortest single-line "Pruned N
# newly-passing row(s)" entries still read in the dozens of characters) and well over what a
# placeholder like a bare date, or a date plus "." , can reach.
$MinNewEntrySubstanceChars = 20

# Entries are anchored on a "YYYY-MM-DD " prefix at the start of a line, not a numbered-list
# marker -- see this file's own header comment for why the sidecar's actual format needs a
# different anchor than scripts/verify-freeze-log.ps1's "^\d+\. ". Everything up to (but not
# including) the next such line -- blank lines and continuation paragraphs included -- belongs to
# the entry that opened it, which is what lets a multi-paragraph entry (Tests/conformance/
# regenerations.log's own 2026-07-31 Phase 6 probe entry, several paragraphs separated by blank
# lines) stay one entry instead of fragmenting at every blank line.
function Get-LogEntries {
    param([string] $Content)
    if ([string]::IsNullOrEmpty($Content)) {
        return @()
    }
    $starts = [regex]::Matches($Content, '(?m)^\d{4}-\d{2}-\d{2}\s')
    $entries = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $starts.Count; $i++) {
        $entryStart = $starts[$i].Index
        $entryEnd = if ($i + 1 -lt $starts.Count) { $starts[$i + 1].Index } else { $Content.Length }
        $entries.Add($Content.Substring($entryStart, $entryEnd - $entryStart).TrimEnd())
    }
    return $entries.ToArray()
}

# True when $Entry (one element of Get-LogEntries' return, date prefix still attached) has enough
# actual content to be worth a reviewer's time -- see $MinNewEntrySubstanceChars above.
function Test-EntryHasSubstance {
    param([string] $Entry)
    $body = $Entry -replace '^\d{4}-\d{2}-\d{2}\s*', ''
    $meaningfulChars = ($body -replace '[^\p{L}\p{N}]', '')
    return $meaningfulChars.Length -ge $MinNewEntrySubstanceChars
}

if (-not (Test-Path -LiteralPath $sidecarFullPath -PathType Leaf)) {
    Write-Error "$sidecarRelPath not found at HEAD. Cannot verify the regenerations log without it."
    exit 1
}
# Normalized to LF, not read as-is: unlike Tests/baseline/*.env.txt and scripts/freeze-manifest.tsv,
# this sidecar has no `eol=lf` pin in .gitattributes, so it checks out as CRLF wherever
# core.autocrlf normalizes on checkout (a Windows clone, or a Windows CI runner) while `git show`
# below always returns the blob's own stored content (LF -- text=auto normalizes CRLF to LF on
# commit). Comparing an as-checked-out $headContent against an always-LF $baseContent would then
# report every multi-line entry as "edited" purely from a line-ending difference that has nothing
# to do with this log's actual content -- confirmed directly: entry text that reads identically in
# Write-Host still failed string equality until this normalization was added. Both sides are
# normalized the same way so the comparison is content, not encoding.
$headContent = (Get-Content -Raw -LiteralPath $sidecarFullPath) -replace "`r`n", "`n" -replace "`r", "`n"

# Resolved separately at $BaseRef via `git show`, not assumed to exist there: the sidecar itself
# might not have existed yet at $BaseRef, in which case 0 prior entries is correct, not a
# resolution failure.
git -C $RepoRoot cat-file -e "${BaseRef}:${sidecarRelPath}" 2>$null
$baseExists = ($LASTEXITCODE -eq 0)
if (-not $baseExists) {
    $baseContent = ''
}
else {
    # PowerShell captures external-command output as a string array (one element per line);
    # passing that straight into a [string] parameter coerces it via $OFS space-joining, which
    # discards every real line break -- the (?m)^ anchor in Get-LogEntries would then only ever
    # match at the very start of the whole blob. -join "`n" restores real newlines first, already
    # LF-only (see $headContent's own comment above for why that side is normalized too).
    $baseContentLines = git -C $RepoRoot show "${BaseRef}:${sidecarRelPath}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "'$sidecarRelPath' exists at $BaseRef per git cat-file, but 'git show' could not read it. This should not happen; investigate before trusting this check's result."
        exit 1
    }
    $baseContent = ($baseContentLines -join "`n") -replace "`r`n", "`n" -replace "`r", "`n"
}

# @() wraps deliberately: PowerShell unrolls a single-element array on return into a bare scalar,
# which would silently turn $baseEntries[0] on a one-entry log into that entry's first CHARACTER
# instead of the entry text -- see scripts/verify-freeze-log.ps1's identical comment for the same
# hazard.
$baseEntries = @(Get-LogEntries $baseContent)
$headEntries = @(Get-LogEntries $headContent)
$baseCount = $baseEntries.Count
$headCount = $headEntries.Count

if ($headCount -le $baseCount) {
    Write-Error @"
$manifestRelPath changed between $BaseRef and HEAD, but $sidecarRelPath did not gain a new entry
($baseCount -> $headCount).

known-fail.tsv is the port's own work queue: every committed change to it needs a record a
reviewer can read without re-deriving it from the diff. If this was a deliberate regeneration, run
scripts/regenerate-known-fail.ps1 (-Reason '...' in default mode; -PruneOnly needs none) -- it
appends the required entry automatically. If known-fail.tsv was hand-edited instead of regenerated,
that is very likely the bypass this gate exists to catch: revert the hand edit and go through
scripts/regenerate-known-fail.ps1 instead, per CONTRIBUTING.md's "Correctness oracle known-fail
list".
"@
    exit 1
}

# Append-only check: every entry that existed at -BaseRef must still read identically, in the same
# position, at HEAD. A count that only goes up is not enough on its own -- see this file's own
# header comment for the demonstrated bypass (delete one entry, add two, count still rises).
for ($i = 0; $i -lt $baseCount; $i++) {
    if ($headEntries[$i] -ne $baseEntries[$i]) {
        Write-Error @"
$sidecarRelPath is append-only, but entry #$($i + 1) differs between $BaseRef and HEAD -- it was
edited, reordered or removed rather than left alone.

Base entry #$($i + 1):
$($baseEntries[$i])

HEAD entry #$($i + 1):
$($headEntries[$i])

If an old entry was wrong, add a NEW entry noting the correction instead of rewriting history in
place -- an append-only log that gets edited is no longer append-only, and this sidecar's own log
already documents exactly this convention (the 2026-07-31 entry correcting the Phase 6 probe
entry above it, left in place, rather than silently rewritten).
"@
        exit 1
    }
}

# Substance check: every entry added since -BaseRef must have real content, not just a bare date --
# see this file's own header comment's point 2 and Test-EntryHasSubstance above.
for ($i = $baseCount; $i -lt $headCount; $i++) {
    if (-not (Test-EntryHasSubstance $headEntries[$i])) {
        Write-Error @"
$sidecarRelPath gained entry #$($i + 1), but it has no real content for a reviewer to read (fewer
than $MinNewEntrySubstanceChars letters/digits once the date prefix is stripped):

$($headEntries[$i])

Describe what changed and why, the same way every existing entry in this log does.
"@
        exit 1
    }
}

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $sidecarRelPath gained $gained $plural ($baseCount -> $headCount), every prior entry unchanged, every new entry has real content."
exit 0
