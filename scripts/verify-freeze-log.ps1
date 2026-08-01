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

.PARAMETER SelfTest
    Build throwaway repositories covering every bypass this gate has been shown to have, run
    this same script against each of them in a child process, and assert its exit code AND the
    failure message it gives -- this gate has three independent ways to refuse, so a plant aimed
    at one of them can go red through another and look like it proved something it did not.
    Touches nothing outside a temporary directory -- in particular it never reads, and never
    writes, the real scripts/freeze-manifest-log.txt.
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

$manifestRelPath = 'scripts/freeze-manifest.tsv'
$sidecarRelPath = 'scripts/freeze-manifest-log.txt'
$sidecarFullPath = Join-Path $RepoRoot 'scripts/freeze-manifest-log.txt'

# ---------------------------------------------------------------------------------------------
# Self-test. Placed ahead of the gate body rather than wrapping it, so the gate itself stays
# byte-for-byte what it was: every case below runs this script as a child process and reads its
# exit code, which is the same thing CI does, so nothing can pass here by way of an in-process
# shortcut the real invocation would not take.
#
# Each case builds a real scratch repository. Mocking git would test the mock: every bypass this
# gate has actually had lived in what git reported (which paths changed, what a blob held at the
# base ref), not in the comparison arithmetic alone.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-freeze-log-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    # By code point, never pasted literally: an invisible character sitting in this file is
    # precisely the thing no reviewer can see, which is the whole reason these two cases exist.
    $SoftHyphen = [string][char]0x00AD
    $ZeroWidthSpace = [string][char]0x200B

    $Header = @'
Sidecar log fixture built by this script's own self-test. Not the real log.

## Manifest updates

'@

    # Multi-line on purpose. A log of single-line entries would make the CRLF case below vacuous:
    # an entry with no interior line break reads identically whichever ending the file uses, so
    # only a wrapped entry can tell a normalizing comparison from a non-normalizing one.
    $BaseEntries = @(
        @'
1. PR #1 (2026-01-01): Restored a guard the C has and this port had dropped,
   citing the C file and the line it came from.
'@
        @'
2. PR #2 (2026-01-02): Re-transliterated one function after an unrelated
   reformat was reverted, so the frozen fingerprint moved on purpose.
'@
        @'
3. PR #3 (2026-01-03): Fidelity fix; no other frozen line moved, and the
   manifest hash moved with it.
'@
    )

    $NewEntry4 = @'
4. PR #4 (2026-01-04): Another fidelity fix citing the C file and line, with
   enough text in it for a reviewer to actually read.
'@
    $NewEntry5 = @'
5. PR #5 (2026-01-05): A second added entry, also with real content in it.
'@
    $NewEntry6 = @'
6. PR #6 (2026-01-06): A third added entry, also with real content in it.
'@

    $BaseManifest = "path`tsha256`nSwissEphNet/CPort/Sweph.cs`t1111111111111111`n"
    $ChangedManifest = "path`tsha256`nSwissEphNet/CPort/Sweph.cs`t2222222222222222`n"

    function New-LogText {
        param([string[]] $Entries)
        $text = $Header + (($Entries -join "`n") + "`n")
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
        New-Item -ItemType Directory -Path (Join-Path $dir 'scripts') -Force | Out-Null
        git init -q -b main $dir
        git -C $dir config user.email 'selftest@example.invalid'
        git -C $dir config user.name 'selftest'
        # autocrlf off, and no .gitattributes: the CRLF case needs the blob to hold exactly the
        # bytes written to disk, so that a CRLF working tree really does disagree with an LF base
        # blob instead of both being normalized to LF on the way in and the case proving nothing.
        git -C $dir config core.autocrlf false
        Set-LabFile (Join-Path $dir 'scripts/freeze-manifest.tsv') $BaseManifest
        Set-LabFile (Join-Path $dir 'scripts/freeze-manifest-log.txt') (New-LogText $BaseEntries)
        git -C $dir add scripts/freeze-manifest.tsv scripts/freeze-manifest-log.txt
        git -C $dir commit -q -m 'fixture base'
        return [pscustomobject]@{ Path = $dir; BaseSha = (git -C $dir rev-parse HEAD).Trim() }
    }

    function Set-LabHead {
        # Applies one case's head commit: optionally a manifest change, optionally a rewritten
        # log, then commits both named paths (never `git add -A`).
        param(
            [pscustomobject] $Lab,
            [string] $LogText,
            [switch] $ChangeManifest,
            [switch] $Crlf
        )
        if ($ChangeManifest) {
            Set-LabFile (Join-Path $Lab.Path 'scripts/freeze-manifest.tsv') $ChangedManifest
        }
        # IsNullOrEmpty, not `$null -ne $LogText`: a [string] parameter coerces $null to the empty
        # string, so the null test is always true and "leave the log alone" silently became "write
        # an empty log" -- which made the manifest-changed-without-an-entry case below fail for the
        # wrong reason (the append-only check caught the emptied log, so the case never exercised
        # the count check it exists for at all). Found by deleting the count check and observing
        # that no case noticed.
        if (-not [string]::IsNullOrEmpty($LogText)) {
            Set-LabFile (Join-Path $Lab.Path 'scripts/freeze-manifest-log.txt') $LogText -Crlf:$Crlf
        }
        git -C $Lab.Path add scripts/freeze-manifest.tsv scripts/freeze-manifest-log.txt
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
        $text = ($output | Out-String)
        # Flat is the same text with PowerShell's error-display decoration removed and every run of
        # whitespace collapsed to one space. The gate reports through Write-Error, and PowerShell
        # renders an ErrorRecord re-wrapped at the console width -- which differs between a
        # developer's terminal and a CI runner -- with each wrapped line prefixed by a "     | "
        # gutter. So a phrase that reads as one line locally arrives elsewhere as
        # "...no real" on one line and "     | content..." on the next, and a -Matching pattern
        # written the way the sentence reads would fail on terminal width alone. Measured: the
        # substance matcher below did exactly that on first run. Stripping the gutter first, then
        # collapsing whitespace, makes these
        # assertions depend on the gate's words and nothing else.
        $flat = ($text -replace '(?m)^\s*(\d+\s*)?\|\s?', '') -replace '\s+', ' '
        return [pscustomobject]@{ Code = $code; Output = $text; Flat = $flat }
    }

    # -Matching is not optional decoration here. This gate has three independent ways to refuse -- the
    # count check, the append-only prefix check and the substance floor -- and a plant aimed at one of
    # them very easily goes red through another, or through a broken fixture, and looks like it proved
    # something it did not. Demonstrated on the sibling gate: with the entry parser bounding the
    # SECTION instead of the ENTRY (the alternative its own comment argues against), the case that
    # exists to prove entries past a "## " heading are still compared stayed green -- because the
    # entries had vanished from BOTH sides, so the count check refused instead, and an exit-code-only
    # assertion cannot tell those two refusals apart. The four content gates in this repository
    # already assert their messages for the same reason.
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

    # The accept cases assert a message too, and specifically the "gained N entr(y|ies)" line. Exit 0
    # alone is also what this gate reports when it finds nothing to compare -- "Nothing to check" --
    # which is exactly what a lab whose fixture commit silently failed would produce. An accept case
    # that cannot tell "the gate looked and approved" from "the gate found nothing to look at" is the
    # control for every refusal case below resting on nothing.
    function Assert-GateAccepts {
        param([string] $Case, [pscustomobject] $Lab, [string] $Matching = 'log gained \d+ entr')
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

    # The three refusal messages the cases below discriminate between. Entry numbers are part of the
    # assertion: "entry #2 differs" and "entry #1 differs" are different findings about different
    # entries, and a case that plants an edit in one must not be satisfied by the gate objecting to
    # the other.
    $CountRefusal = 'did not gain a new entry'
    $AppendOnly2 = 'append-only, but entry #2 differs'
    $AppendOnly1 = 'append-only, but entry #1 differs'
    $NoSubstance = 'gained entry #4, but it has no real content'

    Write-Host 'verify-freeze-log self-test'
    Write-Host ''

    # 1. Control. The same fixture and the same kind of commit as every refusal case below, with
    #    nothing planted in it, must be accepted -- otherwise a case that "passes" proves only
    #    that this harness makes the gate red no matter what.
    $lab = New-Lab 'legitimate-append'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry4)) -ChangeManifest
    Assert-GateAccepts 'a manifest change with one real appended entry is accepted' $lab

    # 2. The gate's basic contract: the manifest moved and the log did not.
    $lab = New-Lab 'manifest-without-entry'
    Set-LabHead $lab $null -ChangeManifest
    Assert-GateRefuses 'manifest changed with no new log entry' $lab $CountRefusal

    # 3. An existing entry rewritten in place, with a valid entry appended alongside it. The
    #    append makes the count rise, so a count-only check reports progress while history is
    #    being edited underneath it.
    $edited = @($BaseEntries[0], ($BaseEntries[1] -replace 'on purpose', 'by accident'), $BaseEntries[2])
    $lab = New-Lab 'entry-edited-in-place'
    Set-LabHead $lab (New-LogText ($edited + $NewEntry4)) -ChangeManifest
    Assert-GateRefuses 'an existing entry edited in place (count still rises)' $lab $AppendOnly2

    # 4. The same edit expressed only as a change of case. PowerShell's -eq and -ne on strings are
    #    culture-aware and case-insensitive, so a comparison written with them reports these two
    #    entries as identical and prints "every prior entry unchanged" -- which is how this bypass
    #    was demonstrated. Only an ordinal comparison sees it. -cne is not the fix either: it
    #    catches this case and misses cases 5 and 6 below.
    $upper = @($BaseEntries[0], $BaseEntries[1].ToUpperInvariant(), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-in-case'
    Set-LabHead $lab (New-LogText ($upper + $NewEntry4)) -ChangeManifest
    Assert-GateRefuses 'an existing entry differing only in case' $lab $AppendOnly2

    # 5. Differs only by a soft hyphen. Invisible in every diff view, and treated as equal by both
    #    -eq and -cne, which is why the comparison has to be ordinal rather than merely
    #    case-sensitive.
    $softened = @($BaseEntries[0], $BaseEntries[1].Insert(12, $SoftHyphen), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-by-soft-hyphen'
    Set-LabHead $lab (New-LogText ($softened + $NewEntry4)) -ChangeManifest
    Assert-GateRefuses 'an existing entry differing only by a soft hyphen' $lab $AppendOnly2

    # 6. Differs only by a zero-width space. Same reasoning as case 5.
    $zeroed = @($BaseEntries[0], $BaseEntries[1].Insert(12, $ZeroWidthSpace), $BaseEntries[2])
    $lab = New-Lab 'entry-differs-only-by-zero-width-space'
    Set-LabHead $lab (New-LogText ($zeroed + $NewEntry4)) -ChangeManifest
    Assert-GateRefuses 'an existing entry differing only by a zero-width space' $lab $AppendOnly2

    # 7. Entries deleted and more added, so the total count still goes up. This is the bypass the
    #    header comment's point 1 records: three entries become four while two of the original
    #    three are gone. Only an entry-by-entry prefix comparison sees it.
    $renumbered = @(
        ($BaseEntries[2] -replace '^3\. ', '1. '),
        ($NewEntry4 -replace '^4\. ', '2. '),
        ($NewEntry5 -replace '^5\. ', '3. '),
        ($NewEntry6 -replace '^6\. ', '4. ')
    )
    $lab = New-Lab 'entries-deleted-but-count-rises'
    Set-LabHead $lab (New-LogText $renumbered) -ChangeManifest
    Assert-GateRefuses 'two entries deleted and three added (count still rises)' $lab $AppendOnly1

    # 8. The log gutted in a commit that touches no gated artifact. An earlier version only ran
    #    its append-only comparison when the manifest itself had changed, so this reported
    #    "Nothing to check" and exited 0. The prefix comparison has to run whenever the LOG moved.
    $lab = New-Lab 'log-gutted-without-manifest-change'
    Set-LabHead $lab ($Header -replace "`r`n", "`n")
    Assert-GateRefuses 'the log gutted in a commit that touches no manifest' $lab $AppendOnly1

    # 9. A new entry that is numbered and nothing else. It satisfies "the count went up" while
    #    giving a reviewer nothing at all to read, which is the entire point of demanding an entry.
    $lab = New-Lab 'vacuous-new-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + '4. .')) -ChangeManifest
    Assert-GateRefuses 'a new entry with no real content' $lab $NoSubstance

    # 10. Line endings must stay invisible. HEAD's working tree holds the log as CRLF (a Windows
    #     checkout, or core.autocrlf on a fresh worktree) while `git show` returns the base blob's
    #     stored LF. Without normalizing both sides, every multi-line entry reports as edited on a
    #     log nobody touched, and the gate goes red for a reason that has nothing to do with its
    #     subject.
    $lab = New-Lab 'crlf-working-tree-vs-lf-blob'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry4)) -ChangeManifest -Crlf
    Assert-GateAccepts 'a CRLF working tree against an LF base blob is not an edit' $lab

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all verify-freeze-log self-test cases passed.'
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
# guts the sidecar's whole log while leaving $manifestRelPath alone previously never ran this diff
# against the sidecar path at all, so it reported "Nothing to check" -- "nothing to check" has to
# mean neither side changed, not just that the manifest side didn't.
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

# "Must gain a new entry" is conditional on the gated artifact ($changed) actually having
# changed -- a sidecar-only change (caught below, unconditionally, by the append-only prefix
# check) does not by itself require a fresh entry.
if ($changed -and $headCount -le $baseCount) {
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

# Append-only check: run whenever the sidecar itself changed, not only when the gated artifact
# also did -- gutting the log in a commit that touches no $manifestRelPath is exactly the bypass
# this unconditional-on-$changedSidecar placement exists to close (see the $changedSidecar comment
# above). Every entry that existed at -BaseRef must still read identically, in the same position,
# at HEAD. A count that only goes up is not enough on its own -- see the header comment's point 1
# for the demonstrated bypass (delete one entry, add two, count still rises).
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
