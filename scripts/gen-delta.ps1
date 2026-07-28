#Requires -Version 7
<#
.SYNOPSIS
    Per-file C diff between the Swiss Ephemeris 2.08 baseline and the 2.10.03 upstream vendored
    as a submodule, for a porting reviewer to check a C# hunk citation against.

.DESCRIPTION
    Compares external/pyswisseph-2.08/<file> (the 2.08 baseline; see
    scripts/fetch-2.08-baseline.ps1) against external/swisseph/<file> (the v2.10.3final
    submodule). Those two fixed paths are the ONLY 2.08 and 2.10.3 inputs this script has --
    there is no parameter that accepts a different 2.08 source, and in particular no parameter
    or code path that can point at the aloistr/swisseph `v2.08.00a` git tag. That tag is an
    incomplete snapshot (missing swecl.c, swehouse.c, swehel.c entirely, and a truncated
    swephexp.h) and diffing against it silently produces a wrong work queue. See
    CONTRIBUTING.md and scripts/fetch-2.08-baseline.ps1.

    Two things make the output usable for review instead of just noisy:

    * License-noise filter (on by default, -IncludeLicenseHunks to see it anyway). Every file
      Astrodienst re-licensed from GPL-2 to AGPL-3 carries the same header rewrite -- the
      copyright year, "GNU public license" -> "GNU Affero General Public License", the GPL/AGPL
      URL, and so on. Hunks whose every changed line matches one of a fixed set of known license
      phrases are dropped from the reported diff and counted separately, so what is left is the
      part a porting reviewer actually has to read.

    * Comments-stripped variant for headers (-File *.h, on by default). Header files are mostly
      doc comments; a raw diff over-counts because the license rewrite and other comment-only
      edits sit close enough to real declaration changes that they land in the same hunk (unlike
      in the .c files, where the header is usually its own isolated hunk). Stripping /* ... */
      comments from both sides before diffing isolates the actual code change -- the real
      `#define`/prototype/struct-field delta -- from prose noise. Reported alongside the raw
      diff, not instead of it.

.PARAMETER File
    A single file name, e.g. sweph.c. If omitted, every file present on both sides is processed
    and a one-line summary is printed for each; the full diff body is only printed when -File
    names exactly one file.

.PARAMETER IncludeLicenseHunks
    Do not filter out the GPL-2 -> AGPL-3 header rewrite hunks.

.PARAMETER NoCommentStrip
    Skip the comments-stripped variant even for header files.
#>
[CmdletBinding()]
param(
    [string] $File,
    [switch] $IncludeLicenseHunks,
    [switch] $NoCommentStrip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Fixed, not parameterized -- see the guard-rail note in the synopsis above. The 2.08 side is
# always what scripts/fetch-2.08-baseline.ps1 produced and verified; the 2.10.3 side is always
# the pinned submodule checkout. Neither is ever a git tag reference.
$baselineDir = Join-Path $repoRoot 'external/pyswisseph-2.08'
$submoduleDir = Join-Path $repoRoot 'external/swisseph'

if (-not (Test-Path -LiteralPath $baselineDir -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $baselineDir -File -ErrorAction SilentlyContinue)) {
    Write-Host "2.08 baseline not found at $baselineDir -- fetching it."
    & (Join-Path $PSScriptRoot 'fetch-2.08-baseline.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: could not prepare the 2.08 baseline.'
        exit 1
    }
}

if (-not (Test-Path -LiteralPath $submoduleDir -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $submoduleDir -Filter '*.c' -File -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: $submoduleDir has no C sources checked out."
    Write-Host 'Run: git submodule update --init external/swisseph'
    exit 1
}

# Known GPL-2 -> AGPL-3 header-rewrite phrases. A hunk is license noise only if every one of its
# changed (+/-) lines matches at least one of these -- a hunk that mixes a license-text change
# with a real code change is deliberately NOT filtered.
$licensePatterns = @(
    'Copyright \(C\) 1997 - \d{4} Astrodienst AG'
    'GNU public license version 2 or later'
    'GNU Affero General Public License \(AGPL\)'
    'GNU GPL software license'
    'AGPL software license'
    'GNU GPL or a compatible license'
    'AGPL or a compatible license'
    'gpl-2\.0\.html'
    'agpl-3\.0\.html'
    '\$Header: /home/dieter/sweph/RCS/'
    '^\+?-?\s*\*+/?\s*$'                       # bare comment border lines ( /*, */, blank-ish )
    '^\s*$'                                    # blank / trailing-whitespace-only lines inside
                                                # the license comment block getting trimmed
)
$licenseRegex = ($licensePatterns | ForEach-Object { "($_)" }) -join '|'

function Get-NormalizedLines {
    param([string] $Path)
    $text = [System.IO.File]::ReadAllText($Path)
    $text = $text -replace "`r`n", "`n" -replace "`r", "`n"
    return $text -split "`n"
}

function Write-NormalizedTemp {
    param([string] $Path, [string] $TempPath)
    $lines = Get-NormalizedLines -Path $Path
    [System.IO.File]::WriteAllText($TempPath, ($lines -join "`n"))
}

function Strip-CComments {
    param([string] $Path, [string] $TempPath)
    $text = [System.IO.File]::ReadAllText($Path)
    $text = $text -replace "`r`n", "`n" -replace "`r", "`n"
    $stripped = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $lines = $stripped -split "`n" | Where-Object { $_.Trim() -ne '' }
    [System.IO.File]::WriteAllText($TempPath, ($lines -join "`n"))
}

function Get-Hunks {
    param([string] $DiffText)
    if (-not $DiffText) { return @() }
    $lines = $DiffText -split "`n"
    $hunks = [System.Collections.Generic.List[object]]::new()
    $current = $null
    foreach ($line in $lines) {
        if ($line.StartsWith('@@')) {
            if ($current) { $hunks.Add($current) }
            $current = [System.Collections.Generic.List[string]]::new()
            continue
        }
        if ($null -ne $current -and ($line.StartsWith('+') -or $line.StartsWith('-'))) {
            if (-not ($line.StartsWith('+++') -or $line.StartsWith('---'))) {
                $current.Add($line)
            }
        }
    }
    if ($current) { $hunks.Add($current) }
    return $hunks
}

function Test-LicenseHunk {
    param($HunkLines)
    foreach ($line in $HunkLines) {
        $content = $line.Substring(1)
        if ($content -notmatch $licenseRegex) { return $false }
    }
    return $true
}

function Get-Diff {
    param([string] $OldPath, [string] $NewPath)
    $diff = & git -C $repoRoot diff --no-index --no-color --unified=3 -- $OldPath $NewPath 2>$null
    return ($diff -join "`n")
}

function Invoke-FileDelta {
    param([string] $Name)

    $oldPath = Join-Path $baselineDir $Name
    $newPath = Join-Path $submoduleDir $Name

    $hasOld = Test-Path -LiteralPath $oldPath -PathType Leaf
    $hasNew = Test-Path -LiteralPath $newPath -PathType Leaf

    if (-not $hasNew) {
        return [pscustomobject]@{
            File = $Name; Status = 'pyswisseph-only (no 2.10.3 counterpart)'
            RawHunks = 0; FilteredHunks = 0; LicenseHunks = 0
            RawPlus = 0; RawMinus = 0; StrippedPlus = 0; StrippedMinus = 0
        }
    }
    if (-not $hasOld) {
        return [pscustomobject]@{
            File = $Name; Status = 'new in 2.10.3 (no 2.08 baseline)'
            RawHunks = 0; FilteredHunks = 0; LicenseHunks = 0
            RawPlus = 0; RawMinus = 0; StrippedPlus = 0; StrippedMinus = 0
        }
    }

    $tmp = [System.IO.Path]::GetTempPath()
    $tmpOld = Join-Path $tmp "gen-delta-old-$Name"
    $tmpNew = Join-Path $tmp "gen-delta-new-$Name"
    Write-NormalizedTemp -Path $oldPath -TempPath $tmpOld
    Write-NormalizedTemp -Path $newPath -TempPath $tmpNew

    $diffText = Get-Diff -OldPath $tmpOld -NewPath $tmpNew
    $rawHunks = Get-Hunks -DiffText $diffText
    $licenseHunks = @($rawHunks | Where-Object { Test-LicenseHunk -HunkLines $_ })
    $filteredHunks = @($rawHunks | Where-Object { -not (Test-LicenseHunk -HunkLines $_) })

    $rawPlus = @($diffText -split "`n" | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }).Count
    $rawMinus = @($diffText -split "`n" | Where-Object { $_.StartsWith('-') -and -not $_.StartsWith('---') }).Count

    $strippedPlus = 0
    $strippedMinus = 0
    $isHeader = $Name.EndsWith('.h')
    if ($isHeader -and -not $NoCommentStrip) {
        $tmpOldStripped = Join-Path $tmp "gen-delta-old-stripped-$Name"
        $tmpNewStripped = Join-Path $tmp "gen-delta-new-stripped-$Name"
        Strip-CComments -Path $oldPath -TempPath $tmpOldStripped
        Strip-CComments -Path $newPath -TempPath $tmpNewStripped
        $strippedDiff = Get-Diff -OldPath $tmpOldStripped -NewPath $tmpNewStripped
        $strippedPlus = @($strippedDiff -split "`n" | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }).Count
        $strippedMinus = @($strippedDiff -split "`n" | Where-Object { $_.StartsWith('-') -and -not $_.StartsWith('---') }).Count
        Remove-Item -LiteralPath $tmpOldStripped, $tmpNewStripped -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $tmpOld, $tmpNew -ErrorAction SilentlyContinue

    [pscustomobject]@{
        File          = $Name
        Status        = 'ok'
        RawHunks      = $rawHunks.Count
        FilteredHunks = $filteredHunks.Count
        LicenseHunks  = $licenseHunks.Count
        RawPlus       = $rawPlus
        RawMinus      = $rawMinus
        StrippedPlus  = $strippedPlus
        StrippedMinus = $strippedMinus
        DiffText      = $diffText
        FilteredLines = $filteredHunks
    }
}

# --- Single-file mode: print the (filtered, unless -IncludeLicenseHunks) diff plus a summary ---

if ($File) {
    $result = Invoke-FileDelta -Name $File
    if ($result.Status -ne 'ok') {
        Write-Host "$($result.File): $($result.Status)"
        exit 0
    }

    if ($IncludeLicenseHunks) {
        Write-Output $result.DiffText
    }
    else {
        # Reconstruct a diff body containing only the non-license hunks' changed lines. This is
        # a reviewer-facing listing of changed lines per hunk, not a byte-identical re-diff.
        $i = 0
        foreach ($hunk in $result.FilteredLines) {
            $i++
            Write-Output "--- hunk $i ---"
            foreach ($line in $hunk) { Write-Output $line }
            Write-Output ''
        }
    }

    Write-Host ''
    Write-Host "# $($result.File): hunks raw=$($result.RawHunks) filtered=$($result.FilteredHunks) license-noise=$($result.LicenseHunks)"
    Write-Host "#   raw lines: +$($result.RawPlus) -$($result.RawMinus)"
    if ($File.EndsWith('.h') -and -not $NoCommentStrip) {
        Write-Host "#   comments-stripped lines: +$($result.StrippedPlus) -$($result.StrippedMinus)"
    }
    exit 0
}

# --- All-files mode: one summary row per file, plus totals -------------------------------------

$names = @(
    Get-ChildItem -LiteralPath $baselineDir -File | ForEach-Object { $_.Name }
    Get-ChildItem -LiteralPath $submoduleDir -File | ForEach-Object { $_.Name }
) | Sort-Object -Unique

$rows = foreach ($name in $names) { Invoke-FileDelta -Name $name }

$rows | Select-Object File, Status, RawHunks, FilteredHunks, LicenseHunks, RawPlus, RawMinus, StrippedPlus, StrippedMinus |
    Format-Table -AutoSize | Out-String -Width 240 | Write-Host

$ok = @($rows | Where-Object { $_.Status -eq 'ok' })
$totalRaw = ($ok | Measure-Object -Property RawHunks -Sum).Sum
$totalFiltered = ($ok | Measure-Object -Property FilteredHunks -Sum).Sum
$totalLicense = ($ok | Measure-Object -Property LicenseHunks -Sum).Sum
Write-Host "TOTAL over $($ok.Count) files with both a 2.08 and 2.10.3 side: raw hunks=$totalRaw filtered=$totalFiltered license-noise=$totalLicense"
exit 0
