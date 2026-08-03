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

.PARAMETER RepoRoot
    Repository root holding the frozen paths. Defaults to the checkout containing this script;
    -SelfTest below is the only thing that points it anywhere else.

.PARAMETER ManifestPath
    Defaults to <RepoRoot>/scripts/freeze-manifest.tsv.

.PARAMETER Update
    Rewrite the manifest from the current tree instead of checking against it. Use only when a
    change to a frozen file is intended, and commit the result together with that change.

.PARAMETER SelfTest
    Build a throwaway frozen tree, plant each way a frozen file has been -- or could be -- altered
    without this check noticing, and assert the check's exit code and failure message for each.
    Touches nothing outside a temporary directory, and never writes this repository's own manifest.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $ManifestPath,
    [switch] $Update,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# $repoRoot and $RepoRoot are the same variable: PowerShell variable names are case-insensitive, so
# every existing reference to $repoRoot below now reads the parameter above without being touched.
if (-not $ManifestPath) {
    $ManifestPath = Join-Path (Join-Path $RepoRoot 'scripts') 'freeze-manifest.tsv'
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
        #
        # [Array] casts on both arguments, deliberately: [System.Array]::Sort($keys, $items,
        # comparer), with $keys typed [string[]] and $items [object[]] and no cast, resolves in
        # PowerShell to the generic Array.Sort<TKey,TValue>(TKey[], TValue[], IComparer<TKey>)
        # overload -- confirmed directly (Write-Host on $items before and after the call printed
        # the identical, unsorted sequence both times) -- which sorts $keys but silently leaves
        # $items untouched. So this function never actually returned files in ordinal-sorted
        # order; it returned whatever order Get-ChildItem's OS directory enumeration happened to
        # produce, with no cross-platform guarantee, and the fingerprint below hashed that
        # enumeration order instead of a stable one. Casting both arguments to [Array] forces the
        # non-generic Array.Sort(Array, Array, IComparer) overload instead, which does sort
        # $items in place -- verified directly (a three-element cast-vs-uncast comparison; only
        # the cast form reorders $items to match $keys). The sort key is the repo-relative path
        # with separators normalized to `/`, not $_.FullName: that is the same string $contents
        # below hashes for each file (see its own comment), so the order files are concatenated
        # into the hash matches the order they are keyed by here, independent of whether the
        # absolute path happens to sort identically (it does for every frozen file today, since
        # `\` and `/` both sort above every character any of their names actually contains, but
        # nothing here should depend on that coincidence holding for a future frozen file).
        $found = @(Get-ChildItem -LiteralPath $full -Recurse -File -Force)
        $keys = [string[]] ($found | ForEach-Object { $_.FullName.Substring($repoRoot.Length).Replace([char]92, [char]47) })
        $items = [object[]] $found
        [System.Array]::Sort([Array] $keys, [Array] $items, [System.StringComparer]::Ordinal)
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

# Two invariants checked directly against the raw bytes of every frozen file, independent of
# scripts/freeze-manifest.tsv: nothing about the manifest's stored counts or hash needs to change
# for these to apply, because both are asserted as fixed rules (verified against the tree as it
# stands today, see the plant/fix table this was proven with) rather than diffed against a golden
# snapshot. That matters here specifically because the alternative -- folding an encoding tag and
# a final-newline flag into the hashed content that already feeds Sha256 -- would move every
# existing row's stored hash and require regenerating scripts/freeze-manifest.tsv to match, which
# this script's own manifest is not touched for a check addition alone.
#
# 1. Every frozen file must end with exactly one trailing newline (`insert_final_newline`, one of
#    the two .editorconfig pins this script's own header already calls a defense). Verified
#    directly against the byte stream: File.ReadAllText + a text split, the way the four proxy
#    counts below are computed, throws this signal away entirely -- "a\nb\n" and "a\nb" both split
#    to ["a","b"] once the trailing empty element is discarded, so a stripped final newline left
#    every count (and, before this check existed, the hash) byte-identical. Measured directly: yes.
# 2. Every frozen file's encoding matches what is on disk today: a UTF-8 BOM (EF BB BF) for every
#    frozen *.cs file, no BOM at all for CPort/.editorconfig, the one frozen file that is not *.cs.
#    Verified across all 19 frozen files before writing this rule, not assumed. File.ReadAllText
#    silently decodes UTF-8 (with or without a BOM) and UTF-16LE/BE (with a BOM) to the identical
#    in-memory string, so re-encoding a frozen file to UTF-16LE (its on-disk size doubles) or
#    stripping its UTF-8 BOM previously left every downstream count, and the hash, unchanged too.
function Test-FrozenFileInvariant {
    param([System.IO.FileInfo] $File, [System.Collections.Generic.List[string]] $Violations)

    $relPath = $File.FullName.Substring($repoRoot.Length).Replace([char]92, [char]47)
    $bytes = [System.IO.File]::ReadAllBytes($File.FullName)

    if ($bytes.Length -eq 0 -or $bytes[$bytes.Length - 1] -ne 0x0A) {
        $Violations.Add("$relPath does not end with a trailing newline (insert_final_newline violation).")
    }

    $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $hasUtf16Bom = $bytes.Length -ge 2 -and (
        ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))
    $expectUtf8Bom = $File.Extension -eq '.cs'

    if ($hasUtf16Bom) {
        $Violations.Add("$relPath is UTF-16 encoded (a UTF-16LE/BE byte-order mark was found); every frozen file must stay UTF-8.")
    }
    elseif ($expectUtf8Bom -and -not $hasUtf8Bom) {
        $Violations.Add("$relPath is a frozen *.cs file with no UTF-8 BOM (the BOM was stripped, or it was re-saved without one).")
    }
    elseif (-not $expectUtf8Bom -and $hasUtf8Bom) {
        $Violations.Add("$relPath unexpectedly carries a UTF-8 BOM; every frozen non-*.cs file (CPort/.editorconfig today) carries none.")
    }
}

$encodingViolations = [System.Collections.Generic.List[string]]::new()

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
        Test-FrozenFileInvariant -File $file -Violations $encodingViolations

        # Read raw so line-ending normalization cannot mask a change, and split on both
        # conventions: these files are CRLF in the repo but a contributor's tooling may not be.
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $fileLines = $text -split "`r`n|`n|`r"

        # A trailing empty element is the artifact of a final newline, not a line of content. Its
        # *presence or absence* is exactly what Test-FrozenFileInvariant checks above, directly
        # against the raw bytes and independent of this hash -- discarding it here only trims the
        # $fileLines/$lines/$krBraces/$trailingWs proxies back to counting real content lines, the
        # same as before; it is not this function's job to also catch its removal.
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

function Write-FreezeManifest {
    # The manifest format, in one place. -Update below writes the real manifest with it; -SelfTest
    # at the bottom writes a throwaway one for its own temporary tree with the same function, so the
    # self-test never needs to invoke -Update against anything.
    param([object[]] $Rows, [string] $Path)

    $out = [System.Text.StringBuilder]::new()
    [void]$out.AppendLine('# Structural fingerprint of the transliteration-frozen paths.')
    [void]$out.AppendLine('# Checked by scripts/verify-freeze.ps1; regenerate with -Update.')
    [void]$out.AppendLine('# Update this ONLY alongside an intended change to a frozen file, so')
    [void]$out.AppendLine('# the new counts are reviewed with the change that caused them.')
    [void]$out.AppendLine("path`tfiles`tlines`tkr_braces`ttrailing_ws`tsha256")
    foreach ($row in $Rows) {
        [void]$out.AppendLine(
            "$($row.Path)`t$($row.Files)`t$($row.Lines)`t$($row.KrBraces)`t$($row.TrailingWs)`t$($row.Sha256)")
    }
    [System.IO.File]::WriteAllText($Path, $out.ToString())
}

function Invoke-FreezeCheck {
    # The whole check, in a function so -SelfTest below can drive it against a throwaway frozen tree.
    # The `exit` statements inside are unchanged and still terminate the script, so CI sees exactly
    # the codes it saw before.
    param([string] $ManifestPath, [switch] $Update)

    $current = foreach ($path in $frozenPaths) { Get-Fingerprint -RelativePath $path }

    if ($encodingViolations.Count -gt 0) {
        Write-Host ''
        foreach ($violation in $encodingViolations) { Write-Host "  $violation" }
        Write-Host ''
        Write-Host 'FAIL: a transliteration-frozen file changed encoding or lost its trailing newline.'
        Write-Host 'This is checked directly against every frozen file, independent of scripts/freeze-manifest.tsv --'
        Write-Host 'no manifest update fixes it. Restore the UTF-8 encoding (with a BOM for *.cs files) and the'
        Write-Host 'trailing newline instead.'
        exit 1
    }

    if ($Update) {
        Write-FreezeManifest -Rows @($current) -Path $ManifestPath
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
        # Header skip is the -like check below, which uses a double-quoted "path`t*" so the backtick
        # is interpreted as a real tab. An earlier version of this line also tried a single-quoted
        # $line.StartsWith('path`t'), which PowerShell never interprets as an escape inside single
        # quotes -- it tested for a literal backtick-t sequence that never appears in the manifest, so
        # that clause was dead code doing nothing; the -like check below has always been what actually
        # skips the header row.
        if ($line.StartsWith('#') -or $line.Trim() -eq '') { continue }
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
}

# ---------------------------------------------------------------------------------------------

if (-not $SelfTest) {
    Invoke-FreezeCheck -ManifestPath $ManifestPath -Update:$Update
    # Unreachable: Invoke-FreezeCheck always exits. Present so that a future edit turning one of
    # those exits into a return cannot make this script pass by falling off the end.
    exit 1
}

# ---------------------------------------------------------------------------------------------
# Self-test. Each case is a way a frozen file has been, or could be, altered; each was planted,
# run, and SEEN to produce the stated exit code. Two of them (cases 2 and 4) are changes this
# check demonstrably could NOT see before the byte-level invariants were added, and one (case 8)
# is a change it must keep NOT seeing.
#
# Nothing here runs -Update. The throwaway tree's manifest is written with Write-FreezeManifest
# directly, so this repository's own scripts/freeze-manifest.tsv is never a target.

$failures = 0
$pwshExe = (Get-Process -Id $PID).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("freeze-selftest-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

# The frozen tree every case starts from: two *.cs under the frozen directory plus the
# .editorconfig that lives there, and the two individually-frozen Program.cs files. Written the way
# the real frozen files are on disk -- UTF-8 with a BOM for *.cs, none for .editorconfig, CRLF
# throughout, one trailing newline each -- because those are exactly the properties the byte-level
# invariants assert, and a template that did not have them could not test their removal.
$pristine = Join-Path $root 'pristine'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-LabFile {
    param([string] $Path, [string[]] $Lines, [switch] $NoBom)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $text = ($Lines -join "`r`n") + "`r`n"
    [System.IO.File]::WriteAllText($Path, $text, $(if ($NoBom) { $utf8NoBom } else { $utf8Bom }))
}

function New-FrozenTree {
    param([string] $Dir)
    Write-LabFile (Join-Path $Dir 'SwissEphNet/CPort/SweDate.cs') @(
        'namespace SwissEphNet.CPort {',
        '    internal class SweDate : BaseCPort {',
        '        public int swe_date_conversion(int y, int m, int d) {',
        '            if (y > 0) {',
        '                return 0;',
        '            }',
        '            return -1;',
        '        }',
        '    }',
        '}')
    Write-LabFile (Join-Path $Dir 'SwissEphNet/CPort/Sweph.cs') @(
        'namespace SwissEphNet.CPort {',
        '    internal class Sweph : BaseCPort {',
        '        public int swe_calc(double tjd, int ipl) {',
        '            return 0;',
        '        }',
        '    }',
        '}')
    Write-LabFile -NoBom (Join-Path $Dir 'SwissEphNet/CPort/.editorconfig') @(
        '[*.cs]',
        'trim_trailing_whitespace = true',
        'insert_final_newline = true')
    Write-LabFile (Join-Path $Dir 'Programs/SweTest/Program.cs') @(
        'namespace SweTest {',
        '    class Program {',
        '        static int Main(string[] args) {',
        '            return 0;',
        '        }',
        '    }',
        '}')
    Write-LabFile (Join-Path $Dir 'Programs/SweMini/Program.cs') @(
        'namespace SweMini {',
        '    class Program {',
        '        static int Main(string[] args) {',
        '            return 0;',
        '        }',
        '    }',
        '}')
}

New-FrozenTree -Dir $pristine

# The manifest for that tree, computed with this script's own Get-Fingerprint. $repoRoot is the
# parameter at the top of this file (PowerShell variable names are case-insensitive), and
# Get-FrozenFile reads it, so pointing it at the pristine tree is what makes the fingerprint
# describe that tree rather than this checkout. The stored paths are repo-relative, so the same
# manifest is valid for every per-case copy of the tree below.
$RepoRoot = $pristine
$labManifest = Join-Path $root 'freeze-manifest.tsv'
Write-FreezeManifest -Rows @(foreach ($path in $frozenPaths) { Get-Fingerprint -RelativePath $path }) -Path $labManifest

function New-FreezeLab {
    # A fresh copy of the pristine tree, so a plant in one case cannot leak into the next.
    param([string] $Name)
    $dir = Join-Path $root $Name
    New-FrozenTree -Dir $dir
    return $dir
}

function Assert-Gate {
    # Runs this script's own normal path in a CHILD process, the way CI invokes it, and asserts the
    # exit code -- read straight from $LASTEXITCODE with no pipeline in between, which would report
    # the pipe's last stage instead of the gate's own code.
    #
    # -Matching additionally requires the failure output to say what the case claims it says. That
    # distinction is the whole point of cases 2 and 4: both must be reported by the byte-level
    # invariants, which run before the manifest is even read, and a case that only checked the exit
    # code could not tell "the invariant caught it" from "the fingerprint caught it" -- when the
    # documented fact about those two plants is that the fingerprint cannot.
    param(
        [string] $Case,
        [ValidateSet('fails', 'passes')][string] $Expect,
        [string] $LabRoot,
        [string] $Matching)

    $output = & $pwshExe -NoProfile -File $PSCommandPath -RepoRoot $LabRoot -ManifestPath $labManifest *>&1
    $code = $LASTEXITCODE
    $text = (@($output) -join "`n")

    $problem = $null
    if ($Expect -eq 'fails' -and $code -eq 0) { $problem = 'expected the gate to fail, got exit 0' }
    elseif ($Expect -eq 'passes' -and $code -ne 0) { $problem = "expected the gate to pass, got exit $code" }
    elseif ($Matching -and $text -notmatch $Matching) {
        $problem = "gate exited $code as expected, but for the wrong reason: nothing in its output matched /$Matching/"
    }

    if (-not $problem) {
        Write-Host ("  PASS  {0} (gate {1}, exit {2})" -f $Case, $Expect, $code)
    }
    else {
        Write-Host ("  FAIL  {0}`n          {1}" -f $Case, $problem)
        foreach ($line in @($output)) { Write-Host "            | $line" }
        $script:failures++
    }
}

# The fingerprint-mismatch message, shared by the cases the manifest comparison is supposed to
# catch. It is deliberately NOT what cases 2, 3 and 4 expect.
$changedShape = 'a transliteration-frozen path changed shape'

function Get-LabFrozenFile {
    param([string] $LabRoot)
    return @(Get-ChildItem -LiteralPath $LabRoot -Recurse -File -Force)
}

Write-Host 'verify-freeze self-test'
Write-Host ''

# 1. The change the fingerprint exists to catch at all: frozen content edited.
$lab = New-FreezeLab 'content-change'
$target = Join-Path $lab 'SwissEphNet/CPort/SweDate.cs'
$text = [System.IO.File]::ReadAllText($target)
[System.IO.File]::WriteAllText($target, $text.Replace('return -1;', 'return -2;'), $utf8Bom)
Assert-Gate 'a content change inside a frozen file' 'fails' $lab -Matching $changedShape

# 2. The trailing newline stripped from EVERY frozen file. This is invisible to all four proxy
#    counts and to the hash: File.ReadAllText plus a text split throws the signal away, because
#    "a\nb\n" and "a\nb" both split to ["a","b"] once the trailing empty element is discarded. Only
#    the byte-level invariant sees it. Stripping it from every file at once, rather than one, is
#    the point -- the fingerprint stays byte-identical across the whole tree.
$lab = New-FreezeLab 'stripped-final-newline'
foreach ($file in Get-LabFrozenFile $lab) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    while ($bytes.Length -gt 0 -and ($bytes[$bytes.Length - 1] -eq 0x0A -or $bytes[$bytes.Length - 1] -eq 0x0D)) {
        $bytes = $bytes[0..($bytes.Length - 2)]
    }
    [System.IO.File]::WriteAllBytes($file.FullName, $bytes)
}
Assert-Gate 'the trailing newline stripped from every frozen file' 'fails' $lab -Matching 'does not end with a trailing newline'

# 3. A frozen file re-encoded to UTF-16LE. Its size doubles on disk, and File.ReadAllText decodes
#    it back to the identical in-memory string, so every count and the hash were unchanged.
$lab = New-FreezeLab 'utf16-reencode'
$target = Join-Path $lab 'SwissEphNet/CPort/Sweph.cs'
[System.IO.File]::WriteAllText($target, [System.IO.File]::ReadAllText($target), [System.Text.Encoding]::Unicode)
Assert-Gate 'a frozen file re-encoded to UTF-16LE' 'fails' $lab -Matching 'is UTF-16 encoded'

# 4. The UTF-8 BOM stripped from a frozen *.cs file -- same blind spot as case 3 from the other
#    direction: ReadAllText decodes UTF-8 with or without a BOM to the same string.
$lab = New-FreezeLab 'stripped-bom'
$target = Join-Path $lab 'SwissEphNet/CPort/Sweph.cs'
[System.IO.File]::WriteAllText($target, [System.IO.File]::ReadAllText($target), $utf8NoBom)
Assert-Gate 'the UTF-8 BOM stripped from a frozen *.cs file' 'fails' $lab -Matching 'no UTF-8 BOM'

# 5. A rename whose ONLY change is capitalisation. core.ignorecase is true in this repository's
#    checkouts, which is how a frozen file has been silently re-cased before; the hash covers each
#    file's path precisely so that a re-case is a change rather than a no-op. The rename goes via a
#    third name because a case-insensitive filesystem rejects a direct A -> a rename as "already
#    exists".
$lab = New-FreezeLab 'case-only-rename'
$dir = Join-Path $lab 'SwissEphNet/CPort'
Rename-Item -LiteralPath (Join-Path $dir 'SweDate.cs') -NewName 'SweDate.cs.tmp'
Rename-Item -LiteralPath (Join-Path $dir 'SweDate.cs.tmp') -NewName 'swedate.cs'
Assert-Gate 'a frozen file renamed by capitalisation alone' 'fails' $lab -Matching $changedShape

# 6. A file added under a frozen path. A directory in $frozenPaths means everything under it, so a
#    new file is covered automatically instead of escaping the freeze.
$lab = New-FreezeLab 'added-file'
Write-LabFile (Join-Path $lab 'SwissEphNet/CPort/SweHouse.cs') @('namespace SwissEphNet.CPort {', '}')
Assert-Gate 'a file added under a frozen path' 'fails' $lab -Matching $changedShape

# 7. A file deleted from a frozen path.
$lab = New-FreezeLab 'deleted-file'
Remove-Item -LiteralPath (Join-Path $lab 'SwissEphNet/CPort/Sweph.cs') -Force
Assert-Gate 'a file deleted from a frozen path' 'fails' $lab -Matching $changedShape

# 8. CRLF rewritten to LF across every frozen file MUST STAY INVISIBLE. That is deliberate: the
#    fingerprint splits on both conventions and joins with "`n" precisely so a contributor whose
#    tooling normalizes line endings does not trip a freeze violation, and so the hash means the
#    same thing on the Windows and ubuntu jobs. This case is here to fail loudly if some future
#    change makes line endings significant -- at which point the manifest becomes platform-specific
#    and the ubuntu job starts reporting a violation nobody introduced.
$lab = New-FreezeLab 'crlf-to-lf'
foreach ($file in Get-LabFrozenFile $lab) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    [System.IO.File]::WriteAllBytes($file.FullName, [byte[]] @($bytes | Where-Object { $_ -ne 0x0D }))
}
Assert-Gate 'CRLF rewritten to LF (must stay invisible by design)' 'passes' $lab

# 9. The unmodified tree. Without this every case above could be satisfied by a check that fails
#    on everything.
Assert-Gate 'the unmodified frozen tree' 'passes' (New-FreezeLab 'unmodified')

Write-Host ''
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue

if ($failures -gt 0) {
    Write-Host "FAIL: $failures self-test case(s) failed."
    exit 1
}
Write-Host 'PASS: all verify-freeze self-test cases passed.'
exit 0
