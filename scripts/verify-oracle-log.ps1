#Requires -Version 7
<#
.SYNOPSIS
    Fails if any of Tests/oracle/known-diff.tsv, known-diff-files.tsv, known-diff-jpl.tsv,
    version-classification.tsv or version-classification-files.tsv changed between -BaseRef and
    HEAD without its paired sidecar log gaining at least one new entry.

.DESCRIPTION
    Three siblings already exist for this exact failure mode: scripts/verify-known-fail-log.ps1
    (Tests/conformance/known-fail.tsv <-> regenerations.log), scripts/verify-baseline-log.ps1
    (Tests/baseline/*.tsv <-> baseline-*.env.txt) and scripts/verify-freeze-log.ps1
    (scripts/freeze-manifest.tsv <-> freeze-manifest-log.txt). Nothing covered
    Tests/oracle/regenerations.log, regenerations-files.log, regenerations-jpl.log or
    version-classification-regenerations.log at all -- a hand edit to known-diff.tsv (or either of
    its two siblings) to waive a row needed no log entry, the exact scenario
    scripts/verify-known-fail-log.ps1's own header says it was written for, just never wired up to
    these four files.

    This is that same mechanism, reused (scripts/lib/DateLogGate.ps1) rather than written a fourth
    time, applied to all four sidecars in one run:

      Tests/oracle/known-diff.tsv        <-> Tests/oracle/regenerations.log
      Tests/oracle/known-diff-files.tsv  <-> Tests/oracle/regenerations-files.log
      Tests/oracle/known-diff-jpl.tsv    <-> Tests/oracle/regenerations-jpl.log
      Tests/oracle/version-classification.tsv AND version-classification-files.tsv
                                          <-> Tests/oracle/version-classification-regenerations.log

    The last pair is the one genuinely new shape: scripts/classify-oracle-versions.ps1 regenerates
    both grids' classification files from a single run and appends one log entry per grid to the
    SAME sidecar, so a change to EITHER version-classification.tsv or version-classification-files.tsv
    (not just both together) must require a new entry -- see Test-SidecarPair's -ManifestRelPaths
    below, which accepts more than one path for exactly this pair.

    Every check below is identical to scripts/verify-known-fail-log.ps1's own: the count must rise
    when the manifest(s) changed; every entry present at -BaseRef must still read identically, in
    the same position, at HEAD (or differ only by the sanctioned "(no PR yet ...)" -> "PR #N" fill);
    and every newly-added entry must have real content. See scripts/lib/DateLogGate.ps1 for the
    shared implementation and scripts/verify-known-fail-log.ps1's own header for the fuller
    rationale (bypasses measured against an earlier version of that gate's sibling family).

    Needs enough history to resolve -BaseRef (fetch-depth: 0, or an explicit fetch of the base
    commit) -- a shallow checkout will make this fail with a clear message rather than silently
    comparing against nothing.

.PARAMETER BaseRef
    Commit-ish to diff HEAD against: the PR's base SHA for pull_request events, or the previous
    commit for push events. Resolved by the caller (see .github/workflows/oracle.yml), not by this
    script, matching every log-gate sibling.

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script.

.PARAMETER SelfTest
    Build throwaway repositories covering the core bypasses this family of gates has been shown to
    have (count-must-rise, append-only entry comparison, the substance floor, the PR-fill
    exception, CRLF normalization) plus the one genuinely new case this script's multi-manifest
    pair needs (either manifest alone, not just both together, must require a new entry), run this
    same script against each in a child process, and assert its exit code AND the failure message
    it gives. Touches nothing outside a temporary directory -- in particular it never reads, and
    never writes, the real Tests/oracle/ sidecars.
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

# The four sidecar pairs this gate covers -- see the .DESCRIPTION for why version-classification's
# is the one with two manifest paths.
function Get-OracleLogPairs {
    return @(
        [pscustomobject]@{ Label = 'known-diff.tsv / regenerations.log'; ManifestRelPaths = @('Tests/oracle/known-diff.tsv'); SidecarRelPath = 'Tests/oracle/regenerations.log' }
        [pscustomobject]@{ Label = 'known-diff-files.tsv / regenerations-files.log'; ManifestRelPaths = @('Tests/oracle/known-diff-files.tsv'); SidecarRelPath = 'Tests/oracle/regenerations-files.log' }
        [pscustomobject]@{ Label = 'known-diff-jpl.tsv / regenerations-jpl.log'; ManifestRelPaths = @('Tests/oracle/known-diff-jpl.tsv'); SidecarRelPath = 'Tests/oracle/regenerations-jpl.log' }
        [pscustomobject]@{ Label = 'version-classification{,-files}.tsv / version-classification-regenerations.log'; ManifestRelPaths = @('Tests/oracle/version-classification.tsv', 'Tests/oracle/version-classification-files.tsv'); SidecarRelPath = 'Tests/oracle/version-classification-regenerations.log' }
    )
}

# Checks one (manifest(s), sidecar) pair. Returns @{ Ok; Message } -- Message is always set, on
# both PASS and FAIL, so the caller (both the real run and -SelfTest's Assert-* helpers) has
# something to print or pattern-match regardless of outcome.
function Test-SidecarPair {
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

    # Normalized to LF, not read as-is: none of these four sidecars carry an eol=lf pin in
    # .gitattributes, so a working-tree checkout that converts LF to CRLF (a Windows clone or CI
    # runner) disagrees with `git show`'s always-LF blob content below -- see
    # scripts/verify-known-fail-log.ps1's identical comment, which measured this directly.
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
    # object onto its output stream (`return , @(...)`) specifically so a plain assignment
    # receives it as one array. Wrapping it again in @() at the call site would build a NEW
    # one-element array whose single element is that whole array -- a nested array-of-one, whose
    # .Count reads 1 regardless of how many real entries it holds. Measured directly while writing
    # this gate's own self-test: with the extra @(), a 2-entry base log and a 3-entry head log both
    # collapsed to "baseCount=1 headCount=1" and every accept/refuse case failed for the same wrong
    # reason. Get-MacroBearingLines in scripts/verify-sedump-macro-parity.ps1 uses the identical
    # `return , $sites` convention and is called the same unwrapped way for the same reason.
    $baseEntries = Get-DateLogEntries $baseContent
    $headEntries = Get-DateLogEntries $headContent
    $baseCount = $baseEntries.Count
    $headCount = $headEntries.Count

    # HIGH 2 fix: a commit that REMOVES entries (headCount < baseCount) must be refused
    # unconditionally, regardless of $changed -- an append-only log must never shrink, whether or
    # not the paired manifest also changed in the same commit. Checked before the count-must-rise
    # block below (which only ever fires when $changed is true) and before the per-entry loops,
    # which index $headEntries[$i] for $i up to $baseCount - 1: without this guard, a head log
    # shorter than the base one reached that loop with headCount < baseCount and threw "Index was
    # outside the bounds of the array" once $i reached headCount, a bare .NET exception instead of
    # this gate's own message, which also aborts the calling foreach in the real run below --
    # pairs after the one that crashed are never checked in that run. Measured directly: a 2-entry
    # base log truncated to 0 entries at head, with $changed forced true, hit this exact exception
    # before this fix; the same shape with $changed false (only the sidecar itself truncated, the
    # manifest untouched) reached the same crash by the $changedSidecar route above.
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

known-diff*.tsv and version-classification*.tsv are the oracle harness's own record: every
committed change needs an entry a reviewer can read without re-deriving it from the diff. If this
was a deliberate regeneration, run scripts/regenerate-oracle-known-diff.ps1 (-Reason '...' in
default mode; -PruneOnly needs none) or scripts/classify-oracle-versions.ps1 -- both append the
required entry automatically. If a file was hand-edited instead, that is very likely the bypass
this gate exists to catch: revert the hand edit and go through the regeneration script instead.
"@
        }
    }

    # Bounded by Math.Min even though headCount >= baseCount is now guaranteed above -- defense in
    # depth against this exact class of indexing bug recurring here, not a claim that it is
    # currently reachable.
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
        # See scripts/lib/DateLogGate.ps1's own comment on Test-DateLogEntryHasSubstance: a
        # duplicate-of-previous-entry check was tried here and reverted -- measured directly
        # against this repository's own Tests/oracle/regenerations-files.log, it refused a real,
        # already-merged commit range over two legitimate, independently-run -PruneOnly entries
        # that happen to share identical boilerplate text (both "0 fewer rows", same day, same
        # PR). Left as the character-count floor alone.
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
# Self-test. Placed ahead of the real run, mirroring scripts/verify-known-fail-log.ps1: every case
# builds a real scratch repository and runs this script as a child process, so nothing here shortcuts
# what git actually reports.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-oracle-log-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    $BaseEntries = @(
        @'
2026-01-01 PR #1 (10 -> 8, 2 fewer rows): Pruned two newly-passing rows after
    porting one function; no reason is required for a pure removal.
'@
        @'
2026-01-02 PR #2 (8 -> 9, 1 more rows): Added one row that regressed while a
    second function was being ported, on purpose.
'@
    )
    $NewEntry3 = @'
2026-01-03 PR #3 (9 -> 8, 1 fewer rows): Pruned one more newly-passing row,
    with enough text for a reviewer to actually read.
'@

    $BaseKnownDiff = "case_id`tcategory`tmax_ulp`treason`nA|1`tPORT-VERSION`t4`tlon differs`nA|2`tPORT-VERSION`t9`tlon differs`n"
    $ChangedKnownDiff = "case_id`tcategory`tmax_ulp`treason`nA|1`tPORT-VERSION`t4`tlon differs`n"

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
        New-Item -ItemType Directory -Path (Join-Path $dir 'Tests/oracle') -Force | Out-Null
        git init -q -b main $dir
        git -C $dir config user.email 'selftest@example.invalid'
        git -C $dir config user.name 'selftest'
        git -C $dir config core.autocrlf false
        Set-LabFile (Join-Path $dir 'Tests/oracle/known-diff.tsv') $BaseKnownDiff
        Set-LabFile (Join-Path $dir 'Tests/oracle/known-diff-files.tsv') "case_id`tcategory`tmax_ulp`treason`n"
        Set-LabFile (Join-Path $dir 'Tests/oracle/known-diff-jpl.tsv') "case_id`tcategory`tmax_ulp`treason`n"
        Set-LabFile (Join-Path $dir 'Tests/oracle/version-classification.tsv') "# comment`ncase_id`tclassification`n"
        Set-LabFile (Join-Path $dir 'Tests/oracle/version-classification-files.tsv') "# comment`ncase_id`tclassification`n"
        Set-LabFile (Join-Path $dir 'Tests/oracle/regenerations.log') (New-LogText $Entries)
        Set-LabFile (Join-Path $dir 'Tests/oracle/regenerations-files.log') ''
        Set-LabFile (Join-Path $dir 'Tests/oracle/regenerations-jpl.log') ''
        Set-LabFile (Join-Path $dir 'Tests/oracle/version-classification-regenerations.log') (New-LogText $Entries)
        git -C $dir add Tests/oracle
        git -C $dir commit -q -m 'fixture base'
        return [pscustomobject]@{ Path = $dir; BaseSha = (git -C $dir rev-parse HEAD).Trim() }
    }

    function Set-LabHead {
        param(
            [pscustomobject] $Lab,
            [string] $KnownDiffLogText,
            [string] $VersionClassLogText,
            [switch] $ChangeKnownDiff,
            [switch] $ChangeKnownDiffFiles,
            [switch] $ChangeVersionClassification,
            [switch] $ChangeVersionClassificationFilesOnly,
            [switch] $Crlf
        )
        if ($ChangeKnownDiff) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/known-diff.tsv') $ChangedKnownDiff
        }
        if ($ChangeKnownDiffFiles) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/known-diff-files.tsv') "case_id`tcategory`tmax_ulp`treason`nF|1`tRETC`t0`tretc differs`n"
        }
        if ($ChangeVersionClassification) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/version-classification.tsv') "# comment`ncase_id`tclassification`nA|1`tAGREES-BOTH`n"
        }
        if ($ChangeVersionClassificationFilesOnly) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/version-classification-files.tsv') "# comment`ncase_id`tclassification`nF|1`tAGREES-BOTH`n"
        }
        if (-not [string]::IsNullOrEmpty($KnownDiffLogText)) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/regenerations.log') $KnownDiffLogText -Crlf:$Crlf
        }
        if (-not [string]::IsNullOrEmpty($VersionClassLogText)) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/oracle/version-classification-regenerations.log') $VersionClassLogText -Crlf:$Crlf
        }
        git -C $Lab.Path add Tests/oracle
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
        if (-not $problem) {
            Write-Host ("  PASS  {0} (refused, exit {1})" -f $Case, $r.Code)
        }
        else {
            Write-Host ("  FAIL  {0}`n          {1}`n{2}" -f $Case, $problem, $r.Output)
            $script:failures++
        }
    }

    function Assert-GateAccepts {
        # $Matching defaults to 'gained \d+ entr', matching every sibling log gate
        # (verify-freeze-log.ps1:255, verify-known-fail-log.ps1:247, verify-baseline-log.ps1:298):
        # exit 0 alone cannot distinguish "the gate looked and approved" from "the gate found
        # nothing to look at" ("[Label] no changes ... Nothing to check." also exits 0). Without a
        # default here, the three call sites below that passed no pattern accepted either outcome
        # silently -- a planted early-return that approves without reading a single entry would
        # have passed this self-test as shipped.
        param([string] $Case, [pscustomobject] $Lab, [string] $Matching = 'gained \d+ entr')
        $r = Invoke-Gate $Lab
        $problem = $null
        if ($r.Code -ne 0) { $problem = "expected exit 0, got $($r.Code)" }
        elseif ($Matching -and $r.Flat -notmatch $Matching) {
            $problem = "accepted as expected, but nothing in its output matched /$Matching/ -- it may have exited 0 without comparing anything"
        }
        if (-not $problem) {
            Write-Host ("  PASS  {0} (accepted)" -f $Case)
        }
        else {
            Write-Host ("  FAIL  {0}`n          {1}`n{2}" -f $Case, $problem, $r.Output)
            $script:failures++
        }
    }

    Write-Host 'verify-oracle-log self-test'
    Write-Host ''

    # 1. Control: a real appended entry alongside the manifest change is accepted.
    $lab = New-Lab 'legitimate-append'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry3)) $null -ChangeKnownDiff
    Assert-GateAccepts 'a known-diff.tsv change with one real appended entry is accepted' $lab 'gained 1 entry'

    # 2. The gate's basic contract: known-diff.tsv changed with no new log entry at all.
    $lab = New-Lab 'known-diff-without-entry'
    Set-LabHead $lab $null $null -ChangeKnownDiff
    Assert-GateRefuses 'known-diff.tsv changed with no new log entry' $lab 'did not gain a new entry'

    # 3. An existing entry rewritten in place, with a valid entry appended alongside it -- the
    #    count-only bypass this whole family of gates exists to close.
    $edited = @($BaseEntries[0], ($BaseEntries[1] -replace 'on purpose', 'by accident'))
    $lab = New-Lab 'entry-edited-in-place'
    Set-LabHead $lab (New-LogText ($edited + $NewEntry3)) $null -ChangeKnownDiff
    Assert-GateRefuses 'an existing entry edited in place (count still rises)' $lab 'append-only, but entry #2 differs'

    # 4. A newly-added entry that is a bare date and nothing else.
    $lab = New-Lab 'vacuous-new-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + '2026-01-03 .')) $null -ChangeKnownDiff
    Assert-GateRefuses 'a new entry with no real content' $lab 'gained entry #3, but it has no real content'

    # 5. CRLF working tree against an LF base blob must not read as an edit.
    $lab = New-Lab 'crlf-working-tree-vs-lf-blob'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry3)) $null -ChangeKnownDiff -Crlf
    Assert-GateAccepts 'a CRLF working tree against an LF base blob is not an edit' $lab

    # 6. The sanctioned PR-placeholder fill.
    $Placeholder = '(no PR yet -- fill in "PR #N" before merging, per CONTRIBUTING.md)'
    $PhEntry2 = @"
2026-01-02 $Placeholder (8 -> 9, 1 more rows): Added one row that regressed
    while a second function was being ported, on purpose.
"@
    $PhFilled2 = $PhEntry2.Replace($Placeholder, 'PR #77')
    $PhBase = @($BaseEntries[0], $PhEntry2)
    $lab = New-Lab 'pr-placeholder-filled' $PhBase
    Set-LabHead $lab (New-LogText (@($BaseEntries[0], $PhFilled2) + $NewEntry3)) $null -ChangeKnownDiff
    Assert-GateAccepts 'a "(no PR yet ...)" placeholder replaced with the real PR number is accepted' $lab

    # 7. The genuinely new shape this script has that its three siblings do not: one sidecar shared
    #    by TWO manifests. Changing version-classification.tsv alone (not -files.tsv too) must still
    #    require a new entry in version-classification-regenerations.log.
    $lab = New-Lab 'version-classification-only-changed'
    Set-LabHead $lab $null $null -ChangeVersionClassification
    Assert-GateRefuses 'version-classification.tsv alone changing requires a new entry' $lab 'version-classification.*did not gain a new entry'

    # 8. The other half of case 7: version-classification-files.tsv alone, not the first file,
    #    still requires a new entry -- proving this is an OR over both paths, not just the first
    #    one named in -ManifestRelPaths.
    $lab = New-Lab 'version-classification-files-only-changed'
    Set-LabHead $lab $null $null -ChangeVersionClassificationFilesOnly
    Assert-GateRefuses 'version-classification-files.tsv alone changing requires a new entry' $lab 'version-classification.*did not gain a new entry'

    # 9. Both manifests changing together, with one real appended entry, is accepted -- the ordinary
    #    case scripts/classify-oracle-versions.ps1 produces on every run (it always regenerates both
    #    grids and appends one entry per grid to the same log).
    $lab = New-Lab 'version-classification-both-changed-with-entry'
    Set-LabHead $lab $null (New-LogText ($BaseEntries + $NewEntry3)) -ChangeVersionClassification -ChangeVersionClassificationFilesOnly
    Assert-GateAccepts 'both version-classification files changing with one real appended entry is accepted' $lab

    # ------------------------------------------------------------------------------------------
    # Crash reproduction. The base ref this gate actually runs against in CI is
    # `git merge-base main HEAD` (resolve-log-base.ps1), not a handful of recent commits -- and at
    # that point in this repository's own history, none of Tests/oracle/known-diff-jpl.tsv,
    # regenerations-jpl.log, or any of the other three pairs, existed yet: `git cat-file -e
    # <merge-base>:Tests/oracle/regenerations-jpl.log` fails outright ("exists on disk, but not in
    # <sha>"). Test-SidecarPair then takes the -not $baseExists branch and sets $baseContent = '',
    # and Get-DateLogEntries's own empty-content early return used a bare `return @()` -- which
    # PowerShell unrolls to ZERO objects on the output stream, not "one object that is an empty
    # array". `$baseEntries = Get-DateLogEntries ''` therefore bound $baseEntries to $null, and
    # $null.Count threw PropertyNotFoundException under Set-StrictMode. Every case above runs this
    # script against a lab where every sidecar already exists (with real content) at the base
    # commit, so none of them exercised this path -- confirmed directly: -BaseRef HEAD~6 could not
    # have caught it, and did not.
    #
    # Two shapes reach the same Get-DateLogEntries('') call: the file not existing at the base
    # commit at all (baseExists = $false, case 10), and the file existing but genuinely empty --
    # 0 bytes committed (case 11). Both are covered; a boundary case at exactly one prior entry
    # (case 12) is covered alongside them, since this class of PowerShell array-unrolling bug
    # clusters at the 0/1 boundary and a fix that only special-cased "truly nothing" could still
    # leave "exactly one" fragile.

    # 10. The manifest and sidecar do not exist AT ALL at the base commit -- not even as an empty
    #     placeholder -- and both are introduced for the first time at head, with a real entry.
    #     This is the exact shape the real merge-base range hit: baseExists = $false.
    $labZero = Join-Path $root 'sidecar-did-not-exist-at-base'
    New-Item -ItemType Directory -Path $labZero -Force | Out-Null
    git init -q -b main $labZero
    git -C $labZero config user.email 'selftest@example.invalid'
    git -C $labZero config user.name 'selftest'
    git -C $labZero config core.autocrlf false
    Set-LabFile (Join-Path $labZero 'README.md') "fixture root; Tests/oracle does not exist yet.`n"
    git -C $labZero add README.md
    git -C $labZero commit -q -m 'fixture base (Tests/oracle does not exist at all)'
    $labZeroBaseSha = (git -C $labZero rev-parse HEAD).Trim()
    New-Item -ItemType Directory -Path (Join-Path $labZero 'Tests/oracle') -Force | Out-Null
    Set-LabFile (Join-Path $labZero 'Tests/oracle/known-diff-jpl.tsv') "case_id`tcategory`tmax_ulp`treason`nJ|1`tPORT-VERSION`t4`tlon differs`n"
    Set-LabFile (Join-Path $labZero 'Tests/oracle/regenerations-jpl.log') (New-LogText @($NewEntry3))
    git -C $labZero add Tests/oracle
    git -C $labZero commit -q -m 'introduce the JPL pair for the first time'
    $labZeroLab = [pscustomobject]@{ Path = $labZero; BaseSha = $labZeroBaseSha }
    Assert-GateAccepts 'the manifest/sidecar not existing at all at the base ref does not crash (real merge-base shape)' $labZeroLab 'gained 1 entry'

    # 11. The sidecar exists and is tracked at the base commit, but is genuinely 0 bytes -- the
    #     other route to the same Get-DateLogEntries('') call, via -not $changedSidecar's sibling
    #     path (the sidecar itself did not change; only the manifest did) rather than
    #     baseExists = $false. New-Lab already commits regenerations-files.log as an empty string
    #     at every lab's base; this case is the first to also change known-diff-files.tsv, which is
    #     what makes it reach the comparison at all instead of short-circuiting on "nothing changed".
    $lab = New-Lab 'sidecar-exists-but-is-empty-at-base'
    Set-LabHead $lab $null $null -ChangeKnownDiffFiles
    Assert-GateRefuses 'known-diff-files.tsv changed while its sidecar stayed empty does not crash, and is refused' $lab 'did not gain a new entry'

    # 12. Boundary control for cases 10-11: a base sidecar with exactly ONE prior entry (not zero)
    #     must still both accept a legitimate append and refuse an edit to that lone entry.
    $lab = New-Lab 'exactly-one-prior-entry-accepts-append' @($BaseEntries[0])
    Set-LabHead $lab (New-LogText (@($BaseEntries[0]) + $NewEntry3)) $null -ChangeKnownDiff
    Assert-GateAccepts 'a base sidecar with exactly one prior entry accepts a legitimate append' $lab 'gained 1 entry'

    # Also appends a real entry alongside the edit -- the count still rising is what makes this
    # case reach the append-only comparison at all instead of being caught first by the simpler
    # count-must-rise check (which an edit-with-no-append would trip on its own, proving nothing
    # about the append-only comparison specifically).
    $lab = New-Lab 'exactly-one-prior-entry-refuses-edit' @($BaseEntries[0])
    $editedFirstEntry = $BaseEntries[0] -replace 'no reason is required', 'no reason at all is needed'
    Set-LabHead $lab (New-LogText (@($editedFirstEntry) + $NewEntry3)) $null -ChangeKnownDiff
    Assert-GateRefuses 'a base sidecar with exactly one prior entry still refuses an edit to it' $lab 'append-only, but entry #1 differs'

    # ------------------------------------------------------------------------------------------
    # HIGH 2 crash reproduction: a commit that REMOVES entries from an already-populated sidecar
    # (baseCount=2 -> headCount=0), which the per-entry loops indexed $headEntries[$i] for $i up to
    # $baseCount - 1 without bounding by $headCount -- "Index was outside the bounds of the array."
    # instead of this gate's own message, and (in the real run below) aborted the calling foreach,
    # so pairs after the one that crashed were never checked in that run. ' ' (a single space, not
    # an empty string) is passed as the head log text so it clears Set-LabHead's own
    # IsNullOrEmpty guard and is written verbatim -- Get-DateLogEntries(' ') still finds zero
    # "YYYY-MM-DD " prefixed lines, so headCount is genuinely 0.

    # 13. Entries removed AND the manifest also changed in the same commit -- $changed is true, so
    #     this reaches the count-must-rise check before HIGH 2's guard was added; that check alone
    #     (`$headCount -le $baseCount`) does not crash on its own, but the per-entry loops right
    #     after it did, on every real range where the count-must-rise check's own refusal wasn't
    #     reached first for an unrelated reason. Covered here for completeness alongside case 14,
    #     which is the shape that could not be caught by any check upstream of the crash at all.
    $lab = New-Lab 'entries-removed-manifest-also-changed'
    Set-LabHead $lab ' ' $null -ChangeKnownDiff
    Assert-GateRefuses 'entries removed (2 -> 0) while the manifest also changed does not crash, and is refused' $lab 'entry count decreased'

    # 14. Entries removed while the manifest itself did NOT change -- the shape this gate's own
    #     header comment (:106-108) says $changedSidecar exists to catch: "a commit that guts the
    #     log while leaving every manifest alone must not silently report 'nothing to check'."
    #     $changed is false here (known-diff.tsv untouched), so before this fix the count-must-rise
    #     check never fired at all (it is gated on $changed), and execution reached the per-entry
    #     loop with headCount (0) < baseCount (2) directly -- the exact crash the review reproduced
    #     against the real merge-base range: "2 entries -> 0 gives 'Index was outside the bounds of
    #     the array.', exit 1".
    $lab = New-Lab 'entries-removed-manifest-unchanged'
    Set-LabHead $lab ' ' $null
    Assert-GateRefuses 'entries removed (2 -> 0) while the manifest itself is untouched does not crash, and is refused' $lab 'entry count decreased'

    # 15. MEDIUM 5, control: a byte-for-byte duplicate of the newest already-published entry (same
    #     date, same PR, same boilerplate outcome text) IS accepted, not refused -- see
    #     scripts/lib/DateLogGate.ps1's Test-DateLogEntryHasSubstance for why a duplicate-content
    #     check was tried and reverted: measured against this repository's own committed
    #     Tests/oracle/regenerations-files.log, two legitimate, independently-run -PruneOnly
    #     entries share this exact shape, and a check that refuses this case refuses real, already-
    #     merged history along with any genuine bypass attempt.
    $lab = New-Lab 'duplicate-of-newest-entry-appended'
    Set-LabHead $lab (New-LogText ($BaseEntries + $BaseEntries[1])) $null -ChangeKnownDiff
    Assert-GateAccepts 'a byte-for-byte duplicate of the newest entry is accepted -- see this case''s own comment for why' $lab 'gained 1 entry'

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all verify-oracle-log self-test cases passed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

git -C $RepoRoot rev-parse --verify "$BaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot resolve base ref '$BaseRef' as a commit. The workflow must check out enough history (fetch-depth: 0, or an explicit fetch of the base commit) for this check to diff against it."
    exit 1
}

$pairs = Get-OracleLogPairs
$anyChecked = $false
$failed = $false
foreach ($pair in $pairs) {
    $result = Test-SidecarPair -RepoRoot $RepoRoot -BaseRef $BaseRef -Label $pair.Label -ManifestRelPaths $pair.ManifestRelPaths -SidecarRelPath $pair.SidecarRelPath
    if ($result.Message -notmatch 'Nothing to check\.$') { $anyChecked = $true }
    if ($result.Ok) {
        Write-Host $result.Message
    }
    else {
        Write-Host $result.Message -ForegroundColor Red
        $failed = $true
    }
}

if (-not $anyChecked) {
    Write-Host ''
    Write-Host 'No Tests/oracle/known-diff*.tsv, version-classification*.tsv or sidecar changes between the base ref and HEAD for any of the four pairs.'
}

if ($failed) { exit 1 }
exit 0
