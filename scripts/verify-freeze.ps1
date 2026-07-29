#Requires -Version 7
<#
.SYNOPSIS
    Fails if any file under the transliteration freeze has been reformatted.

.DESCRIPTION
    SwissEphNet/CPort/, Programs/SweTest/Program.cs and Programs/SweMini/Program.cs are
    deliberate line-by-line transliterations of the Swiss Ephemeris C source. That
    correspondence is the single property that makes each upstream upgrade tractable, and
    reformatting destroys it permanently. See CONTRIBUTING.md.

    Two earlier defenses are both incomplete:

      * CONTRIBUTING.md documents three `dotnet format --exclude` flags, but that depends on
        whoever runs the command remembering to pass them.
      * The nested .editorconfig files pin `trim_trailing_whitespace` and `insert_final_newline`,
        which does work. They deliberately do NOT pin `csharp_new_line_before_open_brace`,
        because .editorconfig has no value meaning "preserve": `none` is as strong an
        instruction as `all`, merely pointing the other way, and pinning it measurably widened
        the damage rather than preventing it.

    So this script is the actual guard. It does not care which tool did the reformatting, or
    whether a tool was involved at all. It records a structural fingerprint of each frozen path
    and fails when the fingerprint moves.

    It is deliberately NOT a fidelity check. Whether a hunk faithfully matches the C it cites is
    a review judgement and cannot be automated cheaply. This answers only the narrower question
    "did anyone reformat", which is exactly what the freeze exists to prevent.

    A legitimate change to a frozen file -- a fidelity fix correcting a divergence from the C,
    or the 2.10.03 re-transliteration itself -- will move these counts. That is expected. Update
    the manifest in the same commit, so the new counts are reviewed alongside the change that
    caused them rather than drifting silently.

.PARAMETER ManifestPath
    Defaults to scripts/freeze-manifest.tsv.

.PARAMETER Update
    Rewrite the manifest from the current tree instead of checking against it. Use only when a
    change to a frozen file is intended, and commit the result together with that change.
#>
[CmdletBinding()]
param(
    [string] $ManifestPath,
    [switch] $Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $PSScriptRoot 'freeze-manifest.tsv'
}

# The frozen paths, exactly as CONTRIBUTING.md names them. A directory means every *.cs under
# it, recursively; anything added there later is covered automatically rather than silently
# escaping the freeze.
$frozenPaths = @(
    'SwissEphNet/CPort'
    'Programs/SweTest/Program.cs'
    'Programs/SweMini/Program.cs'
)

function Get-FrozenFile {
    param([string] $RelativePath)

    $full = Join-Path $repoRoot $RelativePath
    if (Test-Path -LiteralPath $full -PathType Container) {
        # Every file, not just *.cs. The only non-.cs file under a frozen path today is
        # CPort/.editorconfig, and leaving it out meant the hash could not see it being
        # edited -- so replacing it with the csharp_new_line_before_open_brace pin that
        # measurably widens dotnet format's damage returned PASS. That silently disarms
        # one of the two defenses the freeze rests on.
        # Ordinal sort, not Sort-Object. Sort-Object is culture-aware and case-insensitive even
        # with -CaseSensitive, so its order depends on ICU vs NLS vs invariant mode. It agrees
        # across platforms today only because every frozen filename is ASCII alphanumeric; a
        # name with punctuation, or two differing only in case, would order differently on
        # Windows and Linux and move the hash without any content changing.
        # -Force, or dotfiles are invisible on Linux. PowerShell treats a leading-dot name as
        # hidden on Unix but not on Windows, so without it CPort/.editorconfig is counted here
        # and not on the CI runner: the manifest generated on Windows reported 17 files and the
        # ubuntu job read 16 from the same commit. That would make the hash platform-dependent,
        # which is the one property the line-ending normalization above exists to preserve.
        $found = @(Get-ChildItem -LiteralPath $full -Recurse -File -Force)
        $keys = [string[]] ($found | ForEach-Object { $_.FullName })
        $items = [object[]] $found
        [System.Array]::Sort($keys, $items, [System.StringComparer]::Ordinal)
        $items
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) {
        Get-Item -LiteralPath $full
    }
    else {
        throw "Frozen path not found: $RelativePath. If it was moved or renamed, update " +
              "`$frozenPaths in this script and CONTRIBUTING.md's --exclude list together."
    }
}

function Get-Fingerprint {
    param([string] $RelativePath)

    $files = @(Get-FrozenFile -RelativePath $RelativePath)

    $lines = 0        # total physical lines; any reflow moves this
    $krBraces = 0     # `) {` -- the transliterated C's brace style, what Allman-ising destroys
    $trailingWs = 0   # trailing whitespace; what `dotnet format whitespace` strips
    # Accumulates every frozen file's normalized content, in the sorted order
    # Get-FrozenFile returns, so the hash below is stable across machines.
    $contents = [System.Collections.Generic.List[string]]::new()

    foreach ($file in $files) {
        # Read raw so line-ending normalization cannot mask a change, and split on both
        # conventions: these files are CRLF in the repo but a contributor's tooling may not be.
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $fileLines = $text -split "`r`n|`n|`r"

        # A trailing empty element is the artifact of a final newline, not a line of content.
        if ($fileLines.Count -gt 0 -and $fileLines[-1] -eq '') {
            $fileLines = $fileLines[0..($fileLines.Count - 2)]
        }

        # Hash the path too, so moving content between frozen files is a change.
        [void]$contents.Add($file.FullName.Substring($repoRoot.Length).Replace([char]92, [char]47))
        [void]$contents.Add(($fileLines -join "`n"))
        $lines += $fileLines.Count
        foreach ($line in $fileLines) {
            if ($line.Contains(') {')) { $krBraces++ }
            if ($line -match '[ \t]$') { $trailingWs++ }
        }
    }

    # The four counts above are a proxy, and a proxy has blind spots. Re-indenting every
    # line of a file moves none of them: file count, total lines, `) {` count and
    # trailing-whitespace count are all invariant under indentation. Measured: doubling
    # the indentation of SweDate.cs rewrites 568 of its 612 lines and the fingerprint is
    # byte-identical, so the check passed on a file where every content line had changed.
    # That is not a corner case -- indentation normalization is the bulk of what
    # `dotnet format whitespace` does, which is the exact tool this script exists to
    # catch. The hash makes the check exact instead of a four-number approximation.
    #
    # The counts are kept alongside it because they say *what kind* of change happened:
    # a hash mismatch tells you something moved, the counts tell a reviewer whether it
    # was a reflow, a whitespace strip, or added lines.
    $sha = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes(($contents -join "`n")))).Replace('-', '')
    
    [pscustomobject]@{
        Path       = $RelativePath
        Files      = $files.Count
        Lines      = $lines
        KrBraces   = $krBraces
        TrailingWs = $trailingWs
        Sha256     = $sha
    }
}

$current = foreach ($path in $frozenPaths) { Get-Fingerprint -RelativePath $path }

if ($Update) {
    $out = [System.Text.StringBuilder]::new()
    [void]$out.AppendLine('# Structural fingerprint of the transliteration-frozen paths.')
    [void]$out.AppendLine('# Checked by scripts/verify-freeze.ps1; regenerate with -Update.')
    [void]$out.AppendLine('# Update this ONLY alongside an intended change to a frozen file, so')
    [void]$out.AppendLine('# the new counts are reviewed with the change that caused them.')
    [void]$out.AppendLine("path`tfiles`tlines`tkr_braces`ttrailing_ws`tsha256")
    foreach ($row in $current) {
        [void]$out.AppendLine(
            "$($row.Path)`t$($row.Files)`t$($row.Lines)`t$($row.KrBraces)`t$($row.TrailingWs)`t$($row.Sha256)")
    }
    [System.IO.File]::WriteAllText($ManifestPath, $out.ToString())
    Write-Host "Wrote $ManifestPath"
    $current | Format-Table -AutoSize | Out-String | Write-Host
    exit 0
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Write-Host "FAIL: manifest not found at $ManifestPath."
    Write-Host 'Generate it with: pwsh scripts/verify-freeze.ps1 -Update'
    exit 1
}

$expected = @{}
foreach ($line in [System.IO.File]::ReadAllLines($ManifestPath)) {
    if ($line.StartsWith('#') -or $line.Trim() -eq '' -or $line.StartsWith('path`t')) { continue }
    if ($line -like "path`t*") { continue }
    $f = $line -split "`t"
    if ($f.Count -ne 6) {
        Write-Host "FAIL: malformed manifest row: $line"
        exit 1
    }
    $expected[$f[0]] = [pscustomobject]@{
        Files = [int]$f[1]; Lines = [int]$f[2]; KrBraces = [int]$f[3]; TrailingWs = [int]$f[4]; Sha256 = $f[5]
    }
}

$failed = $false
$header = '{0,-30} {1,-12} {2,12} {3,12}' -f 'PATH', 'METRIC', 'EXPECTED', 'ACTUAL'

foreach ($row in $current) {
    $want = $expected[$row.Path]
    if (-not $want) {
        Write-Host "FAIL: $($row.Path) is frozen but has no manifest row."
        $failed = $true
        continue
    }

    foreach ($metric in 'Sha256', 'Files', 'Lines', 'KrBraces', 'TrailingWs') {
        if ($row.$metric -ne $want.$metric) {
            if (-not $failed) { Write-Host $header; Write-Host ('-' * $header.Length) }
            Write-Host ('{0,-30} {1,-12} {2,12} {3,12}' -f
                $row.Path, $metric, $want.$metric, $row.$metric)
            $failed = $true
        }
    }
}

foreach ($path in $expected.Keys) {
    if ($current.Path -notcontains $path) {
        Write-Host "FAIL: manifest lists $path, which no longer resolves to any frozen file."
        $failed = $true
    }
}

if ($failed) {
    Write-Host ''
    Write-Host 'FAIL: a transliteration-frozen path changed shape.'
    Write-Host ''
    Write-Host 'If this was an unexcluded `dotnet format` run, revert it. CONTRIBUTING.md has'
    Write-Host 'the three --exclude flags that must accompany any format command in this repo.'
    Write-Host ''
    Write-Host 'If you deliberately changed a frozen file -- a fidelity fix citing the C, or the'
    Write-Host '2.10.03 re-transliteration -- rerun with -Update and commit the manifest with it.'
    exit 1
}

Write-Host 'PASS: transliteration-frozen paths unchanged.'
$current | Format-Table -AutoSize | Out-String | Write-Host
exit 0
