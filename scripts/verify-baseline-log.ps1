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

.PARAMETER RepoRoot
    Repository root. Defaults to the checkout containing this script. Matches
    scripts/verify-freeze-log.ps1 and scripts/verify-known-fail-log.ps1, which both already take
    one, and is what lets -SelfTest point this gate at a scratch repository instead of the real
    tree.

.PARAMETER SelfTest
    Build throwaway repositories covering every bypass this gate has been shown to have, run this
    same script against each of them in a child process, and assert its exit code AND the failure
    message it gives -- this gate has three independent ways to refuse, and the case covering
    entries past a mid-log "## " heading was measured to be resting on the wrong one of them until
    the message was asserted too. Touches nothing outside a temporary directory -- in particular it
    never reads, and never writes, the real Tests/baseline/baseline-*.env.txt.
#>

[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [string]$BaseRef,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')]
    [switch]$SelfTest,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
# Forward slash, not 'Tests\baseline'. A backslash is a literal path character on Linux rather
# than a separator, so Join-Path yields "<root>/Tests\baseline", Get-ChildItem below finds no
# sidecar, and the gate hard-fails with "Expected exactly one sidecar (found 0)" on any non-Windows
# runner. This gate runs on windows-latest today, which is the only reason that has never fired;
# baseline.yml already has an ubuntu-latest job, and the -SelfTest switch is wired into CI, so the
# latent form of this is one workflow edit away. PowerShell accepts forward slashes on Windows.
$baselineDir = Join-Path $RepoRoot 'Tests/baseline'

# ---------------------------------------------------------------------------------------------
# Self-test. Placed ahead of the gate body rather than wrapping it, so the gate itself stays
# byte-for-byte what it was: every case below runs this script as a child process and reads its
# exit code, which is the same thing CI does, so nothing can pass here by way of an in-process
# shortcut the real invocation would not take.
#
# Each case builds a real scratch repository. Mocking git would test the mock: every bypass this
# gate has actually had lived in what git reported (which paths changed, which sidecar path
# existed at the base ref, what that blob held), not in the comparison arithmetic alone.

if ($SelfTest) {
    $failures = 0
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-baseline-log-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    # By code point, never pasted literally: an invisible character sitting in this file is
    # precisely the thing no reviewer can see, which is the whole reason these two cases exist.
    $SoftHyphen = [string][char]0x00AD
    $ZeroWidthSpace = [string][char]0x200B

    $BaseSidecarName = 'baseline-2.8.0.2.env.txt'

    $Header = @'
Environment sidecar fixture built by this script's own self-test. Not the real sidecar.

## Local regenerations

'@

    # A "## " section sitting BETWEEN two log entries, carrying no numbered line of its own. The
    # real sidecar had exactly this shape (271 lines of re-measurable coverage figures between
    # entries 6 and 7) and it broke the parser: with an entry bounded only by the next numbered
    # line, the whole section parsed as the tail of the entry above it, so correcting one figure
    # in it read as rewriting an append-only entry and the gate failed on a log nobody had
    # touched. That section has since been moved above "## Local regenerations", so only cases 11
    # and 12 below keep the misparse from coming back unnoticed.
    $MidSectionBase = @'
## Coverage figures

Re-measurable figures that sit between two log entries and carry no numbered line
of their own: 14220 of 14220 analytic grid rows bit-identical, 2024 of 2024 for
the file-backed grid.

'@
    $MidSectionCorrected = $MidSectionBase -replace '2024 of 2024', '2025 of 2025'

    # Where the mid-log section goes: after this many entries. Entries past it must still be
    # compared (case 12), not silently dropped by the bounding fix (case 11).
    $MidSectionAfter = 3

    # Multi-line on purpose. A log of single-line entries would make the CRLF case below vacuous:
    # an entry with no interior line break reads identically whichever ending the file uses, so
    # only a wrapped entry can tell a normalizing comparison from a non-normalizing one.
    $BaseEntries = @(
        @'
1. abc1234 (2026-01-01): Fixed a mis-transliteration against the C file and line
   it came from; 207 rows changed per TFM, all a diagnostic string.
'@
        @'
2. abc1234 (2026-01-02): Added seven new baseline areas. New coverage, not a
   behavior change: every pre-existing file is byte-identical before and after.
'@
        @'
3. def5678 (2026-01-03): Fixed a duplicate case id in one of the new areas; no
   other new area and no pre-existing area changed.
'@
        @'
4. ghi9012 (2026-01-04): An entry that sits AFTER the mid-log section above, and
   is therefore the one a bounding fix could drop by accident.
'@
        @'
5. jkl3456 (2026-01-05): A second entry after the mid-log section, so that case
   12 below is not testing the last entry as a special case.
'@
    )

    $NewEntry6 = @'
6. mno7890 (2026-01-06): A deliberate, reviewed local-mode regeneration, with
   enough text in it for a reviewer to actually read.
'@
    $NewEntry7 = @'
7. pqr1234 (2026-01-07): A second added entry, also with real content in it.
'@
    $NewEntry8 = @'
8. stu5678 (2026-01-08): A third added entry, also with real content in it.
'@

    $BaseTsv = "case_id`tvalue`nCALC|1`t1.0`nCALC|2`t2.0`n"
    $ChangedTsv = "case_id`tvalue`nCALC|1`t1.5`nCALC|2`t2.0`n"

    function New-LogText {
        param([string[]] $Entries, [string] $MidSection = $MidSectionBase)
        $before = @()
        $after = @()
        for ($i = 0; $i -lt $Entries.Count; $i++) {
            if ($i -lt $MidSectionAfter) { $before += $Entries[$i] } else { $after += $Entries[$i] }
        }
        $chunks = @()
        if ($before.Count -gt 0) { $chunks += (($before -join "`n") + "`n") }
        $chunks += $MidSection
        if ($after.Count -gt 0) { $chunks += (($after -join "`n") + "`n") }
        return (($Header + ($chunks -join "`n")) -replace "`r`n", "`n")
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
        New-Item -ItemType Directory -Path (Join-Path $dir 'Tests/baseline') -Force | Out-Null
        git init -q -b main $dir
        git -C $dir config user.email 'selftest@example.invalid'
        git -C $dir config user.name 'selftest'
        # autocrlf off, and no .gitattributes: the real sidecar is pinned to eol=lf, and the CRLF
        # case below is exactly the "what if that pin is ever removed or narrowed" scenario this
        # gate's own normalization comment describes -- so the lab must NOT reproduce the pin, and
        # the blob must hold exactly the bytes written to disk.
        git -C $dir config core.autocrlf false
        Set-LabFile (Join-Path $dir 'Tests/baseline/baseline-calc.tsv') $BaseTsv
        Set-LabFile (Join-Path $dir "Tests/baseline/$BaseSidecarName") (New-LogText $BaseEntries)
        git -C $dir add Tests/baseline
        git -C $dir commit -q -m 'fixture base'
        return [pscustomobject]@{ Path = $dir; BaseSha = (git -C $dir rev-parse HEAD).Trim() }
    }

    function Set-LabHead {
        # Applies one case's head commit: optionally a golden TSV change, optionally a rewritten
        # log, optionally a sidecar rename, then commits the named path (never `git add -A`).
        param(
            [pscustomobject] $Lab,
            [string] $LogText,
            [switch] $ChangeTsv,
            [switch] $Crlf,
            [string] $RenameSidecarTo
        )
        if ($ChangeTsv) {
            Set-LabFile (Join-Path $Lab.Path 'Tests/baseline/baseline-calc.tsv') $ChangedTsv
        }
        $sidecarName = $BaseSidecarName
        if (-not [string]::IsNullOrEmpty($RenameSidecarTo)) {
            git -C $Lab.Path mv "Tests/baseline/$BaseSidecarName" "Tests/baseline/$RenameSidecarTo"
            $sidecarName = $RenameSidecarTo
        }
        # IsNullOrEmpty, not `$null -ne $LogText`: a [string] parameter coerces $null to the empty
        # string, so the null test is always true and "leave the log alone" would silently become
        # "write an empty log" -- which makes the TSV-changed-without-an-entry case below fail for
        # the wrong reason (the append-only check catching an emptied log) and never exercise the
        # count check it exists for at all.
        if (-not [string]::IsNullOrEmpty($LogText)) {
            Set-LabFile (Join-Path $Lab.Path "Tests/baseline/$sidecarName") $LogText -Crlf:$Crlf
        }
        git -C $Lab.Path add Tests/baseline
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

    # -Matching is not optional decoration here, and case 12 below is the demonstration. This gate has
    # three independent ways to refuse -- the count check, the append-only prefix check and the
    # substance floor -- and a plant aimed at one of them very easily goes red through another, or
    # through a broken fixture, and looks like it proved something it did not. Measured: with
    # Get-LogEntries bounding the SECTION instead of the ENTRY (the alternative its own comment argues
    # against, which drops every entry sitting after the mid-log "## " heading), case 12 -- whose
    # whole purpose is to prove those entries are still compared -- stayed green, because the entries
    # had vanished from BOTH sides and the COUNT check refused instead. Exit code alone cannot tell
    # those two refusals apart; the message can. The four content gates in this repository already
    # assert their messages for the same reason.
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
    # assertion: "entry #4 differs" and "entry #1 differs" are different findings about different
    # entries, and a case that plants an edit in one must not be satisfied by the gate objecting to
    # the other -- which is exactly the confusion case 12 was found to be resting on.
    $CountRefusal = 'did not gain a new entry'
    $AppendOnly1 = 'append-only, but entry #1 differs'
    $AppendOnly2 = 'append-only, but entry #2 differs'
    $AppendOnly4 = 'append-only, but entry #4 differs'
    $NoSubstance = 'gained entry #6, but it has no real content'

    Write-Host 'verify-baseline-log self-test'
    Write-Host ''

    # 1. Control. The same fixture and the same kind of commit as every refusal case below, with
    #    nothing planted in it, must be accepted -- otherwise a case that "passes" proves only
    #    that this harness makes the gate red no matter what.
    $lab = New-Lab 'legitimate-append'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry6)) -ChangeTsv
    Assert-GateAccepts 'a golden TSV change with one real appended entry is accepted' $lab

    # 2. The gate's basic contract: the committed baseline moved and the log did not.
    $lab = New-Lab 'tsv-without-entry'
    Set-LabHead $lab $null -ChangeTsv
    Assert-GateRefuses 'a golden TSV changed with no new log entry' $lab $CountRefusal

    # 3. An existing entry rewritten in place, with a valid entry appended alongside it. The
    #    append makes the count rise, so a count-only check reports progress while history is
    #    being edited underneath it.
    $edited = @($BaseEntries[0], ($BaseEntries[1] -replace 'byte-identical', 'unverified'), $BaseEntries[2], $BaseEntries[3], $BaseEntries[4])
    $lab = New-Lab 'entry-edited-in-place'
    Set-LabHead $lab (New-LogText ($edited + $NewEntry6)) -ChangeTsv
    Assert-GateRefuses 'an existing entry edited in place (count still rises)' $lab $AppendOnly2

    # 4. The same edit expressed only as a change of case. PowerShell's -eq and -ne on strings are
    #    culture-aware and case-insensitive, so a comparison written with them reports these two
    #    entries as identical and prints "every prior entry unchanged" -- which is how this bypass
    #    was demonstrated. Only an ordinal comparison sees it. -cne is not the fix either: it
    #    catches this case and misses cases 5 and 6 below.
    $upper = @($BaseEntries[0], $BaseEntries[1].ToUpperInvariant(), $BaseEntries[2], $BaseEntries[3], $BaseEntries[4])
    $lab = New-Lab 'entry-differs-only-in-case'
    Set-LabHead $lab (New-LogText ($upper + $NewEntry6)) -ChangeTsv
    Assert-GateRefuses 'an existing entry differing only in case' $lab $AppendOnly2

    # 5. Differs only by a soft hyphen. Invisible in every diff view, and treated as equal by both
    #    -eq and -cne, which is why the comparison has to be ordinal rather than merely
    #    case-sensitive.
    $softened = @($BaseEntries[0], $BaseEntries[1].Insert(30, $SoftHyphen), $BaseEntries[2], $BaseEntries[3], $BaseEntries[4])
    $lab = New-Lab 'entry-differs-only-by-soft-hyphen'
    Set-LabHead $lab (New-LogText ($softened + $NewEntry6)) -ChangeTsv
    Assert-GateRefuses 'an existing entry differing only by a soft hyphen' $lab $AppendOnly2

    # 6. Differs only by a zero-width space. Same reasoning as case 5.
    $zeroed = @($BaseEntries[0], $BaseEntries[1].Insert(30, $ZeroWidthSpace), $BaseEntries[2], $BaseEntries[3], $BaseEntries[4])
    $lab = New-Lab 'entry-differs-only-by-zero-width-space'
    Set-LabHead $lab (New-LogText ($zeroed + $NewEntry6)) -ChangeTsv
    Assert-GateRefuses 'an existing entry differing only by a zero-width space' $lab $AppendOnly2

    # 7. Entries deleted and more added, so the total count still goes up. Five entries become
    #    six while four of the original five are gone. Only an entry-by-entry prefix comparison
    #    sees it.
    $renumbered = @(
        ($BaseEntries[4] -replace '^5\. ', '1. '),
        ($NewEntry6 -replace '^6\. ', '2. '),
        ($NewEntry7 -replace '^7\. ', '3. '),
        ($NewEntry8 -replace '^8\. ', '4. '),
        ($NewEntry6 -replace '^6\. ', '5. '),
        ($NewEntry7 -replace '^7\. ', '6. ')
    )
    $lab = New-Lab 'entries-deleted-but-count-rises'
    Set-LabHead $lab (New-LogText $renumbered) -ChangeTsv
    Assert-GateRefuses 'four entries deleted and five added (count still rises)' $lab $AppendOnly1

    # 8. The log gutted in a commit that touches no gated artifact. An earlier version only ran
    #    its append-only comparison when a *.tsv had changed, so this reported "Nothing to check"
    #    and exited 0. The prefix comparison has to run whenever the LOG moved.
    $lab = New-Lab 'log-gutted-without-tsv-change'
    Set-LabHead $lab (New-LogText @())
    Assert-GateRefuses 'the log gutted in a commit that touches no TSV' $lab $AppendOnly1

    # 9. A new entry that is numbered and nothing else. It satisfies "the count went up" while
    #    giving a reviewer nothing at all to read, which is the entire point of demanding an entry.
    $lab = New-Lab 'vacuous-new-entry'
    Set-LabHead $lab (New-LogText ($BaseEntries + '6. .')) -ChangeTsv
    Assert-GateRefuses 'a new entry with no real content' $lab $NoSubstance

    # 10. Line endings must stay invisible. This is the one log gate that did not normalize its
    #     own sidecar, relying on the eol=lf pin in .gitattributes instead; with the pin gone or
    #     its pattern narrowed, HEAD's working tree is CRLF while `git show` returns the base
    #     blob's stored LF, and every multi-line entry reports as edited on a log nobody touched.
    $lab = New-Lab 'crlf-working-tree-vs-lf-blob'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry6)) -ChangeTsv -Crlf
    Assert-GateAccepts 'a CRLF working tree against an LF base blob is not an edit' $lab

    # 11. The misparse that cost this gate a false red: a "## " section between two entries, with
    #     one of its re-measurable figures corrected and a real new entry appended. An entry
    #     bounded only by the next numbered line swallows that whole section into the entry above
    #     it, so correcting a figure reads as rewriting entry 3. This must be accepted.
    $lab = New-Lab 'mid-log-section-edited'
    Set-LabHead $lab (New-LogText ($BaseEntries + $NewEntry6) $MidSectionCorrected) -ChangeTsv
    Assert-GateAccepts 'a figure corrected in a "## " section between entries is not an entry edit' $lab

    # 12. The other half of case 11, and the reason the fix bounds the ENTRY rather than the
    #     section: entries sitting after that "## " heading are still part of the log and must
    #     still be compared. Entry 4 lives past the heading; editing it must be refused.
    $editedPastHeading = @($BaseEntries[0], $BaseEntries[1], $BaseEntries[2], ($BaseEntries[3] -replace 'by accident', 'on purpose'), $BaseEntries[4])
    $lab = New-Lab 'entry-past-mid-log-section-edited'
    Set-LabHead $lab (New-LogText ($editedPastHeading + $NewEntry6)) -ChangeTsv
    Assert-GateRefuses 'an entry sitting after the "## " section edited in place' $lab $AppendOnly4

    # 13. The version-bump rename. EnvInfo.SidecarFileName derives the sidecar's name from
    #     ReferenceVersion, so a reference-mode regeneration renames the file. Looking the base ref
    #     up by HEAD's new name finds nothing at a path that never existed there, which silently
    #     reads as "0 prior entries" -- and 0 prior entries makes any log at all look like it
    #     gained some, so preserving the old log verbatim across the rename satisfies "the count
    #     went up" while describing nothing about this diff. Resolving the base sidecar by pattern
    #     at the base ref is what makes this a refusal.
    $lab = New-Lab 'sidecar-renamed-by-version-bump'
    Set-LabHead $lab (New-LogText $BaseEntries) -ChangeTsv -RenameSidecarTo 'baseline-2.10.0.0.env.txt'
    Assert-GateRefuses 'the sidecar renamed by a version bump with the log carried over unchanged' $lab $CountRefusal

    Write-Host ''
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

    if ($failures -gt 0) {
        Write-Host "FAIL: $failures self-test case(s) failed."
        exit 1
    }
    Write-Host 'PASS: all verify-baseline-log self-test cases passed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------

git -C $RepoRoot rev-parse --verify "$BaseRef^{commit}" *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot resolve base ref '$BaseRef' as a commit. The workflow must check out enough history (fetch-depth: 0, or an explicit fetch of the base commit) for this check to diff against it."
    exit 1
}

$changedTsv = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- 'Tests/baseline/*.tsv'
if ($LASTEXITCODE -ne 0) {
    Write-Error "git diff between '$BaseRef' and HEAD failed."
    exit 1
}

# Checked separately from $changedTsv: the append-only guarantee below has to hold whenever the
# sidecar itself moved, not only on a commit that also touches a golden/waiver/row-count TSV. A
# commit that guts the sidecar's whole log while leaving every *.tsv alone previously never even
# ran this diff against the sidecar path, so it reported "Nothing to check" -- "nothing to check"
# has to mean neither side changed, not just that the TSV side didn't.
$changedSidecar = git -C $RepoRoot diff --name-only "$BaseRef" HEAD -- 'Tests/baseline/baseline-*.env.txt'
if ($LASTEXITCODE -ne 0) {
    Write-Error "git diff between '$BaseRef' and HEAD failed."
    exit 1
}

if (-not $changedTsv -and -not $changedSidecar) {
    Write-Host "No Tests/baseline/*.tsv or sidecar changes between $BaseRef and HEAD. Nothing to check."
    exit 0
}

if ($changedTsv) {
    Write-Host "Tests/baseline/*.tsv changed between $BaseRef and HEAD:"
    $changedTsv | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
}

$sidecars = @(Get-ChildItem $baselineDir -Filter 'baseline-*.env.txt' -ErrorAction SilentlyContinue)
if ($sidecars.Count -ne 1) {
    Write-Error "Expected exactly one Tests/baseline/baseline-*.env.txt sidecar at HEAD (found $($sidecars.Count)). Cannot verify the regenerations log without it."
    exit 1
}
# Normalized to LF, not read as-is: Tests/baseline/baseline-*.env.txt is currently pinned to
# `eol=lf` in .gitattributes, so this normalization is a no-op on every checkout today -- but it
# is the only one of this repository's three log gates that relied on that external pin instead
# of normalizing itself (verify-known-fail-log.ps1 and verify-freeze-log.ps1 both normalize their
# own sidecars explicitly, precisely because their sidecars have no such pin). Should the
# .gitattributes entry ever be removed or the pattern narrowed, a Windows checkout (or a Windows
# CI runner) would check this file out as CRLF while `git show` below always returns the blob's
# own stored content (LF), and every multi-line entry would then report as "edited" purely from a
# line-ending difference that has nothing to do with the log's actual content -- the same failure
# mode the siblings' own comments describe. Normalizing unconditionally, rather than trusting the
# pin to always be there, matches both siblings and costs nothing when the pin already holds.
$headContent = (Get-Content -Raw -Path $sidecars[0].FullName) -replace "`r`n", "`n" -replace "`r", "`n"
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
$baseSidecarPaths = @(git -C $RepoRoot ls-tree -r --name-only $BaseRef -- 'Tests/baseline' 2>$null |
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
    $baseContentLines = git -C $RepoRoot show "${BaseRef}:${baseSidecarRelPath}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Resolved sidecar path '$baseSidecarRelPath' at $BaseRef via git ls-tree, but 'git show' could not read it. This should not happen; investigate before trusting this check's result."
        exit 1
    }
    # Normalized the same way as $headContent above (both sides must agree, or content is what's
    # being compared -- not encoding) -- already LF-only in practice since `git show` returns the
    # blob's stored content directly, but not assumed: see $headContent's own comment for why this
    # gate normalizes explicitly instead of trusting the .gitattributes pin to always be there.
    $baseContent = ($baseContentLines -join "`n") -replace "`r`n", "`n" -replace "`r", "`n"
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

# "Must gain a new entry" is conditional on the gated artifact ($changedTsv) actually having
# changed -- a sidecar-only change (caught below, unconditionally, by the append-only prefix
# check) does not by itself require a fresh entry.
if ($changedTsv -and $headCount -le $baseCount) {
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

# Append-only check: run whenever the sidecar itself changed, not only when the gated artifact
# also did -- gutting the log in a commit that touches no *.tsv is exactly the bypass this
# unconditional-on-$changedSidecar placement exists to close (see the $changedSidecar comment
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
