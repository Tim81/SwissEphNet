#Requires -Version 7
<#
.SYNOPSIS
    Shared engine behind this repository's date-prefixed append-only sidecar logs.

.DESCRIPTION
    Tests/conformance/regenerations.log (scripts/verify-known-fail-log.ps1) was the original: every
    entry starts with a "YYYY-MM-DD " prefix at the start of a line, and everything up to the next
    such line -- blank lines and continuation paragraphs included -- belongs to the entry that
    opened it. Tests/oracle/regenerations.log, regenerations-files.log, regenerations-jpl.log and
    version-classification-regenerations.log (all four written by scripts/run-oracle-dump.ps1's
    siblings -- scripts/regenerate-oracle-known-diff.ps1 and scripts/classify-oracle-versions.ps1)
    use the identical format, entry for entry. This file is the one copy of that format's parsing
    and comparison rules; scripts/verify-oracle-log.ps1 dot-sources it for all four sidecars rather
    than reimplementing (or copy-pasting) the engine a second, third and fourth time.

    Three functions, matching the three things an append-only log check needs to decide:

      Get-DateLogEntries       -- split raw file content into entries, in file order.
      Test-DateLogEntryHasSubstance -- does a newly-added entry have real content, not just a bare
                                       date?
      Test-DateLogEntryUnchangedOrPrFilled -- is a previously-published entry either byte-for-byte
                                       unchanged, or changed only by the one sanctioned edit
                                       (CONTRIBUTING.md's "(no PR yet ...)" placeholder replaced
                                       with the real PR number)?

    Deliberately does NOT wrap these in a single "check this repo/ref pair" entry point: the git
    plumbing (resolving -BaseRef, reading blobs at two refs, deciding which paths changed) differs
    just enough between a single-sidecar gate and a multi-sidecar one (see verify-oracle-log.ps1's
    own Test-SidecarPair) that forcing both through one signature would recreate the abstraction
    mismatch this file exists to avoid in the first place.
#>

Set-StrictMode -Version Latest

# Entries are anchored on a "YYYY-MM-DD " prefix at the start of a line, not a numbered-list
# marker (unlike scripts/verify-freeze-log.ps1 and scripts/verify-baseline-log.ps1's "^\d+\. ",
# which number their own sidecars) -- every log this file serves is dated, not numbered. Everything
# up to (but not including) the next such line belongs to the entry that opened it, which is what
# lets a multi-paragraph entry (Tests/conformance/regenerations.log's own 2026-07-31 Phase 6 probe
# entry, several paragraphs separated by blank lines) stay one entry instead of fragmenting at every
# blank line.
function Get-DateLogEntries {
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
    # @() forces array shape even for zero or one match -- PowerShell unrolls a single-element
    # array on return into a bare scalar, which would silently turn a one-entry log's first
    # element into that entry's first CHARACTER at the call site instead of the entry text.
    return , @($entries.ToArray())
}

# True when $Entry (one element of Get-DateLogEntries' return, date prefix still attached) has
# enough actual content to be worth a reviewer's time. $MinChars matches
# scripts/verify-known-fail-log.ps1's own floor: well under every genuine entry in any of these
# logs today, and well over what a placeholder like a bare date, or a date plus ".", can reach.
function Test-DateLogEntryHasSubstance {
    param([string] $Entry, [int] $MinChars = 20)
    $body = $Entry -replace '^\d{4}-\d{2}-\d{2}\s*', ''
    $meaningfulChars = ($body -replace '[^\p{L}\p{N}]', '')
    return $meaningfulChars.Length -ge $MinChars
}

# CONTRIBUTING.md requires the "(no PR yet ...)" placeholder in a log entry to be replaced with the
# real PR number before merging. That is an edit to an already-published entry, so a naive
# append-only comparison refuses it outright -- this is the one edit this family of gates allows,
# and nothing wider. The base entry must carry a placeholder; the head entry must carry a
# "PR #<digits>" reference; and substituting that reference into the base's placeholder must
# reproduce the head entry character for character. Any other edit -- a reworded sentence, a
# corrected number, a changed SHA -- fails to reproduce it and is still refused.
#
# Two placeholder shapes exist across this repository's producers and both are handled, in this
# order:
#   "1. (no PR yet -- fill in ...) 2026-08-01: ..."  ->  "1. PR #32 2026-08-01: ..."   (parenthetical replaced)
#   "local (no PR yet, log entry 6; ..."             ->  "local (PR #32, log entry 6; ..."  (phrase replaced)
#
# The first pattern is pinned to the literal placeholder every producer emits
# (scripts/regenerate-known-fail.ps1, scripts/regenerate-oracle-known-diff.ps1,
# scripts/classify-oracle-versions.ps1, scripts/verify-swetest-diff.ps1) rather than the looser
# '\(no PR yet[^)]*\)'. Loose, it matches ANY parenthetical merely starting with "no PR yet" and
# swallows everything up to the next ")", so for the second shape above it also accepts
# "local PR #32" -- silently deleting a published cross-reference out of an append-only entry and
# calling that a PR fill. See scripts/verify-known-fail-log.ps1's identical function and its
# 'pr-phrase-parenthetical-gutted' self-test case for where this was measured.
function Test-DateLogEntryUnchangedOrPrFilled {
    param([string] $BaseEntry, [string] $HeadEntry)

    if ([string]::Equals($HeadEntry, $BaseEntry, [StringComparison]::Ordinal)) { return $true }

    # Every "PR #<digits>" in the head entry is a candidate, not just the first. An entry that
    # already cited some other PR ahead of its own placeholder makes the first match the OLD
    # number, so a first-match-only lookup refuses the very fill this exception exists to allow.
    $prRefs = @([regex]::Matches($HeadEntry, 'PR #\d+') | ForEach-Object { $_.Value } | Select-Object -Unique)
    if ($prRefs.Count -eq 0) { return $false }

    foreach ($prRef in $prRefs) {
        foreach ($pattern in @('\(no PR yet -- fill in "PR #N" before merging[^)]*\)', 'no PR yet')) {
            if ($BaseEntry -cnotmatch $pattern) { continue }
            $filled = [regex]::Replace($BaseEntry, $pattern, $prRef)
            if ([string]::Equals($HeadEntry, $filled, [StringComparison]::Ordinal)) { return $true }
        }
    }

    return $false
}
