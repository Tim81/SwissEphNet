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

    Measured at the time of writing: 403 of 403 filtered hunks across 24 files, zero mismatches
    on either side.

    Run this after any change to gen-delta.ps1. It needs the submodule and the verified 2.08
    baseline, both of which gen-delta.ps1 itself sets up on first run.

.PARAMETER File
    Check one file only. Defaults to every file with a side in both trees.
#>
[CmdletBinding()]
param(
    [string] $File
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

foreach ($name in $names) {
    $oldPath = Join-Path $baselineDir $name
    $newPath = Join-Path $submoduleDir $name
    if (-not (Test-Path -LiteralPath $oldPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $newPath -PathType Leaf)) { continue }

    $rendered = & pwsh -NoProfile -File $genDelta -File $name 2>$null
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
        if ($line -notmatch '^#\s+\S+:(\d+)(?:-(\d+))?\s+--\s+(@@ -(\d+)(?:,(\d+))? \+\d+(?:,\d+)? @@)') {
            $i++
            continue
        }
        $newStart = [int]$Matches[1]
        $oldStart = [int]$Matches[4]
        $totalHunks++

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
    Write-Host 'FAIL: a rendered hunk is not a byte-exact excerpt of the file at its cited range.'
    Write-Host 'Something between git and the rendered output is dropping, reordering or corrupting a line,'
    Write-Host 'or the citation arithmetic is wrong. Either way the port must not be transcribed from this.'
    exit 1
}

if ($totalHunks -eq 0) {
    Write-Host ''
    Write-Host 'FAIL: no hunks were checked, so this proves nothing. Is the 2.08 baseline fetched and the'
    Write-Host 'submodule initialized? Run scripts/gen-delta.ps1 once to set both up.'
    exit 1
}

Write-Host ''
Write-Host 'PASS: every rendered hunk is a byte-exact excerpt of both files at its cited range.'
exit 0
