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

.PARAMETER SelfTest
    Build throwaway repositories covering every bypass this family of gates has been shown to
    have, run this same script against each of them in a child process, and assert the exit code.
    Touches nothing outside a temporary directory -- in particular it never reads, and never
    writes, the real Tests/conformance/regenerations.log.
#>
[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [string] $BaseRef,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch] $SelfTest,
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$manifestRelPath = 'Tests/conformance/known-fail.tsv'
$sidecarRelPath = 'Tests/conformance/regenerations.log'
$sidecarFullPath = Join-Path $RepoRoot 'Tests/conformance/regenerations.log'

# ---------------------------------------------------------------------------------------------
# Self-test. Placed ahead of the gate body rather than wrapping it, so the gate itself stays
# byte-for-byte what it was: every case below runs this script as a child process and reads its
# exit code, which is the same thing CI does, so nothing can pass here by way of an in-process
# shortcut the real invocation would not take.
#
# Each case builds a real scratch repository. Mocking git would test the mock: the bypasses this
# family of gates has actually had lived in what git reported (which paths changed, what a blob
# held at the base ref), not in the comparison arithmetic alone.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-known-fail-log-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    # By code point, never pasted literally: an invisible character sitting in this file is
    # precisely the thing no reviewer can see, which is the whole reason these two cases exist.
    $SoftHyphen = [string][char]0x00AD
    $ZeroWidthSpace = [string][char]0x200B

    # Multi-line, and one of them multi-paragraph, on purpose. A log of single-line entries would
    # make the CRLF case below vacuous (an entry with no interior line break reads identically
    # whichever ending the file uses) and would never exercise this gate's own rule that a blank
    # line does not start a new entry -- only the next date-prefixed line does.
    $BaseEntries = @(
        @'
2026-01-01 PR #1 (10 -> 8, 2 fewer rows) [ephe: 8 files]: Pruned two
    newly-passing rows after porting one function; no reason is required for a
    pure removal.
'@
        @'
2026-01-02 PR #2 (8 -> 9, 1 more rows) [ephe: 8 files]: Added one row that
    regressed while a second function was being ported.

    A second paragraph of the same entry, carrying no date prefix of its own,
    which is what makes this entry a multi-paragraph one.
'@
        @'
2026-01-03 PR #3 (9 -> 7, 2 fewer rows) [ephe: 8 files]: Pruned two more
    newly-passing rows after a fidelity fix citing the C.
'@
    )

    $NewEntry4 = @'
2026-01-04 PR #4 (7 -> 6, 1 fewer rows) [ephe: 8 files]: Pruned one more
    newly-passing row, with enough text for a reviewer to actually read.
'@
    $NewEntry5 = @'
2026-01-05 PR #5 (6 -> 5, 1 fewer rows): A second added entry, also with real content.
'@
    $NewEntry6 = @'
2026-01-06 PR #6 (5 -> 4, 1 fewer rows): A third added entry, also with real content.
'@

    $BaseKnownFail = "suite`ttestcase`titeration`tcategory`n1`t1`t377`tVALUE-MISMATCH`n1`t1`t379`tVALUE-MISMATCH`n"
    $ChangedKnownFail = "suite`ttestcase`titeration`tcategory`n1`t1`t377`tVALUE-MISMATCH`n"

    function New-LogText {
        param([string[]] $Entries)
        $text = ($Entries -join "`n") + "`n"
        return ($text -replace "`r`n", "`n")
    }

    function Set-LabFile {
        # -Crlf writes the file with CRLF endings while git stores whatever bytes it is given
        # (see New-Lab's core.autocrlf setting), which is how the CRLF case gets a CRLF working
        # tree at HEAD against an LF blob at the base ref.
        param([string] $Path, [string] $Text, [switch] $Crlf)
        $normalized = $Text -replace "`r`n", "`n"
        if ($Crlf) { $normalized = $normalized -replace "`n", "`r`n" }
        [System.IO.File]::WriteAllText($Path, $normalized, (New-Object System.Text.UTF8Encoding $false))
    }

    function New-Lab {
        param([string] $Name)
        $dir = Join-Path $root $Name
        New-Item -ItemType Directory -Path (Join-Path $dir 'Tests/conformance') -Force | Out-Null
        git init -q -b main $dir
        git -C $dir config user.email 'selftest@example.invalid'
        git -C $dir config user.name 'selftest'
        # autocrlf off, and no .gitattributes: the CRLF case needs the blob to hold exactly the
        # bytes written to disk, so that a CRLF working tree really does disagree with an LF base
        # blob instead of both being normalized to LF on the way in and the case proving nothing.
        git -C $dir config core.autocrlf false
        Set-LabFile (Join-Path $dir 'Tests/conformance/known-fail.tsv') $BaseKnownFail
        Set-LabFile (Join-Path $dir 'Tests/conformance/regenerations.log') (New-LogText $BaseEntries)
        git -C $dir add Tests/conformance/known-fail.tsv Tests/conformance/regenerations.log
        git -C $dir commit -q -m 'fixture base'
        return [pscustomobject]@{ Path = $dir; BaseSha = (git -C $dir rev-parse HEAD).Trim() }
    }

    function Set-LabHead {
        # Applies one case's head commit: optionally a known-fail.tsv change, optionally a
        # rewritten log, then commits both named paths (never `git add -A`).
        param(
            [pscustomobject] $Lab,
            [string] $LogText,
            [switch] $ChangeKnownFail,
            [switch] $Crlf
        )
        if ($ChangeKnownFail) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/conformance/known-fail.tsv') $ChangedKnownFail
        }
        # IsNullOrEmpty, not `$null -ne $LogText`: a [string] parameter coerces $null to the empty
        # string, so the null test is always true and "leave the log alone" would silently become
        # "write an empty log" -- which makes the known-fail-changed-without-an-entry case below
        # fail for the wrong reason (the append-only check catching an emptied log) and never
        # exercise the count check it exists for at all.
        if (-not [string]::IsNullOrEmpty($LogText)) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/conformance/regenerations.log') $LogText -Crlf:$Crlf
        }
        git -C $Lab.Path add Tests/conformance/known-fail.tsv Tests/conformance/regenerations.log
        git -C $Lab.Path commit -q -m 'case head'
    }

    function Invoke-Gate {
        # A child process, not dot-sourcing: the gate ends in `exit`, which would tear the
        # self-test down in-process, and an exit code is the only thing CI ever looks at anyway.
        # Assigned, never piped -- through a pipeline $LASTEXITCODE reports the pipe's last stage
        # instead of the command's own status.
        param([pscustomobject] $Lab)
        $output = & pwsh -NoProfile -NonInteractive -File $PSCommandPath -BaseRef $Lab.BaseSha -RepoRoot $Lab.Path 2>&1
        $code = $LASTEXITCODE
        return [pscustomobject]@{ Code = $code; Output = ($output | Out-String) }
    }

    function Assert-GateRefuses {
        param([string] $Case, [pscustomobject] $Lab)
        $r = Invoke-Gate $Lab
        if ($r.Code -ne 0) {
            Write-Host ("  PASS  {0} (refused, exit {1})" -f $Case, $r.Code)
        }
        else {
            Write-Host ("  FAIL  {0}`n          expected a non-zero exit, got 0`n{1}" -f $Case, $r.Output)
            $script:failures++
        }
    }

    function Assert-GateAccepts {
        param([string] $Case, [pscustomobject] $Lab)
        $r = Invoke-Gate $Lab
        if ($r.Code -eq 0) {
            Write-Host ("  PASS  {0} (accepted)" -f $Case)
        }
        else {
            Write-Host ("  FAIL  {0}`n          expected exit 0, got {1}`n{2}" -f $Case, $r.Code, $r.Output)
            $script:failures++
        }
    }

    Write-Host 'verify-known-fail-log self-test'
    Write-Host ''

    # 1. Control. The same fixture and the same kind of commit as every refusal case below, with
    #    nothing planted in it, must be accepted -- otherwise a case that "passes" proves only
    #    that this harness makes the gate red no matter what.
    $lab = New-Lab 'legitimate-append'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry4)) -ChangeKnownFail
    Assert-GateAccepts 'a known-fail.tsv change with one real appended entry is accepted' $lab

    # 2. The gate's basic contract: the work queue moved and the log did not. This is the hand
    #    edit that goes around scripts/regenerate-known-fail.ps1 and its -Reason entirely.
    $lab = New-Lab 'known-fail-without-entry'
    Set-LabHead $lab $null -ChangeKnownFail
    Assert-GateRefuses 'known-fail.tsv changed with no new log entry' $lab

    # 3. An existing entry rewritten in place, with a valid entry appended alongside it. The
    #    append makes the count rise, so a count-only check reports progress while history is
    #    being edited underneath it.
    $edited = @($BaseEntries[0], ($BaseEntries[1] -replace 'regressed', 'improved'), $BaseEntries[2])
    $lab = New-Lab 'entry-edited-in-place'
    Set-LabHead $lab (New-LogText ($edited + $NewEntry4)) -ChangeKnownFail
    Assert-GateRefuses 'an existing entry edited in place (count still rises)' $lab

    # 4. The same edit expressed only as a change of case. PowerShell's -eq and -ne on strings are
    #    culture-aware and case-insensitive, so a comparison written with them reports these two
    #    entries as identical and prints "every prior entry unchanged" -- which is how this bypass
    #    was demonstrated. Only an ordinal comparison sees it. -cne is not the fix either: it
    #    catches this case and misses cases 5 and 6 below.
    $upper = @($BaseEntries[0], $BaseEntries[1].ToUpperInvariant(), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-in-case'
    Set-LabHead $lab (New-LogText ($upper + $NewEntry4)) -ChangeKnownFail
    Assert-GateRefuses 'an existing entry differing only in case' $lab

    # 5. Differs only by a soft hyphen. Invisible in every diff view, and treated as equal by both
    #    -eq and -cne, which is why the comparison has to be ordinal rather than merely
    #    case-sensitive.
    $softened = @($BaseEntries[0], $BaseEntries[1].Insert(40, $SoftHyphen), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-by-soft-hyphen'
    Set-LabHead $lab (New-LogText ($softened + $NewEntry4)) -ChangeKnownFail
    Assert-GateRefuses 'an existing entry differing only by a soft hyphen' $lab

    # 6. Differs only by a zero-width space. Same reasoning as case 5.
    $zeroed = @($BaseEntries[0], $BaseEntries[1].Insert(40, $ZeroWidthSpace), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-by-zero-width-space'
    Set-LabHead $lab (New-LogText ($zeroed + $NewEntry4)) -ChangeKnownFail
    Assert-GateRefuses 'an existing entry differing only by a zero-width space' $lab

    # 7. Entries deleted and more added, so the total count still goes up. This is the bypass the
    #    header comment's point 1 records: three entries become four while two of the original
    #    three are gone. Only an entry-by-entry prefix comparison sees it.
    $lab = New-Lab 'entries-deleted-but-count-rises'
    Set-LabHead $lab (New-LogText @($BaseEntries[2], $NewEntry4, $NewEntry5, $NewEntry6)) -ChangeKnownFail
    Assert-GateRefuses 'two entries deleted and three added (count still rises)' $lab

    # 8. The log gutted in a commit that touches no gated artifact. A gate that only ran its
    #    append-only comparison when known-fail.tsv itself had changed would report "Nothing to
    #    check" and exit 0 here. The prefix comparison has to run whenever the LOG moved.
    $lab = New-Lab 'log-gutted-without-known-fail-change'
    Set-LabHead $lab 'Every entry removed; nothing date-prefixed is left in this file.'
    Assert-GateRefuses 'the log gutted in a commit that touches no known-fail.tsv' $lab

    # 9. A new entry that is a bare date and nothing else. It satisfies "the count went up" while
    #    giving a reviewer nothing at all to read, which is the entire point of demanding an entry.
    $lab = New-Lab 'vacuous-new-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + '2026-01-04 .')) -ChangeKnownFail
    Assert-GateRefuses 'a new entry with no real content' $lab

    # 10. Line endings must stay invisible. HEAD's working tree holds the log as CRLF (this
    #     sidecar has no eol pin in .gitattributes, so a Windows checkout or a fresh worktree under
    #     core.autocrlf produces exactly this) while `git show` returns the base blob's stored LF.
    #     Without normalizing both sides, every multi-line entry reports as edited on a log nobody
    #     touched, and the gate goes red for a reason that has nothing to do with its subject.
    $lab = New-Lab 'crlf-working-tree-vs-lf-blob'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry4)) -ChangeKnownFail -Crlf
    Assert-GateAccepts 'a CRLF working tree against an LF base blob is not an edit' $lab

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all verify-known-fail-log self-test cases passed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

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

# Checked separately from $changed: the append-only guarantee below has to hold whenever the
# sidecar itself moved, not only on a commit that also touches $manifestRelPath. A commit that
# deletes most of the sidecar's entries while leaving $manifestRelPath alone previously never ran
# this diff against the sidecar path at all, so it reported "Nothing to check" -- "nothing to
# check" has to mean neither side changed, not just that the manifest side didn't.
$changedSidecar = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- $sidecarRelPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "git diff between '$BaseRef' and HEAD failed."
    exit 1
}

if (-not $changed -and -not $changedSidecar) {
    Write-Host "No $manifestRelPath or $sidecarRelPath changes between $BaseRef and HEAD. Nothing to check."
    exit 0
}

if ($changed) {
    Write-Host "$manifestRelPath changed between $BaseRef and HEAD."
    Write-Host ""
}

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

# "Must gain a new entry" is conditional on the gated artifact ($changed) actually having
# changed -- a sidecar-only change (caught below, unconditionally, by the append-only prefix
# check) does not by itself require a fresh entry.
if ($changed -and $headCount -le $baseCount) {
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

# Append-only check: run whenever the sidecar itself changed, not only when the gated artifact
# also did -- gutting the log in a commit that touches no $manifestRelPath is exactly the bypass
# this unconditional-on-$changedSidecar placement exists to close (see the $changedSidecar comment
# above). Every entry that existed at -BaseRef must still read identically, in the same position,
# at HEAD. A count that only goes up is not enough on its own -- see this file's own header
# comment for the demonstrated bypass (delete one entry, add two, count still rises).
if ($changedSidecar) {
    for ($i = 0; $i -lt $baseCount; $i++) {
        # [string]::Equals(..., Ordinal): PowerShell's -ne on strings is culture-aware, so it
        # reports two entries as equal when they differ only by case, by a soft hyphen or
        # zero-width space, or by Unicode normalization form (NFD vs NFC) -- confirmed directly,
        # rewriting an existing entry to uppercase and appending a valid new entry still printed
        # "every prior entry unchanged" and exited 0. Ordinal comparison treats the entry as the
        # exact sequence of code points it is, which an append-only log's "still reads identically"
        # requirement means literally.
        if (-not [string]::Equals($headEntries[$i], $baseEntries[$i], [StringComparison]::Ordinal)) {
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
