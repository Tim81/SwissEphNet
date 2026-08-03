#Requires -Version 7
<#
.SYNOPSIS
    Fails if any of Tests/netstandard-compat/known-diff-net8.0.tsv, known-diff-net462.tsv or
    known-diff-net48.tsv changed between -BaseRef and HEAD without
    Tests/netstandard-compat/regenerations.log gaining at least one new entry.

.DESCRIPTION
    Every other gated golden in this repository has a sidecar log and a verify-*-log.ps1 gate:
    Tests/conformance/known-fail.tsv (scripts/verify-known-fail-log.ps1), Tests/oracle/known-diff*.tsv
    and version-classification*.tsv (scripts/verify-oracle-log.ps1), scripts/freeze-manifest.tsv
    (scripts/verify-freeze-log.ps1) and Tests/baseline/*.tsv (scripts/verify-baseline-log.ps1). The
    three Tests/netstandard-compat/known-diff-<fw>.tsv files scripts/verify-netstandard-compat.ps1
    gates on had none: scripts/regenerate-netstandard-compat-known-diff.ps1 already appends to
    Tests/netstandard-compat/regenerations.log on every run, but nothing checked that a committed
    change to a known-diff-<fw>.tsv file actually went through that script rather than a hand edit
    that bypassed -Reason entirely -- the same gap scripts/verify-known-fail-log.ps1's own header
    describes for known-fail.tsv before it existed.

    Reuses scripts/lib/DateLogGate.ps1, the shared engine behind every "YYYY-MM-DD "-prefixed
    append-only sidecar this repository has (see that file's own header) -- this is one more
    consumer of it, not a fifth reimplementation. All three known-diff-<fw>.tsv files are paired
    with the SAME sidecar (regenerate-netstandard-compat-known-diff.ps1 always regenerates whatever
    -Framework selects and appends one entry per framework to this one log), matching
    scripts/verify-oracle-log.ps1's own version-classification pair -- a change to ANY of the three
    manifests, not just all three together, must require a new entry. See that script's own
    Test-SidecarPair, which this script's Test-KnownDiffLog below is modeled on almost verbatim.

    Needs enough history to resolve -BaseRef (fetch-depth: 0, or an explicit fetch of the base
    commit) -- a shallow checkout will make this fail with a clear message rather than silently
    comparing against nothing.

.PARAMETER BaseRef
    Commit-ish to diff HEAD against: the PR's base SHA for pull_request events, or the previous
    commit for push events. Resolved by the caller (see .github/workflows/ci.yml), not by this
    script, matching every log-gate sibling.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.

.PARAMETER SelfTest
    Build throwaway repositories covering the core bypasses this family of gates has been shown to
    have (count-must-rise, append-only entry comparison, the substance floor, the PR-fill
    exception, CRLF normalization, entries-removed crash reproduction) plus the shape genuinely new
    to a three-manifest/one-sidecar pair (any one of the three known-diff-<fw>.tsv files alone, not
    just all three together, must require a new entry), run this same script against each in a
    child process, and assert its exit code AND the failure message it gives. Touches nothing
    outside a temporary directory -- in particular it never reads, and never writes, the real
    Tests/netstandard-compat/ sidecar.
#>
[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [string] $BaseRef,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch] $SelfTest,
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/DateLogGate.ps1')

$ManifestRelPaths = @(
    'Tests/netstandard-compat/known-diff-net8.0.tsv'
    'Tests/netstandard-compat/known-diff-net462.tsv'
    'Tests/netstandard-compat/known-diff-net48.tsv'
)
$SidecarRelPath = 'Tests/netstandard-compat/regenerations.log'
$Label = 'known-diff-<fw>.tsv / regenerations.log'

# Nearly verbatim from scripts/verify-oracle-log.ps1's own Test-SidecarPair -- see this script's
# own header for why it is not shared as a third function in scripts/lib/DateLogGate.ps1 instead:
# the git plumbing here (a single sidecar paired with a fixed set of manifest paths) is close
# enough to that script's own multi-pair shape that copying it once was judged simpler than forcing
# both through one signature, matching DateLogGate.ps1's own stated reasoning for not doing that
# either.
function Test-KnownDiffLog {
    param(
        [string] $RepoRoot,
        [string] $BaseRef,
        [string] $Label,
        [string[]] $ManifestRelPaths,
        [string] $SidecarRelPath
    )

    $sidecarFullPath = Join-Path $RepoRoot $SidecarRelPath

    $changed = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- @ManifestRelPaths
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Ok = $false; Message = "[$Label] git diff between '$BaseRef' and HEAD failed." }
    }

    # Checked separately from $changed: the append-only guarantee has to hold whenever the sidecar
    # itself moved, not only on a commit that also touches a manifest -- a commit that guts the log
    # while leaving every manifest alone must not silently report "nothing to check".
    $changedSidecar = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- $SidecarRelPath
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Ok = $false; Message = "[$Label] git diff between '$BaseRef' and HEAD failed for the sidecar." }
    }

    if (-not $changed -and -not $changedSidecar) {
        return [pscustomobject]@{ Ok = $true; Message = "[$Label] no changes between $BaseRef and HEAD. Nothing to check." }
    }

    if (-not (Test-Path -LiteralPath $sidecarFullPath -PathType Leaf)) {
        return [pscustomobject]@{ Ok = $false; Message = "[$Label] $SidecarRelPath not found at HEAD. Cannot verify the regenerations log without it." }
    }

    # Normalized to LF, not read as-is: this sidecar carries no eol=lf pin in .gitattributes, so a
    # working-tree checkout that converts LF to CRLF (a Windows clone or CI runner) disagrees with
    # `git show`'s always-LF blob content below -- see scripts/verify-known-fail-log.ps1's identical
    # comment, which measured this directly.
    $headContent = (Get-Content -Raw -LiteralPath $sidecarFullPath) -replace "`r`n", "`n" -replace "`r", "`n"

    git -C $RepoRoot cat-file -e "${BaseRef}:${SidecarRelPath}" 2>$null
    $baseExists = ($LASTEXITCODE -eq 0)
    if (-not $baseExists) {
        $baseContent = ''
    }
    else {
        $baseContentLines = git -C $RepoRoot show "${BaseRef}:${SidecarRelPath}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            return [pscustomobject]@{ Ok = $false; Message = "[$Label] '$SidecarRelPath' exists at $BaseRef per git cat-file, but 'git show' could not read it." }
        }
        $baseContent = ($baseContentLines -join "`n") -replace "`r`n", "`n" -replace "`r", "`n"
    }

    # NOT wrapped in another @() here: Get-DateLogEntries already comma-forces a single array
    # object onto its output stream, specifically so a plain assignment receives it as one array --
    # see scripts/verify-oracle-log.ps1's identical comment for the crash this avoids.
    $baseEntries = Get-DateLogEntries $baseContent
    $headEntries = Get-DateLogEntries $headContent
    $baseCount = $baseEntries.Count
    $headCount = $headEntries.Count

    # An append-only log must never shrink, whether or not the paired manifest also changed in the
    # same commit -- checked before the count-must-rise block below (which only ever fires when
    # $changed is true) and before the per-entry loop, which indexes $headEntries[$i] for $i up to
    # $baseCount - 1: without this guard first, a head log shorter than the base one throws "Index
    # was outside the bounds of the array" instead of this gate's own message. See
    # scripts/verify-oracle-log.ps1's own HIGH-2 fix comment, which measured this crash directly.
    if ($headCount -lt $baseCount) {
        return [pscustomobject]@{
            Ok      = $false
            Message = @"
[$Label] $SidecarRelPath entry count decreased ($baseCount -> $headCount) between $BaseRef and
HEAD. An append-only log must never shrink, regardless of whether $($ManifestRelPaths -join ', ')
itself changed in the same commit -- entries are removed only by history rewriting, which this
gate exists to catch, not append-only regeneration.

If a log entry was published in error, add a NEW entry noting the correction instead of removing
the old one.
"@
        }
    }

    if ($changed -and $headCount -le $baseCount) {
        return [pscustomobject]@{
            Ok      = $false
            Message = @"
[$Label] $($ManifestRelPaths -join ', ') changed between $BaseRef and HEAD, but $SidecarRelPath did
not gain a new entry ($baseCount -> $headCount).

known-diff-<fw>.tsv is this instrument's own record of where the netstandard2.0 asset's swe_calc
diverges from net10.0 across target frameworks: every committed change needs an entry a reviewer
can read without re-deriving it from the diff. If this was a deliberate regeneration, run
scripts/regenerate-netstandard-compat-known-diff.ps1 -Reason '...' -- it appends the required
entry automatically. If a file was hand-edited instead, that is very likely the bypass this gate
exists to catch: revert the hand edit and go through the regeneration script instead.
"@
        }
    }

    $commonCount = [Math]::Min($baseCount, $headCount)
    for ($i = 0; $i -lt $commonCount; $i++) {
        if (-not (Test-DateLogEntryUnchangedOrPrFilled -BaseEntry $baseEntries[$i] -HeadEntry $headEntries[$i])) {
            return [pscustomobject]@{
                Ok      = $false
                Message = @"
[$Label] $SidecarRelPath is append-only, but entry #$($i + 1) differs between $BaseRef and HEAD --
it was edited, reordered or removed rather than left alone.

Base entry #$($i + 1):
$($baseEntries[$i])

HEAD entry #$($i + 1):
$($headEntries[$i])

If an old entry was wrong, add a NEW entry noting the correction instead of rewriting history in
place.
"@
            }
        }
    }

    for ($i = $baseCount; $i -lt $headCount; $i++) {
        if (-not (Test-DateLogEntryHasSubstance -Entry $headEntries[$i])) {
            return [pscustomobject]@{
                Ok      = $false
                Message = @"
[$Label] $SidecarRelPath gained entry #$($i + 1), but it has no real content for a reviewer to read:

$($headEntries[$i])

Describe what changed and why, the same way every existing entry in this log does.
"@
            }
        }
    }

    $gained = $headCount - $baseCount
    $plural = if ($gained -eq 1) { 'entry' } else { 'entries' }
    return [pscustomobject]@{ Ok = $true; Message = "[$Label] OK: $SidecarRelPath gained $gained $plural ($baseCount -> $headCount), every prior entry unchanged, every new entry has real content." }
}

# ---------------------------------------------------------------------------------------------
# Self-test.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-netstandard-compat-log-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    $BaseEntries = @(
        @'
2026-01-01 PR #1 (net8.0: 0 -> 0, net462: 0 -> 20, net48: 0 -> 20): Initial bootstrap
    measurement.
'@
        @'
2026-01-02 PR #2 (net48: 20 -> 19, 1 fewer rows): Pruned one newly-matching row after a
    .NET SDK patch release.
'@
    )
    $NewEntry3 = @'
2026-01-03 PR #3 (net462: 20 -> 19, 1 fewer rows): Pruned one more newly-matching row,
    with enough text for a reviewer to actually read.
'@

    $BaseNet8 = "case_id`tcategory`tmax_ulp`treason`n"
    $BaseNet462 = "case_id`tcategory`tmax_ulp`treason`nNSC|1|1`tRUNTIME-MATH`t4`tlon differs`n"
    $BaseNet48 = "case_id`tcategory`tmax_ulp`treason`nNSC|1|1`tRUNTIME-MATH`t4`tlon differs`n"
    $ChangedNet462 = "case_id`tcategory`tmax_ulp`treason`n"
    $ChangedNet48 = "case_id`tcategory`tmax_ulp`treason`nNSC|1|1`tRUNTIME-MATH`t9`tlon differs`n"

    function New-LogText {
        param([string[]] $Entries)
        if ($Entries.Count -eq 0) { return '' }
        return (($Entries -join "`n") + "`n") -replace "`r`n", "`n"
    }

    function Set-LabFile {
        param([string] $Path, [string] $Text, [switch] $Crlf)
        $normalized = $Text -replace "`r`n", "`n"
        if ($Crlf) { $normalized = $normalized -replace "`n", "`r`n" }
        [System.IO.File]::WriteAllText($Path, $normalized, (New-Object System.Text.UTF8Encoding $false))
    }

    function New-Lab {
        param([string] $Name, [string[]] $Entries = $BaseEntries)
        $dir = Join-Path $root $Name
        New-Item -ItemType Directory -Path (Join-Path $dir 'Tests/netstandard-compat') -Force | Out-Null
        git init -q -b main $dir
        git -C $dir config user.email 'selftest@example.invalid'
        git -C $dir config user.name 'selftest'
        git -C $dir config core.autocrlf false
        Set-LabFile (Join-Path $dir 'Tests/netstandard-compat/known-diff-net8.0.tsv') $BaseNet8
        Set-LabFile (Join-Path $dir 'Tests/netstandard-compat/known-diff-net462.tsv') $BaseNet462
        Set-LabFile (Join-Path $dir 'Tests/netstandard-compat/known-diff-net48.tsv') $BaseNet48
        Set-LabFile (Join-Path $dir 'Tests/netstandard-compat/regenerations.log') (New-LogText $Entries)
        git -C $dir add Tests/netstandard-compat
        git -C $dir commit -q -m 'fixture base'
        return [pscustomobject]@{ Path = $dir; BaseSha = (git -C $dir rev-parse HEAD).Trim() }
    }

    function Set-LabHead {
        param(
            [pscustomobject] $Lab,
            [string] $LogText,
            [switch] $ChangeNet462,
            [switch] $ChangeNet48,
            [switch] $Crlf
        )
        if ($ChangeNet462) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/netstandard-compat/known-diff-net462.tsv') $ChangedNet462
        }
        if ($ChangeNet48) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/netstandard-compat/known-diff-net48.tsv') $ChangedNet48
        }
        if (-not [string]::IsNullOrEmpty($LogText)) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/netstandard-compat/regenerations.log') $LogText -Crlf:$Crlf
        }
        git -C $Lab.Path add Tests/netstandard-compat
        git -C $Lab.Path commit -q -m 'case head'
    }

    function Invoke-Gate {
        param([pscustomobject] $Lab)
        $output = & pwsh -NoProfile -NonInteractive -File $PSCommandPath -BaseRef $Lab.BaseSha -RepoRoot $Lab.Path 2>&1
        $code = $LASTEXITCODE
        $text = ($output | Out-String)
        $flat = ($text -replace '(?m)^\s*(\d+\s*)?\|\s?', '') -replace '\s+', ' '
        return [pscustomobject]@{ Code = $code; Output = $text; Flat = $flat }
    }

    function Assert-GateRefuses {
        param([string] $Case, [pscustomobject] $Lab, [string] $Matching)
        $r = Invoke-Gate $Lab
        $problem = $null
        if ($r.Code -eq 0) { $problem = 'expected a non-zero exit, got 0' }
        elseif ($Matching -and $r.Flat -notmatch $Matching) {
            $problem = "refused with exit $($r.Code) as expected, but for the wrong reason: nothing in its output matched /$Matching/"
        }
        if (-not $problem) { Write-Host ("  PASS  {0} (refused, exit {1})" -f $Case, $r.Code) }
        else { Write-Host ("  FAIL  {0}`n          {1}`n{2}" -f $Case, $problem, $r.Output); $script:failures++ }
    }

    function Assert-GateAccepts {
        param([string] $Case, [pscustomobject] $Lab, [string] $Matching = 'gained \d+ entr')
        $r = Invoke-Gate $Lab
        $problem = $null
        if ($r.Code -ne 0) { $problem = "expected exit 0, got $($r.Code)" }
        elseif ($Matching -and $r.Flat -notmatch $Matching) {
            $problem = "accepted as expected, but nothing in its output matched /$Matching/ -- it may have exited 0 without comparing anything"
        }
        if (-not $problem) { Write-Host ("  PASS  {0} (accepted)" -f $Case) }
        else { Write-Host ("  FAIL  {0}`n          {1}`n{2}" -f $Case, $problem, $r.Output); $script:failures++ }
    }

    Write-Host 'verify-netstandard-compat-log self-test'
    Write-Host ''

    # 1. Control: a real appended entry alongside a manifest change is accepted.
    $lab = New-Lab 'legitimate-append'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry3)) -ChangeNet462
    Assert-GateAccepts 'a known-diff-net462.tsv change with one real appended entry is accepted' $lab 'gained 1 entry'

    # 2. The gate's basic contract: a known-diff-<fw>.tsv changed with no new log entry at all.
    $lab = New-Lab 'known-diff-without-entry'
    Set-LabHead $lab $null -ChangeNet462
    Assert-GateRefuses 'known-diff-net462.tsv changed with no new log entry' $lab 'did not gain a new entry'

    # 3. An existing entry rewritten in place, with a valid entry appended alongside it.
    $edited = @($BaseEntries[0], ($BaseEntries[1] -replace 'SDK patch release', 'SDK point release'))
    $lab = New-Lab 'entry-edited-in-place'
    Set-LabHead $lab (New-LogText ($edited + $NewEntry3)) -ChangeNet462
    Assert-GateRefuses 'an existing entry edited in place (count still rises)' $lab 'append-only, but entry #2 differs'

    # 4. A newly-added entry that is a bare date and nothing else.
    $lab = New-Lab 'vacuous-new-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + '2026-01-03 .')) -ChangeNet462
    Assert-GateRefuses 'a new entry with no real content' $lab 'gained entry #3, but it has no real content'

    # 5. CRLF working tree against an LF base blob must not read as an edit.
    $lab = New-Lab 'crlf-working-tree-vs-lf-blob'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry3)) -ChangeNet462 -Crlf
    Assert-GateAccepts 'a CRLF working tree against an LF base blob is not an edit' $lab

    # 6. The sanctioned PR-placeholder fill.
    $Placeholder = '(no PR yet -- fill in "PR #N" before merging, per CONTRIBUTING.md)'
    $PhEntry2 = @"
2026-01-02 $Placeholder (net48: 20 -> 19, 1 fewer rows): Pruned one newly-matching row
    after a .NET SDK patch release.
"@
    $PhFilled2 = $PhEntry2.Replace($Placeholder, 'PR #77')
    $PhBase = @($BaseEntries[0], $PhEntry2)
    $lab = New-Lab 'pr-placeholder-filled' $PhBase
    Set-LabHead $lab (New-LogText (@($BaseEntries[0], $PhFilled2) + $NewEntry3)) -ChangeNet462
    Assert-GateAccepts 'a "(no PR yet ...)" placeholder replaced with the real PR number is accepted' $lab

    # 7. The genuinely new shape here: one manifest of three (net462) alone changing must require a
    #    new entry, proving the OR is over all three paths, not just whichever one a first draft
    #    happened to test.
    $lab = New-Lab 'net462-alone-changed'
    Set-LabHead $lab $null -ChangeNet462
    Assert-GateRefuses 'known-diff-net462.tsv alone changing requires a new entry' $lab 'did not gain a new entry'

    # 8. The other half: net48 alone, not net462, still requires a new entry.
    $lab = New-Lab 'net48-alone-changed'
    Set-LabHead $lab $null -ChangeNet48
    Assert-GateRefuses 'known-diff-net48.tsv alone changing requires a new entry' $lab 'did not gain a new entry'

    # 9. Both manifests changing together, with one real appended entry, is accepted -- the ordinary
    #    case scripts/regenerate-netstandard-compat-known-diff.ps1 -Framework All produces.
    $lab = New-Lab 'both-changed-with-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry3)) -ChangeNet462 -ChangeNet48
    Assert-GateAccepts 'both known-diff-<fw>.tsv files changing with one real appended entry is accepted' $lab

    # 10. Entries removed from an already-populated sidecar must be refused, not crash -- see
    #     scripts/verify-oracle-log.ps1's own HIGH-2 fix comment for the "Index was outside the
    #     bounds of the array" this guards against.
    $lab = New-Lab 'entries-removed-manifest-also-changed'
    Set-LabHead $lab ' ' -ChangeNet462
    Assert-GateRefuses 'entries removed while the manifest also changed does not crash, and is refused' $lab 'entry count decreased'

    $lab = New-Lab 'entries-removed-manifest-unchanged'
    Set-LabHead $lab ' '
    Assert-GateRefuses 'entries removed while the manifest itself is untouched does not crash, and is refused' $lab 'entry count decreased'

    # 11. The manifest/sidecar not existing at all at the base ref (the real merge-base shape, per
    #     scripts/verify-oracle-log.ps1's own case 10) does not crash.
    $labZero = Join-Path $root 'sidecar-did-not-exist-at-base'
    New-Item -ItemType Directory -Path $labZero -Force | Out-Null
    git init -q -b main $labZero
    git -C $labZero config user.email 'selftest@example.invalid'
    git -C $labZero config user.name 'selftest'
    git -C $labZero config core.autocrlf false
    Set-LabFile (Join-Path $labZero 'README.md') "fixture root; Tests/netstandard-compat does not exist yet.`n"
    git -C $labZero add README.md
    git -C $labZero commit -q -m 'fixture base (Tests/netstandard-compat does not exist at all)'
    $labZeroBaseSha = (git -C $labZero rev-parse HEAD).Trim()
    New-Item -ItemType Directory -Path (Join-Path $labZero 'Tests/netstandard-compat') -Force | Out-Null
    Set-LabFile (Join-Path $labZero 'Tests/netstandard-compat/known-diff-net8.0.tsv') $BaseNet8
    Set-LabFile (Join-Path $labZero 'Tests/netstandard-compat/known-diff-net462.tsv') $BaseNet462
    Set-LabFile (Join-Path $labZero 'Tests/netstandard-compat/known-diff-net48.tsv') $BaseNet48
    Set-LabFile (Join-Path $labZero 'Tests/netstandard-compat/regenerations.log') (New-LogText @($NewEntry3))
    git -C $labZero add Tests/netstandard-compat
    git -C $labZero commit -q -m 'introduce the pair for the first time'
    $labZeroLab = [pscustomobject]@{ Path = $labZero; BaseSha = $labZeroBaseSha }
    Assert-GateAccepts 'the manifest/sidecar not existing at all at the base ref does not crash (real merge-base shape)' $labZeroLab 'gained 1 entry'

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all verify-netstandard-compat-log self-test cases passed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

git -C $RepoRoot rev-parse --verify "$BaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot resolve base ref '$BaseRef' as a commit. The workflow must check out enough history (fetch-depth: 0, or an explicit fetch of the base commit) for this check to diff against it."
    exit 1
}

$result = Test-KnownDiffLog -RepoRoot $RepoRoot -BaseRef $BaseRef -Label $Label -ManifestRelPaths $ManifestRelPaths -SidecarRelPath $SidecarRelPath

if ($result.Ok) {
    Write-Host $result.Message
    exit 0
}

Write-Host $result.Message -ForegroundColor Red
exit 1
