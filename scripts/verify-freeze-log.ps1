#Requires -Version 7
<#
.SYNOPSIS
    Fails if scripts/freeze-manifest.tsv changed between -BaseRef and HEAD without
    scripts/freeze-manifest-log.txt's "## Manifest updates" log gaining at least one new entry.

.DESCRIPTION
    scripts/verify-freeze.ps1 -Update rewrites scripts/freeze-manifest.tsv from whatever is
    currently on disk. Nothing about that command requires a reason, and nothing before this
    script existed stopped a red freeze gate from being turned green by running -Update and
    committing the result, with no record of why the frozen files' fingerprint actually moved.
    Tests/baseline/*.tsv has exactly this same failure mode and is closed by
    Tests/baseline/baseline-*.env.txt's own "## Local regenerations" log
    (scripts/verify-baseline-log.ps1); this script and scripts/freeze-manifest-log.txt are the
    identical mechanism for the freeze manifest, which had none of it.

    This does not care whether the manifest change was legitimate (a real fidelity fix or
    re-transliteration, which does move the frozen paths' fingerprint, exactly as
    scripts/verify-freeze.ps1's own header describes) or not -- only that a PR/push touching the
    committed manifest is accompanied by a corresponding log entry, so a reviewer always has
    something to read that explains why the fingerprint moved, the same standard
    verify-baseline-log.ps1 already holds Tests/baseline/*.tsv to.

    Extracts numbered entries ("1. ", "2. ", ...) under the sidecar's "## Manifest updates"
    heading at -BaseRef and at HEAD, and requires two things, not one:

      1. Every entry present at -BaseRef must still be present, verbatim and in the same order,
         at HEAD -- the base entry list must be a prefix of the head entry list. A count-only
         comparison ("did the number go up") is satisfied by deleting an old entry and adding two
         new ones, which destroys history while reporting progress; this was demonstrated against
         an earlier version of this script (a 3-entry log rewritten to drop entry 2 and add two
         replacements printed "gained 1 entry (3 -> 4)" and exited 0). Comparing entries, not just
         counting them, is what makes the log actually append-only rather than append-only in name.
      2. Every entry added since -BaseRef must have real content: a bare "." or a numbered line
         with nothing readable after it satisfies a presence check but gives a reviewer nothing to
         read, which is the entire point of requiring an entry at all. See
         $MinNewEntrySubstanceChars below for the exact bar and why a small fixed floor was chosen
         over no floor at all.

    Unlike Tests/baseline/baseline-*.env.txt, the sidecar here has one fixed name and is not
    renamed by anything, so it is read by path directly at each ref rather than discovered by a
    glob pattern.

    Needs enough history to resolve -BaseRef (fetch-depth: 0, or an explicit fetch of the base
    commit) -- a shallow checkout will make this fail with a clear message rather than silently
    comparing against nothing.

.PARAMETER BaseRef
    Commit-ish to diff HEAD against: the PR's base SHA for pull_request events, or the previous
    commit for push events. Resolved by the caller (see .github/workflows/ci.yml), not by this
    script, since only the workflow knows which GitHub event triggered it.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.
#>
param(
    [Parameter(Mandatory)]
    [string] $BaseRef,
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$manifestRelPath = 'scripts/freeze-manifest.tsv'
$sidecarRelPath = 'scripts/freeze-manifest-log.txt'
$sidecarFullPath = Join-Path $RepoRoot 'scripts/freeze-manifest-log.txt'

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

# Below this many letters/digits (numbering and punctuation stripped), a new entry is rejected as
# having no real content -- see the header comment's point 2. Chosen well under every genuine
# entry in scripts/freeze-manifest-log.txt today (the shortest reads in the hundreds of
# characters) and well over what a placeholder like "." or "4. " or "TODO" can reach, so this
# floor rejects the vacuous case demonstrated in review without being able to reject a real entry
# by accident.
$MinNewEntrySubstanceChars = 20

# Scoped to the "## Manifest updates" section, not the whole file -- a numbered line appearing
# anywhere else (this file's own header prose, a future section) should not count. Returns the
# entries themselves, each trimmed of trailing whitespace, in file order -- not just a count --
# so the caller can both compare entry text (append-only prefix check) and inspect each new
# entry's content (substance check), neither of which a bare count can do.
function Get-LogEntries {
    param([string] $Content)
    if ([string]::IsNullOrEmpty($Content)) {
        return @()
    }
    $idx = $Content.IndexOf('## Manifest updates')
    if ($idx -lt 0) {
        return @()
    }
    $section = $Content.Substring($idx)
    $starts = [regex]::Matches($section, '(?m)^\d+\. ')
    $entries = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $starts.Count; $i++) {
        $entryStart = $starts[$i].Index
        $entryEnd = if ($i + 1 -lt $starts.Count) { $starts[$i + 1].Index } else { $section.Length }
        $entries.Add($section.Substring($entryStart, $entryEnd - $entryStart).TrimEnd())
    }
    return $entries.ToArray()
}

# True when $Entry (one element of Get-LogEntries' return, numbering still attached) has enough
# actual content to be worth a reviewer's time -- see $MinNewEntrySubstanceChars above.
function Test-EntryHasSubstance {
    param([string] $Entry)
    $body = $Entry -replace '^\d+\.\s*', ''
    $meaningfulChars = ($body -replace '[^\p{L}\p{N}]', '')
    return $meaningfulChars.Length -ge $MinNewEntrySubstanceChars
}

if (-not (Test-Path -LiteralPath $sidecarFullPath -PathType Leaf)) {
    Write-Error "$sidecarRelPath not found at HEAD. Cannot verify the manifest-updates log without it."
    exit 1
}
# Normalized to LF, not read as-is: scripts/freeze-manifest-log.txt has no `eol=lf` pin in
# .gitattributes, so a working-tree checkout that converts LF to CRLF (a Windows clone or a
# Windows CI runner; `core.autocrlf=true` reproduces this on any fresh `git worktree add`, not
# only the original clone) disagrees with `git show`'s always-LF blob content below. Without this,
# every multi-line entry in this sidecar's numbered list reports as edited between $BaseRef and
# HEAD purely from a line-ending difference that has nothing to do with its actual text --
# confirmed directly: entry text that read identically in Write-Host still failed string equality
# on a fresh Windows worktree checkout until this normalization was added. Both sides are
# normalized the same way so the comparison is content, not encoding.
$headContent = (Get-Content -Raw -LiteralPath $sidecarFullPath) -replace "`r`n", "`n" -replace "`r", "`n"

# Resolved separately at $BaseRef via `git show`, not assumed to exist there: the sidecar itself
# might not have existed yet at $BaseRef (the commit that first added this log), in which case 0
# prior entries is correct, not a resolution failure.
git -C $RepoRoot cat-file -e "${BaseRef}:${sidecarRelPath}" 2>$null
$baseExists = ($LASTEXITCODE -eq 0)
if (-not $baseExists) {
    $baseContent = ''
}
else {
    # PowerShell captures external-command output as a string array (one element per line);
    # passing that straight into a [string] parameter coerces it via $OFS space-joining, which
    # discards every real line break -- the (?m)^ anchors in Get-LogEntryCount would then only
    # ever match at the very start of the whole blob. -join "`n" restores real newlines first,
    # already LF-only (see $headContent's own comment above for why that side is normalized too).
    $baseContentLines = git -C $RepoRoot show "${BaseRef}:${sidecarRelPath}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "'$sidecarRelPath' exists at $BaseRef per git cat-file, but 'git show' could not read it. This should not happen; investigate before trusting this check's result."
        exit 1
    }
    $baseContent = ($baseContentLines -join "`n") -replace "`r`n", "`n" -replace "`r", "`n"
}

# @() wraps deliberately: PowerShell unrolls a single-element array on return into a bare scalar,
# which would silently turn $baseEntries[0] on a one-entry log into that entry's first CHARACTER
# ($baseEntries.Count would then read the string's own .Count, which PowerShell's adapter reports
# as 1, hiding the problem completely) instead of the entry text. @() forces array shape
# regardless of how many entries Get-LogEntries found, matching this repository's own convention
# for the same hazard (see e.g. regenerate-baseline.ps1's @($ExpectedScope | ...) and
# @($existingSidecars)).
$baseEntries = @(Get-LogEntries $baseContent)
$headEntries = @(Get-LogEntries $headContent)
$baseCount = $baseEntries.Count
$headCount = $headEntries.Count

if ($headCount -le $baseCount) {
    Write-Error @"
$manifestRelPath changed between $BaseRef and HEAD, but $sidecarRelPath's '## Manifest updates'
log did not gain a new entry ($baseCount -> $headCount).

Every committed change to the transliteration-freeze manifest needs a record a reviewer can read
without re-deriving it. If this was a deliberate, reviewed change to a frozen file -- a fidelity
fix citing the C, or a re-transliteration -- add an entry to $sidecarRelPath's '## Manifest
updates' log describing what changed and citing the C, then rerun
'pwsh scripts/verify-freeze.ps1 -Update' and commit the manifest together with both. If nothing
in SwissEphNet/CPort, Programs/SweTest/Program.cs or Programs/SweMini/Program.cs was meant to
change, this manifest update is very likely an unexcluded reformat -- see CONTRIBUTING.md's three
--exclude flags -- and should be reverted, not logged.
"@
    exit 1
}

# Append-only check: every entry that existed at -BaseRef must still read identically, in the
# same position, at HEAD. A count that only goes up is not enough on its own -- see the header
# comment's point 1 for the demonstrated bypass (delete one entry, add two, count still rises).
for ($i = 0; $i -lt $baseCount; $i++) {
    if ($headEntries[$i] -ne $baseEntries[$i]) {
        Write-Error @"
$sidecarRelPath's '## Manifest updates' log is append-only, but entry #$($i + 1) differs between
$BaseRef and HEAD -- it was edited, reordered or removed rather than left alone.

Base entry #$($i + 1):
$($baseEntries[$i])

HEAD entry #$($i + 1):
$($headEntries[$i])

If an old entry was wrong, add a NEW entry noting the correction instead of rewriting history in
place -- an append-only log that gets edited is no longer append-only, and Tests/baseline/
baseline-2.8.0.2.env.txt's own '## Local regenerations' log documents exactly this convention for
entries 2 to 4 (left as originally written, with the misattribution noted afterward, rather than
silently corrected).
"@
        exit 1
    }
}

# Substance check: every entry added since -BaseRef must have real content, not just a numbered
# line -- see the header comment's point 2 and `Test-EntryHasSubstance` above.
for ($i = $baseCount; $i -lt $headCount; $i++) {
    if (-not (Test-EntryHasSubstance $headEntries[$i])) {
        Write-Error @"
$sidecarRelPath's '## Manifest updates' log gained entry #$($i + 1), but it has no real content
for a reviewer to read (fewer than $MinNewEntrySubstanceChars letters/digits once the numbering
is stripped):

$($headEntries[$i])

Describe what changed and cite the C, the same way every existing entry in this log does.
"@
        exit 1
    }
}

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $sidecarRelPath's manifest-updates log gained $gained $plural ($baseCount -> $headCount), every prior entry unchanged, every new entry has real content."
exit 0
