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
    up is a failure. The sidecar file itself is discovered by name pattern
    (baseline-*.env.txt) rather than hardcoded, matching how
    Tools/BaselineMatrix/EnvInfo.cs derives it from ReferenceVersion.

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
$sidecarName = $sidecars[0].Name
$sidecarRelPath = "Tests/baseline/$sidecarName"

function Get-LogEntryCount {
    param([string]$Content)
    if ([string]::IsNullOrEmpty($Content) -or $Content -notmatch '## Local regenerations') {
        return 0
    }
    return ([regex]::Matches($Content, '(?m)^\d+\. ')).Count
}

# The sidecar's *name* is derived from EnvInfo.ReferenceVersion and changes on a
# reference-mode regeneration that bumps the reference package version -- in that case it
# will not exist under the same name at $BaseRef. git show returning nothing (rather than
# throwing) is treated as "0 prior entries" rather than a hard failure, since there is
# nothing to compare the log against; the count-increased check below still applies against
# that baseline of zero.
$baseContent = git -C $repoRoot show "${BaseRef}:${sidecarRelPath}" 2>$null
if ($LASTEXITCODE -ne 0) {
    $baseContent = ''
}
$headContent = Get-Content -Raw -Path $sidecars[0].FullName

$baseCount = Get-LogEntryCount $baseContent
$headCount = Get-LogEntryCount $headContent

if ($headCount -le $baseCount) {
    Write-Error @"
Tests/baseline/*.tsv changed ($($changedTsv.Count) file(s), listed above) between $BaseRef
and HEAD, but $sidecarRelPath's '## Local regenerations' log did not gain a new entry
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
    exit 1
}

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $sidecarRelPath's regenerations log gained $gained $plural ($baseCount -> $headCount)."
exit 0
