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

    Counts numbered entries ("1. ", "2. ", ...) under the sidecar's "## Manifest updates" heading
    at -BaseRef and at HEAD; a manifest change without the count going up is a failure. Unlike
    Tests/baseline/baseline-*.env.txt, the sidecar here has one fixed name and is not renamed by
    anything, so it is read by path directly at each ref rather than discovered by a glob pattern.

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

function Get-LogEntryCount {
    # Scoped to the "## Manifest updates" section, not the whole file -- a numbered line
    # appearing anywhere else (this file's own header prose, a future section) should not count.
    param([string] $Content)
    if ([string]::IsNullOrEmpty($Content)) {
        return 0
    }
    $idx = $Content.IndexOf('## Manifest updates')
    if ($idx -lt 0) {
        return 0
    }
    $section = $Content.Substring($idx)
    return ([regex]::Matches($section, '(?m)^\d+\. ')).Count
}

if (-not (Test-Path -LiteralPath $sidecarFullPath -PathType Leaf)) {
    Write-Error "$sidecarRelPath not found at HEAD. Cannot verify the manifest-updates log without it."
    exit 1
}
$headContent = Get-Content -Raw -LiteralPath $sidecarFullPath

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
    # ever match at the very start of the whole blob. -join "`n" restores real newlines first.
    $baseContentLines = git -C $RepoRoot show "${BaseRef}:${sidecarRelPath}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "'$sidecarRelPath' exists at $BaseRef per git cat-file, but 'git show' could not read it. This should not happen; investigate before trusting this check's result."
        exit 1
    }
    $baseContent = $baseContentLines -join "`n"
}

$baseCount = Get-LogEntryCount $baseContent
$headCount = Get-LogEntryCount $headContent

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

$gained = $headCount - $baseCount
$plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
Write-Host "OK: $sidecarRelPath's manifest-updates log gained $gained $plural ($baseCount -> $headCount)."
exit 0
