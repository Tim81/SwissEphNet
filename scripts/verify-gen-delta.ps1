#Requires -Version 7
<#
.SYNOPSIS
    Acceptance test for scripts/gen-delta.ps1: every rendered hunk must be a byte-exact excerpt
    of the real files at the coordinates its citation claims.

.DESCRIPTION
    gen-delta.ps1 is what every commit of the 2.10.03 port is transcribed from. Four review
    rounds found five defects in it, all the same shape: a line silently missing from the output,
    or a count that disagreed with the body above it. Each was found by hand, one hunk at a time.

    This is the property those rounds were circling. For every hunk gen-delta renders, take the
    citation range, index into the real 2.08 and 2.10.3 files, and compare case-sensitively
    against the rendered lines:

      * the new-side lines (context + additions) must equal external/swisseph/<file> over the
        cited range
      * the old-side lines (context + deletions) must equal external/pyswisseph-2.08/<file> over
        the corresponding range

    A single dropped context line, a reordered line, an off-by-one in the citation arithmetic, an
    encoding corruption, or a wrong @@-to-range mapping each breaks the equality. That makes this
    one check stronger than "no line was dropped", which is all the earlier rounds could assert.

    Three further vacuity holes are closed, all found by perturbing gen-delta.ps1 and checking
    this script still failed. Each had passed:

      * a hunk body must be the length the @@ header declares. Without that the comparison ran
        over a prefix, so a body truncated by one line, or a body loop that emitted no source
        lines at all, was still certified as "403 hunks verified".
      * each file must render the number of hunks it is pinned to in gen-delta-hunk-counts.tsv.
        Nothing else notices a hunk that was never rendered. Removing gen-delta's final-hunk
        flush drops the last hunk of every file, 17 in all, with every surviving hunk still
        byte-exact. The pin has to be external: gen-delta's own `filtered=N` summary derives
        from the same array as the output, so under that bug the claim moves with the defect.
      * a citation start below 1 is rejected rather than indexed, since [-1] is the last element
        in PowerShell and would compare against the end of the file.

    Measured at the time of writing: 403 of 403 filtered hunks across 24 files, zero mismatches
    on either side.

    Run this after any change to gen-delta.ps1. It needs the submodule and the verified 2.08
    baseline, both of which gen-delta.ps1 itself sets up on first run.

.PARAMETER File
    Check one file only. Defaults to every file with a side in both trees.

.PARAMETER UpdateExpected
    Rewrite scripts/gen-delta-hunk-counts.tsv from the current run. Only legitimate when the
    delta itself changed -- a submodule bump or a change to the licence filter. If the counts
    move without one of those, a hunk is being dropped and this is the wrong command.
#>
[CmdletBinding()]
param(
    [string] $File,
    [switch] $UpdateExpected
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Decode the child's stdout as UTF-8 regardless of the console codepage. See the invocation below.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repoRoot = Split-Path -Parent $PSScriptRoot
$genDelta = Join-Path $PSScriptRoot 'gen-delta.ps1'
$baselineDir = Join-Path $repoRoot 'external/pyswisseph-2.08'
$submoduleDir = Join-Path $repoRoot 'external/swisseph'

# Read exactly as gen-delta does: strict UTF-8, CRLF and lone CR normalized to LF. Comparing
# against differently-decoded text would make this check pass for the wrong reason.
function Read-NormalizedLines {
    param([string] $Path)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false, $true))
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    return $text -split "`n"
}

$names = @()
if ($File) {
    $names = @($File)
}
else {
    $oldNames = Get-ChildItem -LiteralPath $baselineDir -File | Select-Object -ExpandProperty Name
    $newNames = Get-ChildItem -LiteralPath $submoduleDir -File | Select-Object -ExpandProperty Name
    $names = @($oldNames + $newNames | Sort-Object -Unique | Where-Object {
        $_ -ne '.manifest-sha256' -and
        (Test-Path -LiteralPath (Join-Path $baselineDir $_) -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $submoduleDir $_) -PathType Leaf)
    })
}

$totalHunks = 0
$newMismatch = 0
$oldMismatch = 0
$failures = [System.Collections.Generic.List[string]]::new()
$seenCounts = @{}

# The per-file hunk pin. Read from a sidecar rather than embedded here so a change to the delta
# shows up as a data diff a reviewer can read, next to the code change that caused it.
$countsPath = Join-Path $PSScriptRoot 'gen-delta-hunk-counts.tsv'
$expectedHunks = @{}
if (Test-Path -LiteralPath $countsPath -PathType Leaf) {
    foreach ($row in Get-Content -LiteralPath $countsPath) {
        if ($row -match '^\s*(?:#|$)') { continue }
        $cols = $row -split "`t"
        if ($cols.Count -ge 2) { $expectedHunks[$cols[0]] = [int]$cols[1] }
    }
}
elseif (-not $UpdateExpected) {
    Write-Host "FAIL: $countsPath is missing. Without it a dropped hunk passes unnoticed."
    Write-Host 'Regenerate with -UpdateExpected only after confirming the delta is correct.'
    exit 1
}

foreach ($name in $names) {
    $fileHunks = 0
    $oldPath = Join-Path $baselineDir $name
    $newPath = Join-Path $submoduleDir $name
    if (-not (Test-Path -LiteralPath $oldPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $newPath -PathType Leaf)) { continue }

    # Pin UTF-8 across the process boundary. gen-delta's output crosses a pipe, and both ends
    # default to the console codepage on Windows -- ibm850 on this machine. The C sources carry
    # non-ASCII in comments and string literals (`Vondrák 2011` in swephlib.c, `e=23°` in
    # swehouse.c, `359°50'` in sweph.h), so under any OEM codepage every one of those lines came
    # back mangled and the check reported an encoding corruption that exists nowhere in the data.
    # The child sets its own encoding because it inherits the console codepage, not the parent's
    # [Console]::OutputEncoding.
    $rendered = & pwsh -NoProfile -Command @"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(`$false)
& '$genDelta' -File '$name'
"@ 2>$null
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("$name : gen-delta.ps1 exited $LASTEXITCODE")
        continue
    }

    $oldLines = Read-NormalizedLines -Path $oldPath
    $newLines = Read-NormalizedLines -Path $newPath

    # Walk the rendered output hunk by hunk. The citation line carries both the derived range and
    # the original @@ header; the old-side start comes from the header's `-` side.
    $i = 0
    while ($i -lt $rendered.Count) {
        $line = $rendered[$i]
        if ($line -notmatch '^#\s+\S+:(\d+)(?:-(\d+))?\s+--\s+(@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@)') {
            $i++
            continue
        }
        $newStart = [int]$Matches[1]
        $oldStart = [int]$Matches[4]
        # A @@ range with no `,n` covers exactly one line. These are the lengths git itself
        # declared for the hunk, which is what makes them an oracle independent of anything
        # gen-delta computed -- see the body-length assertion below.
        $oldCount = if ($Matches[5]) { [int]$Matches[5] } else { 1 }
        $newCount = if ($Matches[7]) { [int]$Matches[7] } else { 1 }
        $totalHunks++
        $fileHunks++

        # Guard the indexing below. Line numbers are 1-based, so a citation start of 0 would
        # make $newStart - 1 index [-1], which in PowerShell is the *last* element -- the
        # comparison would then run against the end of the file and most likely pass. Not
        # reachable today (the loop only sees files present in both trees, and @@ -0,0 arises
        # only for a one-sided file), but it fails silently rather than loudly if that changes.
        if ($newStart -lt 1 -or $oldStart -lt 1) {
            $failures.Add("$name : citation start below 1 (new $newStart, old $oldStart) in '$line'")
            $i++
            continue
        }

        # Collect the body: everything until the next citation or the trailing summary.
        $body = [System.Collections.Generic.List[string]]::new()
        $j = $i + 1
        while ($j -lt $rendered.Count -and
               $rendered[$j] -notmatch '^#\s+\S+:\d+' -and
               $rendered[$j] -notmatch '^---\s+hunk' -and
               $rendered[$j] -notmatch '^#\s') {
            $body.Add($rendered[$j])
            $j++
        }

        # New side: context + additions, in order, must match the file at $newStart.
        $expectedNew = @($body | Where-Object { $_.StartsWith(' ') -or $_.StartsWith('+') } |
                         ForEach-Object { $_.Substring(1) })

        # Assert the body is the length git said it was, before comparing content. Without this
        # the check compares only a *prefix*: a short body produces a short comparison, and a
        # short comparison matches. Two ways gen-delta can drop lines therefore passed -- a
        # body loop truncated by one (`-lt $n-1`, the classic bound slip), and the body loop
        # removed outright, which emitted citations and no source lines at all and was still
        # certified as "403 hunks verified". Zero compared lines must never read as success.
        if ($expectedNew.Count -ne $newCount) {
            $newMismatch++
            $failures.Add("$name new-side hunk at line ${newStart}: body has $($expectedNew.Count) lines, @@ declares $newCount")
        }
        $actualNew = @()
        if ($expectedNew.Count -gt 0) {
            $actualNew = $newLines[($newStart - 1)..($newStart - 2 + $expectedNew.Count)]
        }
        for ($k = 0; $k -lt $expectedNew.Count; $k++) {
            if (-not ($expectedNew[$k] -ceq $actualNew[$k])) {
                $newMismatch++
                $failures.Add("$name new-side line $($newStart + $k): rendered '$($expectedNew[$k])' vs file '$($actualNew[$k])'")
                break
            }
        }

        # Old side: context + deletions, in order, must match the 2.08 file at $oldStart.
        $expectedOld = @($body | Where-Object { $_.StartsWith(' ') -or $_.StartsWith('-') } |
                         ForEach-Object { $_.Substring(1) })
        if ($expectedOld.Count -ne $oldCount) {
            $oldMismatch++
            $failures.Add("$name old-side hunk at line ${oldStart}: body has $($expectedOld.Count) lines, @@ declares $oldCount")
        }
        $actualOld = @()
        if ($expectedOld.Count -gt 0) {
            $actualOld = $oldLines[($oldStart - 1)..($oldStart - 2 + $expectedOld.Count)]
        }
        for ($k = 0; $k -lt $expectedOld.Count; $k++) {
            if (-not ($expectedOld[$k] -ceq $actualOld[$k])) {
                $oldMismatch++
                $failures.Add("$name old-side line $($oldStart + $k): rendered '$($expectedOld[$k])' vs file '$($actualOld[$k])'")
                break
            }
        }

        $i = $j
    }

    # Pin the count per file. Everything above verifies the hunks that were rendered; nothing
    # above notices a hunk that was never rendered at all. Three ways of losing hunks passed
    # the content checks: the first hunk of every file suppressed, a citation whose range
    # failed to render (skipped by the regex without counting as either checked or failed),
    # and -- a deletion of a real line rather than an invented bug -- removing gen-delta's
    # final-hunk flush, which drops the last hunk of every file, 17 in all.
    #
    # The pin has to come from outside gen-delta. Cross-checking against its own `filtered=N`
    # summary does not work: both that number and the rendered hunks derive from the same
    # array, so under the flush bug the claim moves with the defect and agrees with itself.
    $seenCounts[$name] = $fileHunks
    if ($UpdateExpected) { }
    elseif ($expectedHunks.ContainsKey($name)) {
        if ($fileHunks -ne $expectedHunks[$name]) {
            $failures.Add("$name : rendered $fileHunks hunks, expected $($expectedHunks[$name]). " +
                          "A hunk is missing, or the delta legitimately changed -- if the latter, " +
                          "rerun with -UpdateExpected and commit the table with the change.")
        }
    }
    else {
        $failures.Add("$name : no expected hunk count pinned. Add one via -UpdateExpected so a " +
                      "dropped hunk in this file cannot pass unnoticed.")
    }
}

# A pin for a file that never got checked is the same failure seen from the other side: the file
# left one of the trees, or was renamed. Only meaningful on a full run -- with -File the other
# pins are out of scope by construction.
if (-not $File -and -not $UpdateExpected) {
    foreach ($pinned in $expectedHunks.Keys) {
        if (-not $seenCounts.ContainsKey($pinned)) {
            $failures.Add("$pinned : pinned at $($expectedHunks[$pinned]) hunks but never checked. " +
                          "The file vanished from one of the two trees, or the name changed.")
        }
    }
}

Write-Host ''
Write-Host ("hunks verified : {0}" -f $totalHunks)
Write-Host ("new-side mismatches : {0}" -f $newMismatch)
Write-Host ("old-side mismatches : {0}" -f $oldMismatch)

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($f in $failures | Select-Object -First 20) { Write-Host "  $f" }
    if ($failures.Count -gt 20) { Write-Host "  ... and $($failures.Count - 20) more" }
    Write-Host ''
    Write-Host 'FAIL: the rendered delta does not match the files it cites.'
    Write-Host 'Either a hunk body is not a byte-exact excerpt at its cited range -- something is dropping,'
    Write-Host 'reordering or corrupting a line, or the citation arithmetic is wrong -- or a file rendered a'
    Write-Host 'different number of hunks than it is pinned to, meaning a whole hunk went missing. Either'
    Write-Host 'way the port must not be transcribed from this.'
    exit 1
}

if ($totalHunks -eq 0) {
    Write-Host ''
    Write-Host 'FAIL: no hunks were checked, so this proves nothing. Is the 2.08 baseline fetched and the'
    Write-Host 'submodule initialized? Run scripts/gen-delta.ps1 once to set both up.'
    exit 1
}

Write-Host ''
if ($UpdateExpected) {
    if ($File) {
        Write-Host ''
        Write-Host 'FAIL: -UpdateExpected needs a full run. With -File it would write a table'
        Write-Host 'holding one file and drop the pins for the other 23.'
        exit 1
    }
    $rows = foreach ($k in ($seenCounts.Keys | Sort-Object)) { "$k`t$($seenCounts[$k])" }
    $header = @(
        '# Hunks gen-delta.ps1 renders per file, after the licence filter. Pinned so a dropped',
        '# hunk cannot pass as a smaller total. Regenerate with:',
        '#     scripts/verify-gen-delta.ps1 -UpdateExpected',
        '# and only when the delta legitimately changed -- a submodule bump or a licence-filter',
        '# change. A count that moves on its own is a defect in gen-delta.ps1, not a stale pin.',
        "# file`thunks"
    )
    Set-Content -LiteralPath $countsPath -Value ($header + $rows) -Encoding utf8NoBOM
    Write-Host ''
    Write-Host "WROTE: $countsPath ($($seenCounts.Count) files, $totalHunks hunks)"
    exit 0
}

Write-Host 'PASS: every rendered hunk is a byte-exact excerpt of both files at its cited range.'
exit 0
