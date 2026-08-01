#Requires -Version 7
<#
.SYNOPSIS
    Fails if any Tests/baseline/*.tsv changed between -BaseRef and HEAD without the
    sidecar's "## Local regenerations" log gaining at least one new entry.

.DESCRIPTION
    scripts/regenerate-baseline.ps1 only checks that -DeviationNote is non-empty at the
    point of regeneration; nothing stops a committed change to Tests/baseline/*.tsv from
    landing without ever going through that script at all, or with the resulting sidecar
    edit dropped before commit. This is the CI-side half of that guard: it does not care
    how the TSV change was produced, only that a PR/push touching the committed baseline
    is accompanied by a corresponding entry in the sidecar's append-only log, so a
    reviewer always has something to read that explains the change.

    Extracts numbered entries ("1. ", "2. ", ...) under the sidecar's "## Local
    regenerations" heading at -BaseRef and at HEAD, and requires two things, not one: every
    entry present at -BaseRef must still read identically, in the same order, at HEAD (the
    base entry list must be a prefix of the head entry list -- a count-only comparison is
    satisfied by deleting an old entry and adding two new ones, which destroys history while
    reporting progress), and every entry added since -BaseRef must have real content, not
    just a numbered line with nothing readable after it (see $MinNewEntrySubstanceChars
    below). A TSV change without the count going up at all is still a failure, as before.
    The sidecar file is discovered by name pattern (baseline-*.env.txt)
    SEPARATELY at each ref (git ls-tree at -BaseRef, Get-ChildItem at HEAD), never by
    reusing HEAD's filename to look up -BaseRef's content: EnvInfo.SidecarFileName derives the name from
    ReferenceVersion, so a reference-mode regeneration that bumps the version renames the
    file, and looking up the *old* ref by the *new* name always finds nothing --
    previously that silently became "0 prior entries" instead of "resolve the file that
    was actually there", which let regenerate-baseline.ps1's practice of preserving the
    old sidecar's log across a version bump alone satisfy "the count went up", with no
    connection to whether the log actually gained an entry describing this diff.

    Needs enough history to resolve -BaseRef (fetch-depth: 0, or an explicit fetch of the
    base commit) -- a shallow checkout will make this fail with a clear message rather
    than silently comparing against nothing.

.PARAMETER BaseRef
    Commit-ish to diff HEAD against: the PR's base SHA for pull_request events, or the
    previous commit for push events. Resolved by the caller (see .github/workflows/baseline.yml),
    not by this script, since only the workflow knows which GitHub event triggered it.
#>

param(
    [Parameter(Mandatory)]
    [string]$BaseRef
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$baselineDir = Join-Path $repoRoot 'Tests\baseline'

git -C $repoRoot rev-parse --verify "$BaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot resolve base ref '$BaseRef' as a commit. The workflow must check out enough history (fetch-depth: 0, or an explicit fetch of the base commit) for this check to diff against it."
    exit 1
}

$changedTsv = git -C $repoRoot diff --name-only "$BaseRef" HEAD -- 'Tests/baseline/*.tsv'
if ($LASTEXITCODE -ne 0) {
    Write-Error "git diff between '$BaseRef' and HEAD failed."
    exit 1
}

if (-not $changedTsv) {
    Write-Host "No Tests/baseline/*.tsv changes between $BaseRef and HEAD. Nothing to check."
    exit 0
}

Write-Host "Tests/baseline/*.tsv changed between $BaseRef and HEAD:"
$changedTsv | ForEach-Object { Write-Host "  $_" }
Write-Host ""

$sidecars = @(Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue)
if ($sidecars.Count -ne 1) {
    Write-Error "Expected exactly one Tests/baseline/baseline-*.env.txt sidecar at HEAD (found $($sidecars.Count)). Cannot verify the regenerations log without it."
    exit 1
}
$headContent = Get-Content -Raw -Path $sidecars[0].FullName
$headSidecarRelPath = "Tests/baseline/$($sidecars[0].Name)"

# Below this many letters/digits (numbering and punctuation stripped), a new entry is rejected as
# having no real content -- see this file's own header comment. Chosen well under every genuine
# entry in Tests/baseline/baseline-*.env.txt today (the shortest reads in the hundreds of
# characters) and well over what a placeholder like "." or "4. " or "TODO" can reach, so this
# floor rejects the vacuous case demonstrated in review without being able to reject a real entry
# by accident.
$MinNewEntrySubstanceChars = 20

# Scoped to the "## Local regenerations" section, not the whole file: the automatic "^\d+\. "
# regex has no other anchor, so a numbered line appearing anywhere else in the sidecar (a
# numbered list in a comment, a future section) would otherwise count too. The heading is a
# location to slice from, not just a presence check. Returns the entries themselves, each
# trimmed of trailing whitespace, in file order -- not just a count -- so the caller can both
# compare entry text (append-only prefix check) and inspect each new entry's content (substance
# check), neither of which a bare count can do.
function Get-LogEntries {
    param([string]$Content)
    if ([string]::IsNullOrEmpty($Content)) {
        return @()
    }
    $idx = $Content.IndexOf('## Local regenerations')
    if ($idx -lt 0) {
        return @()
    }
    $section = $Content.Substring($idx)
    $starts = [regex]::Matches($section, '(?m)^\d+\. ')
    $entries = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $starts.Count; $i++) {
        $entryStart = $starts[$i].Index
        $entryEnd = if ($i + 1 -lt $starts.Count) { $starts[$i + 1].Index } else { $section.Length }
        $entryText = $section.Substring($entryStart, $entryEnd - $entryStart)
        # An entry's body also ends at a "## " subsection heading, not only at the next numbered
        # line. Without this, any section sitting between two entries is swallowed into the
        # earlier one's body: this sidecar's own "## pyswisseph 2.10.03 validation coverage"
        # sits between entries 6 and 7 and carries no numbered line, so 271 lines of
        # re-measurable coverage figures parsed as entry 6's tail. Correcting a figure there
        # then read as rewriting an append-only log entry, and the gate failed on a file whose
        # log was untouched. Bounding the entry rather than the section keeps entries 7 onward,
        # which sit after that heading and are genuinely part of the log.
        $subHeading = [regex]::Match($entryText, '(?m)^## ')
        if ($subHeading.Success) {
            $entryText = $entryText.Substring(0, $subHeading.Index)
        }
        $entries.Add($entryText.TrimEnd())
    }
    return $entries.ToArray()
}

# True when $Entry (one element of Get-LogEntries' return, numbering still attached) has enough
# actual content to be worth a reviewer's time -- see $MinNewEntrySubstanceChars above.
function Test-EntryHasSubstance {
    param([string]$Entry)
    $body = $Entry -replace '^\d+\.\s*', ''
    $meaningfulChars = ($body -replace '[^\p{L}\p{N}]', '')
    return $meaningfulChars.Length -ge $MinNewEntrySubstanceChars
}

# Resolve the sidecar SEPARATELY at $BaseRef, by pattern, rather than reusing HEAD's
# filename: EnvInfo.SidecarFileName is derived from ReferenceVersion, so a reference-mode
# regeneration that bumps the version renames the file -- git show '<BaseRef>:<HEAD's
# name>' then finds nothing at a path that never existed at that ref, which used to be
# silently treated the same as "the sidecar legitimately did not exist yet" (0 prior
# entries). Those are different situations: one means "nothing to compare against, fine",
# the other means "compare against the wrong (nonexistent) path and get zero by accident".
# git ls-tree lists whatever baseline-*.env.txt path(s) actually existed at $BaseRef, so a
# rename is followed instead of missed.
$baseSidecarPaths = @(git -C $repoRoot ls-tree -r --name-only $BaseRef -- 'Tests/baseline' 2>$null |
    Where-Object { $_ -match '^Tests/baseline/baseline-.*\.env\.txt$' })

if ($baseSidecarPaths.Count -eq 0) {
    # Genuinely nothing to compare against (the sidecar did not exist yet at $BaseRef,
    # e.g. the commit that first introduced Tests/baseline/). 0 prior entries is correct
    # here, not a resolution failure.
    $baseContent = ''
}
else {
    if ($baseSidecarPaths.Count -gt 1) {
        Write-Warning "Multiple baseline-*.env.txt sidecars found at ${BaseRef}: $($baseSidecarPaths -join ', '). Using the first; this should not normally happen (BaselineVerify itself fails on more than one at HEAD)."
    }
    $baseSidecarRelPath = $baseSidecarPaths[0]

    # PowerShell captures external-command output as a string array (one element per
    # line), and passing that straight to Get-LogEntryCount's [string] parameter
    # coerces it via $OFS space-joining, which silently discards every line break --
    # the (?m)^ anchors would then only ever match at the very start of the whole blob.
    # -join "`n" restores real newlines before the regex ever sees it.
    $baseContentLines = git -C $repoRoot show "${BaseRef}:${baseSidecarRelPath}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Resolved sidecar path '$baseSidecarRelPath' at $BaseRef via git ls-tree, but 'git show' could not read it. This should not happen; investigate before trusting this check's result."
        exit 1
    }
    $baseContent = $baseContentLines -join "`n"
}

# @() wraps deliberately: PowerShell unrolls a single-element array on return into a bare scalar,
# which would silently turn $baseEntries[0] on a one-entry log into that entry's first CHARACTER
# ($baseEntries.Count would then read the string's own .Count, which PowerShell's adapter reports
# as 1, hiding the problem completely) instead of the entry text. @() forces array shape
# regardless of how many entries Get-LogEntries found, matching this repository's own convention
# for the same hazard (see e.g. this script's own @($sidecars) above and
# regenerate-baseline.ps1's @($ExpectedScope | ...) / @($existingSidecars)).
$baseEntries = @(Get-LogEntries $baseContent)
$headEntries = @(Get-LogEntries $headContent)
$baseCount = $baseEntries.Count
$headCount = $headEntries.Count

if ($headCount -le $baseCount) {
    # Tests/baseline/*.tsv sweeps up three different kinds of file: the golden
    # baseline-<area>.tsv files regenerate-baseline.ps1 writes, plus waivers.tsv and
    # row-counts.tsv, which land in the same glob deliberately (see
    # Tools/BaselineGen/README.md, "Why waivers.tsv lives at Tests/baseline/, not
    # Tools/BaselineVerify/") but are never written by that script. Recommending
    # `regenerate-baseline.ps1 -FromLocal` unconditionally is actively dangerous for a
    # waivers.tsv-only or row-counts.tsv-only change -- that flag rewrites all 19 golden
    # files from local code, which is exactly the undeliberate, unreviewed rebaseline this
    # gate exists to prevent, and it is precisely the wrong tool for a PR whose only job is
    # deleting a stale waiver (which the gate itself, via BaselineVerify's stale-waiver
    # check, can require).
    $changedGoldenTsv = @($changedTsv | Where-Object { (Split-Path $_ -Leaf) -like 'baseline-*.tsv' })

    if ($changedGoldenTsv.Count -gt 0) {
        Write-Error @"
Tests/baseline/*.tsv changed ($($changedTsv.Count) file(s), listed above, including $($changedGoldenTsv.Count) golden baseline-*.tsv file(s)) between $BaseRef
and HEAD, but $headSidecarRelPath's '## Local regenerations' log did not gain a new entry
($baseCount -> $headCount).

Every committed change to the baseline needs a record a reviewer can read without
re-deriving it. If this was a deliberate, reviewed local-mode regeneration, run
scripts/regenerate-baseline.ps1 -FromLocal -DeviationNote '...' -- it appends the required
entry automatically. If this is a reference-mode regeneration (a new SwissEphNet package
version), add an entry to the same log by hand describing the version bump: that mode does
not append one on its own, since it does not run under -FromLocal, but reviewers still need
to see why every number in the committed baseline changed at once (see
Tools/BaselineGen/README.md, 'Local mode -- when it is legitimate', for why this is a hard
gate rather than a review checklist item).
"@
    }
    else {
        Write-Error @"
Tests/baseline/*.tsv changed ($($changedTsv.Count) file(s), listed above -- waivers.tsv and/or
row-counts.tsv only, no golden baseline-*.tsv file) between $BaseRef and HEAD, but
$headSidecarRelPath's '## Local regenerations' log did not gain a new entry ($baseCount -> $headCount).

Do NOT run scripts/regenerate-baseline.ps1 -FromLocal for this -- nothing wrote a golden
baseline-*.tsv file, so that would rewrite all of them from local code for no reason, which is
exactly the undeliberate rebaseline this gate exists to prevent (and is very likely to move
numbers this change has nothing to do with). Add an entry to the sidecar's '## Local
regenerations' log by hand instead, describing which waiver or row-count entry changed and why
(e.g. a stale waiver being deleted, as BaselineVerify's own stale-waiver check can require, or a
waiver being added or narrowed) -- see Tools/BaselineGen/README.md, 'Why waivers.tsv lives at
Tests/baseline/, not Tools/BaselineVerify/'.
"@
    }
    exit 1
}

# Append-only check: every entry that existed at -BaseRef must still read identically, in the
# same position, at HEAD. A count that only goes up is not enough on its own -- see this file's
# own header comment for the demonstrated bypass (delete one entry, add two, count still rises).
for ($i = 0; $i -lt $baseCount; $i++) {
    if ($headEntries[$i] -ne $baseEntries[$i]) {
        Write-Error @"
$headSidecarRelPath's '## Local regenerations' log is append-only, but entry #$($i + 1) differs
between $BaseRef and HEAD -- it was edited, reordered or removed rather than left alone.

Base entry #$($i + 1):
$($baseEntries[$i])

HEAD entry #$($i + 1):
$($headEntries[$i])

If an old entry was wrong, add a NEW entry noting the correction instead of rewriting history in
place -- an append-only log that gets edited is no longer append-only, and this sidecar's own log
documents exactly this convention for entries 2 to 4 (left as originally written, with the
misattribution noted afterward, rather than silently corrected).
"@
        exit 1
    }
}

# Substance check: every entry added since -BaseRef must have real content, not just a numbered
# line -- see this file's own header comment and `Test-EntryHasSubstance` above.
for ($i = $baseCount; $i -lt $headCount; $i++) {
    if (-not (Test-EntryHasSubstance $headEntries[$i])) {
        Write-Error @"
$headSidecarRelPath's '## Local regenerations' log gained entry #$($i + 1), but it has no real
content for a reviewer to read (fewer than $MinNewEntrySubstanceChars letters/digits once the
numbering is stripped):

$($headEntries[$i])

Describe what changed and why, the same way every existing entry in this log does.
"@
        exit 1
    }
}

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $headSidecarRelPath's regenerations log gained $gained $plural ($baseCount -> $headCount), every prior entry unchanged, every new entry has real content."
exit 0
