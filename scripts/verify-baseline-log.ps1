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

    Counts numbered entries ("1. ", "2. ", ...) under the sidecar's "## Local
    regenerations" heading at -BaseRef and at HEAD; a TSV change without the count going
    up is a failure. The sidecar file is discovered by name pattern (baseline-*.env.txt)
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

function Get-LogEntryCount {
    param([string]$Content)
    # Scoped to the "## Local regenerations" section, not the whole file: the automatic
    # "^\d+\. " regex has no other anchor, so a numbered line appearing anywhere else in
    # the sidecar (a numbered list in a comment, a future section) would otherwise count
    # too. The heading is a location to slice from, not just a presence check.
    if ([string]::IsNullOrEmpty($Content)) {
        return 0
    }
    $idx = $Content.IndexOf('## Local regenerations')
    if ($idx -lt 0) {
        return 0
    }
    $section = $Content.Substring($idx)
    return ([regex]::Matches($section, '(?m)^\d+\. ')).Count
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

$baseCount = Get-LogEntryCount $baseContent
$headCount = Get-LogEntryCount $headContent

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

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $headSidecarRelPath's regenerations log gained $gained $plural ($baseCount -> $headCount)."
exit 0
