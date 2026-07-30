#Requires -Version 7.3
<#
.SYNOPSIS
    Text-level comparison between Astrodienst's swetest and this port's SweTest -- the only check
    in this repo that looks at a single formatted output character.

.DESCRIPTION
    scripts/verify-baseline.ps1 and scripts/verify-oracle.ps1 both compare doubles: bit patterns,
    ULP distances, tolerances. Neither one ever runs Programs/SweTest, and neither one would notice
    if print_line printed a value in the wrong column, dropped a decimal place, or mis-spelled a
    house-system label. Programs/SweTest/Program.cs is also a frozen, line-by-line transliteration
    (CONTRIBUTING.md, "Transliterated files must never be reformatted") of swetest.c, the single
    largest file still to port to 2.10.03 -- a regression there is both easy to introduce and, until
    this script existed, invisible to every other gate in this repo.

    This runs every row of Tools/SwetestDiff/args-grid.tsv through both
    external/.c-reference/swetest.exe (Astrodienst's own C, MSVC-built by Tools/CReference/build-
    c.ps1, linked against 2.10.03) and Programs/SweTest (this port, built here), and diffs their
    combined stdout+stderr line by line. Differences are expected, not a failure by themselves: the
    port is at 2.08, the C reference is 2.10.03, exactly the situation
    Tests/SwissEphNet.Conformance.Tests already lives in. What gates is Tests/swetest/known-diff.tsv
    -- every case_id that differs must be listed there under a category that still matches; an
    unlisted difference is a regression, a listed case_id that now matches must be pruned, and a
    listed case_id absent from the current grid is stale. This mirrors scripts/verify-oracle.ps1's
    three-way check, but on formatted text instead of raw hex doubles, so there is no ULP distance
    to compare -- see Tests/swetest/known-diff.tsv's own header for why category is the unit of
    comparison here, the same choice Tests/conformance/known-fail.tsv makes for the same reason.

    NEITHER BINARY IS BUILT SILENTLY WRONG

    A missing or wrong -edir does not fail swe_calc -- it falls back to Moshier and both binaries
    print a plausible-looking value that differs from the real-ephemeris one only in the last
    printed digit (Tools/CReference/build-c.ps1's own Invoke-SwetestSmoke measured this: 280.3681656
    real vs 280.3681666 Moshier-fallback at JD 2451545.0). This script asserts the eight-file set
    Tests/conformance/required-ephemeris-files.tsv declares is present in external/swisseph/ephe --
    the same manifest scripts/run-oracle-dump.ps1 and Tools/CReference/build-c.ps1 check -- before
    running a single case, and points both binaries at that directory with -edir. Two things make
    that assertion trustworthy rather than decorative:

    First, -edir is always an absolute, resolved path (Resolve-EpheDir below), whether $EpheDir came
    from the default or a caller's override. Astrodienst's swetest reads a relative -edir correctly;
    Programs/SweTest/Program.cs's sweph_OnLoadFile handler does not (it is a frozen transliteration,
    out of scope to fix here -- see CONTRIBUTING.md). e.FileName there is already the full path
    swi_fopen built from ephepath plus the file name, and the handler then runs it through
    Path.Combine(ephepath, e.FileName) a second time. Path.Combine only discards the first argument
    when the second is rooted, which a relative ephepath makes it not. Confirmed by hand before this
    comment was written: -b1.1.2000 -p0 -fPl -eswe against a relative -edir prints "using Moshier
    eph." and Sun 279.8584626 on the .NET side while the C reference, same relative -edir, reads the
    real file and prints 279.8584613 -- the same value both sides give with an absolute -edir.
    Passing a relative directory here would silently compare a real-ephemeris C run against a
    Moshier-fallback .NET run on every row.

    Second, Test-RequiredFileMissReported below scans both binaries' output for "SwissEph file 'X'
    not found" naming one of the eight required, just-confirmed-present files, and aborts the whole
    run the moment it sees one -- that combination is only possible when a binary failed to read a
    file this script already proved was on disk, i.e. a path-resolution defect, not a data gap. It
    does not fire on a missing file outside the required set (an extended-range file like
    seplm06.se1, or an unshipped asteroid file): both binaries fall back to Moshier there for a real,
    shared reason -- the declared ephemeris set genuinely does not cover it -- and print matching
    values (confirmed: -b1.1.-100 gives Sun 277.7544113 on both sides, "using Moshier eph." visible
    on both, not a divergence to hide). Asserting file presence alone (the previous form of this
    guard) caught neither failure mode: presence says nothing about whether the file was actually
    read. $EpheDir's default already happened to resolve absolute before this change (it is built
    from $repoRoot, itself a Resolve-Path result), so re-running the existing grid against it does
    not, by itself, turn up rows that were secretly reading Moshier -- the exposure was a caller
    passing -EpheDir a relative path, which nothing here checked or corrected.

.PARAMETER Regenerate
    Rewrites Tests/swetest/known-diff.tsv from the current comparison run and appends a dated entry
    to Tests/swetest/regenerations.log recording the row-count change and -Reason. This is the only
    supported way to change known-diff.tsv -- never hand-edit it. Requires -Reason. This is also the
    gate's own bypass (a regenerate can silently turn a real regression into "the file now says this
    is expected"), so a regenerate should be reviewed the same way a change to
    Tests/conformance/known-fail.tsv is.

.PARAMETER Reason
    Required with -Regenerate. Short prose explaining what changed and why -- becomes the log entry
    in Tests/swetest/regenerations.log.

.PARAMETER PR
    Optional with -Regenerate. PR number to cite in the log entry. Leave blank and fill in the
    logged line by hand once the PR number is known, before it merges -- the commit carrying this
    change does not exist yet while the branch is open, so no other identifier survives past merge.

.PARAMETER GridPath
    The args grid to run. Defaults to Tools/SwetestDiff/args-grid.tsv.

.PARAMETER KnownDiffPath
    Defaults to Tests/swetest/known-diff.tsv.

.PARAMETER CExePath
    Defaults to external/.c-reference/swetest.exe (Tools/CReference/build-c.ps1's output).

.PARAMETER EpheDir
    Defaults to external/swisseph/ephe. Resolved to an absolute path (Resolve-EpheDir) before
    being passed to either binary as -edir, whether it came from this default or an override --
    see "NEITHER BINARY IS BUILT SILENTLY WRONG" above for why a relative directory would be a
    silent Moshier-fallback defect on the .NET side.
#>
[CmdletBinding()]
param(
    [switch] $Regenerate,
    [string] $Reason,
    [string] $PR,
    [string] $GridPath,
    [string] $KnownDiffPath,
    [string] $CExePath,
    [string] $EpheDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# cmd and dotnet below are both native commands; see Tools/CReference/build-c.ps1's own copy of
# this line for why it is set even though it changes nothing under the pwsh version this was
# written against.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $GridPath) { $GridPath = Join-Path $repoRoot 'Tools/SwetestDiff/args-grid.tsv' }
if (-not $KnownDiffPath) { $KnownDiffPath = Join-Path $repoRoot 'Tests/swetest/known-diff.tsv' }
if (-not $CExePath) { $CExePath = Join-Path $repoRoot 'external/.c-reference/swetest.exe' }
if (-not $EpheDir) { $EpheDir = Join-Path $repoRoot 'external/swisseph/ephe' }
$logPath = Join-Path $repoRoot 'Tests/swetest/regenerations.log'
$netCsproj = Join-Path $repoRoot 'Programs/SweTest/SweTest.csproj'
$netExePath = Join-Path $repoRoot 'Programs/SweTest/bin/Release/net10.0/SweTest.exe'

if ($Regenerate -and -not $Reason) {
    Write-Host 'FAIL: -Regenerate requires -Reason.' -ForegroundColor Red
    exit 1
}

function Fail($message) {
    # Thrown, not exited: matches scripts/verify-crt-parity.ps1 and Tools/CReference/build-c.ps1's
    # own convention, so a failure part-way through still reaches the single top-level catch below
    # and gets the same "FAIL: ..." banner.
    throw $message
}

# ---------------------------------------------------------------------------------------
# Grid loading -- same comment/header/data shape as Tools/OracleGrid's grids
# (scripts/run-oracle-dump.ps1's Get-GridDataRowCount), reimplemented here rather than shared
# since this script has no other reason to depend on that one.
# ---------------------------------------------------------------------------------------

function Read-ArgsGrid {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail "Args grid not found at $Path. Run: pwsh Tools/SwetestDiff/gen-args-grid.ps1"
    }
    $expectedHeader = 'case_id' + "`t" + 'category' + "`t" + 'args'
    $headerSeen = $false
    $result = [System.Collections.Generic.List[pscustomobject]]::new()
    $seenIds = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($textLine in [System.IO.File]::ReadLines($Path)) {
        if ($textLine.Length -eq 0) { continue }
        if ($textLine[0] -eq '#') { continue }
        if (-not $headerSeen) {
            if ($textLine -ne $expectedHeader) {
                Fail "$Path's column header is '$textLine', expected '$expectedHeader'. Was this hand-edited instead of regenerated by Tools/SwetestDiff/gen-args-grid.ps1?"
            }
            $headerSeen = $true
            continue
        }
        $fields = $textLine -split "`t"
        if ($fields.Count -ne 3) {
            Fail "$Path has a data row with $($fields.Count) field(s), expected 3: '$textLine'"
        }
        $caseId = $fields[0]
        if (-not $seenIds.Add($caseId)) {
            Fail "$Path has a duplicate case_id: '$caseId'"
        }
        $result.Add([pscustomobject]@{ CaseId = $caseId; Category = $fields[1]; Args = $fields[2] })
    }
    if (-not $headerSeen) { Fail "$Path has no header row." }
    if ($result.Count -eq 0) { Fail "$Path contains zero data rows." }
    return $result
}

# ---------------------------------------------------------------------------------------
# known-diff.tsv -- same shape as Tests/conformance/known-fail.tsv: case_id, category, reason.
# Gates on category only, not on the reason text, matching that file's own design (see
# CONTRIBUTING.md, "Correctness oracle known-fail list") -- reason is documentation, not part of
# the comparison.
# ---------------------------------------------------------------------------------------

function Read-KnownDiff {
    param([string] $Path)
    $expectedHeader = 'case_id' + "`t" + 'category' + "`t" + 'reason'
    $result = [System.Collections.Generic.Dictionary[string, pscustomobject]]::new()
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $result
    }
    $headerSeen = $false
    foreach ($textLine in [System.IO.File]::ReadLines($Path)) {
        if ($textLine.Length -eq 0) { continue }
        if ($textLine[0] -eq '#') { continue }
        if (-not $headerSeen) {
            if ($textLine -ne $expectedHeader) {
                Fail "$Path's column header is '$textLine', expected '$expectedHeader'."
            }
            $headerSeen = $true
            continue
        }
        $spi = $textLine.IndexOf("`t")
        $sp2i = $textLine.IndexOf("`t", $spi + 1)
        if ($spi -lt 0 -or $sp2i -lt 0) {
            Fail "$Path has a malformed row (expected 3 tab-separated fields): '$textLine'"
        }
        $caseId = $textLine.Substring(0, $spi)
        $category = $textLine.Substring($spi + 1, $sp2i - $spi - 1)
        $reasonField = $textLine.Substring($sp2i + 1)
        if ($result.ContainsKey($caseId)) {
            Fail "$Path has a duplicate case_id: '$caseId'"
        }
        $result[$caseId] = [pscustomobject]@{ Category = $category; Reason = $reasonField }
    }
    return $result
}

# ---------------------------------------------------------------------------------------
# -edir must be absolute. Programs/SweTest/Program.cs's sweph_OnLoadFile (a frozen
# transliteration -- see this script's own .DESCRIPTION, "NEITHER BINARY IS BUILT SILENTLY WRONG")
# double-combines a relative ephepath and misses every file, falling back to Moshier instead of
# failing. Astrodienst's swetest has no such bug, so a relative -edir would compare a real-
# ephemeris C run against a Moshier-fallback .NET run without either binary raising an error.
# Applied to $EpheDir unconditionally, whether it came from the default or -EpheDir override, so
# a caller cannot reintroduce the relative case by supplying their own path.
# ---------------------------------------------------------------------------------------

function Resolve-EpheDir {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        Fail "$Path does not exist. Run the sparse-checkout recipe in CONTRIBUTING.md's `"The upstream C is vendored at external/swisseph`" section."
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

# ---------------------------------------------------------------------------------------
# Ephemeris manifest -- same two-way check as scripts/run-oracle-dump.ps1's
# Assert-EphemerisManifest, reimplemented here for the same reason that script gives for its own
# copy: no other shared dependency justifies referencing it instead. Returns the required-file
# list on success so the caller can reuse it for Test-RequiredFileMissReported below, rather than
# re-reading $ManifestPath a second time.
# ---------------------------------------------------------------------------------------

function Assert-EphemerisManifest {
    param([string] $ManifestPath, [string] $EpheDirPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        Fail "Required ephemeris file list not found at $ManifestPath."
    }
    $required = @(Get-Content -LiteralPath $ManifestPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') })
    if ($required.Count -eq 0) {
        Fail "$ManifestPath parsed to zero required files."
    }
    if (-not (Test-Path -LiteralPath $EpheDirPath -PathType Container)) {
        Fail "$EpheDirPath does not exist. Run the sparse-checkout recipe in CONTRIBUTING.md's `"The upstream C is vendored at external/swisseph`" section."
    }
    $present = @{}
    Get-ChildItem -LiteralPath $EpheDirPath -Force | ForEach-Object {
        $name = if ($_.PSIsContainer) { "$($_.Name)/" } else { $_.Name }
        $present[$name] = $true
    }
    $requiredSet = @{}
    foreach ($r in $required) { $requiredSet[$r.ToLowerInvariant()] = $true }
    $missing = @($required | Where-Object { -not $present.ContainsKey($_) })
    $extra = @($present.Keys | Where-Object { -not $requiredSet.ContainsKey($_.ToLowerInvariant()) } | Sort-Object)
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host "PASS: $EpheDirPath matches the declared ephemeris file set ($($required.Count) file(s))." -ForegroundColor Green
        return $required
    }
    $message = "$EpheDirPath does not match the declared ephemeris file set ($ManifestPath).`n"
    if ($missing.Count -gt 0) { $message += "Missing ($($missing.Count)): $($missing -join ', ')`n" }
    if ($extra.Count -gt 0) { $message += "Extra ($($extra.Count)): $($extra -join ', ')`n" }
    Fail $message
}

# ---------------------------------------------------------------------------------------
# Running a single case. Built as one string and invoked via cmd /c, never as a PowerShell native
# argument array -- PowerShell's own argument parser mangles swetest-style option syntax like
# "-b1.1.2000" and "-edir<path>" (it tries to interpret the leading "-" as a PowerShell parameter
# and the embedded "." as something to tokenize), so it has to go through cmd's own tokenizer
# instead. Verified against real output before this harness was built on top of it, the same
# lesson Tools/CReference/build-c.ps1's Invoke-SwetestSmoke and this script's own EpheDir check
# above already encode.
# ---------------------------------------------------------------------------------------

function Invoke-SwetestCase {
    param([string] $ExePath, [string] $ArgsStr, [string] $EpheDirPath)
    # $ArgsStr already carries -head (or deliberately does not, for HEADER_BLOCK) -- see
    # Tools/SwetestDiff/gen-args-grid.ps1's Add-Row and its own .DESCRIPTION on why that has to be
    # baked into the grid row rather than added here uniformly: doing it here once made
    # HEADER_BLOCK indistinguishable from every other category, since -head would have applied to
    # it too. -edir is the one thing this function still appends itself, since the ephemeris path
    # is machine-specific and does not belong in committed grid data, matching
    # Invoke-SwetestSmoke's own ordering (args first, -edir last).
    $full = "`"$ExePath`" $ArgsStr -edir`"$EpheDirPath`""
    $lines = @(cmd /c $full 2>&1 | ForEach-Object { $_.ToString() })
    $lines = @(Format-ArgvEchoLine -Lines $lines)
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}

# with_header (swetest's default, suppressed by -head -- see the grid generator's .DESCRIPTION on
# why HEADER_BLOCK is the one category that leaves -head out) echoes argv[0] first: the exe's own
# path on the C side, and, on the .NET side, the managed .dll path the apphost actually reports in
# Environment.GetCommandLineArgs()[0] rather than the SweTest.exe that was invoked. Confirmed by
# running both binaries with no -head: line 1 was
# "...\external\.c-reference\swetest.exe -b1.1.2000 ... " on the C side and
# "...\Programs\SweTest\bin\Release\net10.0\SweTest.dll -b1.1.2000 ... " on the .NET side -- same
# trailing arguments, different leading path and different file extension, for reasons that have
# nothing to do with swetest's own output formatting (a different checkout path, and a runtime
# detail of how a .NET apphost reports its own argv[0], not something print_line controls). This
# is the one, explicit, documented normalization this harness performs: replace the leading
# "<path>.exe"/"<path>.dll" token on a recognizable argv-echo line with a fixed placeholder, on
# both sides, before any comparison runs. It only ever fires for HEADER_BLOCK, since every other
# category's rows carry -head and never print this line at all -- see Tools/SwetestDiff/
# gen-args-grid.ps1's HEADER_BLOCK section for why that category exists.
function Format-ArgvEchoLine {
    param([string[]] $Lines)
    if ($Lines.Count -eq 0) { return $Lines }
    $result = [string[]]$Lines.Clone()
    if ($result[0] -match '^\S+\.(exe|dll)\b') {
        $result[0] = '<swetest-executable-path>' + $result[0].Substring($Matches[0].Length)
    }
    return $result
}

# ---------------------------------------------------------------------------------------
# The Moshier-fallback guard. Assert-EphemerisManifest already proved every file in
# $RequiredFiles is on disk in $EpheDir before the first case ran, so a "SwissEph file 'X' not
# found" line naming one of those files during an actual run cannot be a data gap -- it can only
# mean the binary that printed it failed to read a file this script already confirmed was there,
# the path-resolution defect this guard exists to catch (see this script's own .DESCRIPTION).
# Deliberately does not fire on a missing file outside $RequiredFiles (an extended-range file
# like seplm06.se1, or an unshipped asteroid file): both binaries fall back to Moshier there for a
# real, shared reason, and print matching values -- recording that as a normal diff, the way
# DATE_FORMATS|NEGATIVE_YEAR and STAR_ASTEROID_MISSING already do, is correct, not a gap in the
# guard.
# ---------------------------------------------------------------------------------------

function Test-RequiredFileMissReported {
    param([string[]] $Lines, [System.Collections.Generic.HashSet[string]] $RequiredFiles)
    foreach ($line in $Lines) {
        if ($line -match "SwissEph file '([^']+)' not found") {
            $missingFile = $Matches[1].ToLowerInvariant()
            if ($RequiredFiles.Contains($missingFile)) {
                return $Matches[1]
            }
        }
    }
    return $null
}

# ---------------------------------------------------------------------------------------
# Comparison and categorization.
# ---------------------------------------------------------------------------------------

# .NET's own unhandled-exception exit code (0xE0434352, "ECD3" as ASCII-adjacent bytes, signed
# int32) is one recognizable shape; any other non-zero exit alongside "Unhandled exception." in the
# captured text is treated the same way. Native swetest.exe returning non-zero is a real crash on
# that side too, not assumed to be .NET-only.
function Test-Crashed {
    param([pscustomobject] $Result)
    if ($Result.ExitCode -ne 0) { return $true }
    return ($Result.Lines -join "`n") -match 'Unhandled exception\.'
}

# The first "Unhandled exception." line plus the exception type/message line that follows it, with
# every subsequent " at ..." stack frame (which carries a machine-local absolute path) dropped.
# This is what makes a CRASH row's reason column portable across machines -- see
# Tests/swetest/known-diff.tsv's own header for why an absolute path in a committed reason field
# would be a problem.
function Get-CrashSummary {
    param([pscustomobject] $Result)
    $kept = [System.Collections.Generic.List[string]]::new()
    $started = $false
    foreach ($line in $Result.Lines) {
        if (-not $started) {
            if ($line -match 'Unhandled exception\.') { $started = $true; $kept.Add($line.Trim()) }
            continue
        }
        if ($line.TrimStart().StartsWith('at ')) { break }
        if ($line.Trim().Length -eq 0) { continue }
        $kept.Add($line.Trim())
    }
    if ($kept.Count -eq 0) {
        return "exit code $($Result.ExitCode)"
    }
    return ($kept -join ' ')
}

function Get-FirstDiffSummary {
    param([string[]] $CLines, [string[]] $NetLines)
    $max = [Math]::Max($CLines.Count, $NetLines.Count)
    for ($i = 0; $i -lt $max; $i++) {
        $c = if ($i -lt $CLines.Count) { $CLines[$i] } else { '<no line>' }
        $n = if ($i -lt $NetLines.Count) { $NetLines[$i] } else { '<no line>' }
        if ($c -ne $n) {
            $maxLen = 120
            if ($c.Length -gt $maxLen) { $c = $c.Substring(0, $maxLen) + '...' }
            if ($n.Length -gt $maxLen) { $n = $n.Substring(0, $maxLen) + '...' }
            $countNote = if ($CLines.Count -ne $NetLines.Count) { " ($($CLines.Count) C line(s) vs $($NetLines.Count) .NET line(s))" } else { '' }
            return "line $($i + 1)$($countNote): c=`"$c`" net=`"$n`""
        }
    }
    return 'outputs differ but no differing line was found -- this is a bug in this script, not a real result'
}

# Detects a difference that is fully explained by the embedded "version 2.08" / "version 2.10.03"
# banner text (see Tools/SwetestDiff/gen-args-grid.ps1's .DESCRIPTION on why HEADER_BLOCK is the
# only category that can produce this): if substituting one version string for the other in the
# C side's text makes it byte-identical to the .NET side, the entire observed difference is that
# substitution and nothing else.
function Test-PureVersionDifference {
    param([string[]] $CLines, [string[]] $NetLines)
    if ($CLines.Count -ne $NetLines.Count) { return $false }
    $cText = ($CLines -join "`n").Replace('version 2.10.03', 'version 2.08')
    $netText = ($NetLines -join "`n")
    return $cText -eq $netText
}

function Compare-Case {
    param([pscustomobject] $CResult, [pscustomobject] $NetResult)

    if (($CResult.Lines -join "`n") -eq ($NetResult.Lines -join "`n")) {
        return $null
    }

    $cCrashed = Test-Crashed -Result $CResult
    $netCrashed = Test-Crashed -Result $NetResult
    if ($cCrashed -ne $netCrashed) {
        $reason = if ($netCrashed) {
            ".NET crashed, C did not: $(Get-CrashSummary -Result $NetResult)"
        }
        else {
            "C crashed, .NET did not: $(Get-CrashSummary -Result $CResult)"
        }
        return [pscustomobject]@{ Category = 'CRASH'; Reason = $reason }
    }

    if (Test-PureVersionDifference -CLines $CResult.Lines -NetLines $NetResult.Lines) {
        return [pscustomobject]@{ Category = 'PORT-VERSION'; Reason = 'entire difference is the embedded swe_version() banner (2.08 vs 2.10.03); every other line matches' }
    }

    $summary = Get-FirstDiffSummary -CLines $CResult.Lines -NetLines $NetResult.Lines
    return [pscustomobject]@{ Category = 'OUTPUT-DIFFERS'; Reason = $summary }
}

# =========================================================================================
$exitCode = 0
try {
    # Resolved to an absolute path before anything else runs, whether $EpheDir came from the
    # default above or a caller's -EpheDir override -- see Resolve-EpheDir's own comment for why a
    # relative -edir is a silent Moshier-fallback defect on the .NET side and not on the C side.
    $EpheDir = Resolve-EpheDir -Path $EpheDir

    Write-Host "Grid:          $GridPath"
    Write-Host "C reference:   $CExePath"
    Write-Host "Ephemeris dir: $EpheDir"
    Write-Host "Known-diff:    $KnownDiffPath"
    Write-Host ''

    if (-not (Test-Path -LiteralPath $CExePath -PathType Leaf)) {
        Fail "C reference swetest.exe not found at $CExePath. Run: pwsh Tools/CReference/build-c.ps1"
    }

    $requiredFilesManifest = Join-Path $repoRoot 'Tests/conformance/required-ephemeris-files.tsv'
    $requiredFiles = Assert-EphemerisManifest -ManifestPath $requiredFilesManifest -EpheDirPath $EpheDir
    $requiredFilesSet = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($f in $requiredFiles) { [void]$requiredFilesSet.Add($f.ToLowerInvariant()) }

    $grid = Read-ArgsGrid -Path $GridPath
    Write-Host "Grid rows:     $($grid.Count)"

    Write-Host 'Building Programs/SweTest (Release)...'
    $buildOutput = & dotnet build $netCsproj -c Release --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        Fail 'dotnet build Programs/SweTest failed.'
    }
    if (-not (Test-Path -LiteralPath $netExePath -PathType Leaf)) {
        Fail "dotnet build reported success but $netExePath does not exist. Is this running on Windows (the apphost .exe is Windows-only)?"
    }
    Write-Host ''

    # -----------------------------------------------------------------------------------
    # Run every case through both binaries.
    # -----------------------------------------------------------------------------------

    $actualDiffs = [System.Collections.Generic.Dictionary[string, pscustomobject]]::new()
    $categoryTally = [System.Collections.Generic.Dictionary[string, pscustomobject]]::new()
    $i = 0
    foreach ($row in $grid) {
        $i++
        if (($i % 50) -eq 0) { Write-Host "  ... $i / $($grid.Count)" -ForegroundColor DarkGray }

        $cResult = Invoke-SwetestCase -ExePath $CExePath -ArgsStr $row.Args -EpheDirPath $EpheDir
        $netResult = Invoke-SwetestCase -ExePath $netExePath -ArgsStr $row.Args -EpheDirPath $EpheDir

        # Guard scoped to rows that actually asked for the real ephemeris (-eswe). -emos rows
        # (PLANETS_MOSEPH) are exempt on purpose: swetest.c's asteroid lookup ignores -edir under
        # -emos and probes its own compiled-in default path instead, a real, symmetric-on-neither-
        # side C quirk unrelated to this defect -- confirmed by hand, -pp -emos at 1.1.1900: the C
        # reference reports seas_18.se1 "not found in PATH '\sweph\ephe\'" (its own hardcoded
        # default, not $EpheDir) and zeroes the six asteroid bodies, while Programs/SweTest reads
        # them via -edir and returns real values. That is a genuine C-vs-port difference for
        # known-diff.tsv to carry as OUTPUT-DIFFERS, not a sign either binary mishandled $EpheDir.
        if ($row.Args -match '-eswe') {
            $cMiss = Test-RequiredFileMissReported -Lines $cResult.Lines -RequiredFiles $requiredFilesSet
            if ($cMiss) {
                Fail "$($row.CaseId): C reference reported required file '$cMiss' not found under $EpheDir, which Assert-EphemerisManifest already confirmed is present. -eswe was requested and the run degraded to Moshier instead of failing outright -- that is a harness defect, not a case result to record."
            }
            $netMiss = Test-RequiredFileMissReported -Lines $netResult.Lines -RequiredFiles $requiredFilesSet
            if ($netMiss) {
                Fail "$($row.CaseId): .NET reported required file '$netMiss' not found under $EpheDir, which Assert-EphemerisManifest already confirmed is present. -eswe was requested and the run degraded to Moshier instead of failing outright -- that is a harness defect, not a case result to record."
            }
        }

        $diff = Compare-Case -CResult $cResult -NetResult $netResult

        if (-not $categoryTally.ContainsKey($row.Category)) {
            $categoryTally[$row.Category] = [pscustomobject]@{ Total = 0; Identical = 0; Differing = 0 }
        }
        $categoryTally[$row.Category].Total++
        if ($null -eq $diff) {
            $categoryTally[$row.Category].Identical++
        }
        else {
            $categoryTally[$row.Category].Differing++
            $actualDiffs[$row.CaseId] = $diff
        }
    }
    Write-Host ''

    # -----------------------------------------------------------------------------------
    # Report the measurement -- unconditionally, in both -Regenerate and gate mode, so a run
    # never has to be re-run under a different flag just to see the breakdown.
    # -----------------------------------------------------------------------------------

    $totalIdentical = ($categoryTally.Values | Measure-Object -Property Identical -Sum).Sum
    $totalDiffering = ($categoryTally.Values | Measure-Object -Property Differing -Sum).Sum
    Write-Host "RESULT: $totalIdentical / $($grid.Count) byte-identical, $totalDiffering differing." -ForegroundColor Cyan
    foreach ($cat in ($categoryTally.Keys | Sort-Object)) {
        $t = $categoryTally[$cat]
        Write-Host ("  {0,-20} {1,4} identical / {2,4} total" -f $cat, $t.Identical, $t.Total)
    }
    $diffCategoryTally = @{}
    foreach ($d in $actualDiffs.Values) {
        if (-not $diffCategoryTally.ContainsKey($d.Category)) { $diffCategoryTally[$d.Category] = 0 }
        $diffCategoryTally[$d.Category]++
    }
    if ($diffCategoryTally.Count -gt 0) {
        Write-Host ''
        Write-Host 'Differing cases by known-diff category:'
        foreach ($cat in ($diffCategoryTally.Keys | Sort-Object)) {
            Write-Host ("  {0,-20} {1}" -f $cat, $diffCategoryTally[$cat])
        }
    }
    Write-Host ''

    # -----------------------------------------------------------------------------------
    # -Regenerate: write known-diff.tsv from $actualDiffs and log it. Never runs the gate below.
    # -----------------------------------------------------------------------------------

    if ($Regenerate) {
        $oldCount = 0
        if (Test-Path -LiteralPath $KnownDiffPath -PathType Leaf) {
            $oldCount = (Read-KnownDiff -Path $KnownDiffPath).Count
        }

        $writer = [System.IO.StreamWriter]::new($KnownDiffPath, $false, [System.Text.UTF8Encoding]::new($false))
        try {
            $writer.NewLine = "`n"
            $writer.WriteLine('case_id' + "`t" + 'category' + "`t" + 'reason')
            foreach ($caseId in ($actualDiffs.Keys | Sort-Object -Culture 'en-US' -CaseSensitive)) {
                $d = $actualDiffs[$caseId]
                $writer.WriteLine($caseId + "`t" + $d.Category + "`t" + $d.Reason)
            }
        }
        finally {
            $writer.Dispose()
        }

        $prText = if ($PR) { "PR #$PR" } else { '(no PR yet -- fill in "PR #N" before merging)' }
        $today = (Get-Date).ToString('yyyy-MM-dd')
        $delta = $actualDiffs.Count - $oldCount
        $deltaText = if ($delta -eq 0) { 'no change in row count' } elseif ($delta -gt 0) { "$delta more row(s)" } else { "$([Math]::Abs($delta)) fewer row(s)" }
        $logLine = "$today $prText ($oldCount -> $($actualDiffs.Count), $deltaText): $Reason"
        [System.IO.File]::AppendAllText($logPath, $logLine + "`n", [System.Text.UTF8Encoding]::new($false))

        Write-Host "PASS: wrote $($actualDiffs.Count) row(s) to $KnownDiffPath (was $oldCount)." -ForegroundColor Green
        Write-Host "Logged to $logPath."
        exit 0
    }

    # -----------------------------------------------------------------------------------
    # Gate: three-way check against Tests/swetest/known-diff.tsv, matching
    # scripts/verify-oracle.ps1's own three-way check in spirit (regression / drift, stale-now-
    # passing, stale-case-id), minus a magnitude comparison -- there is no ULP distance for text.
    # -----------------------------------------------------------------------------------

    $known = Read-KnownDiff -Path $KnownDiffPath
    $gridIds = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($row in $grid) { [void]$gridIds.Add($row.CaseId) }

    $regressions = [System.Collections.Generic.List[string]]::new()
    $drifted = [System.Collections.Generic.List[string]]::new()
    foreach ($caseId in $actualDiffs.Keys) {
        if (-not $known.ContainsKey($caseId)) {
            $regressions.Add("$caseId : $($actualDiffs[$caseId].Category) -- $($actualDiffs[$caseId].Reason)")
        }
        elseif ($known[$caseId].Category -ne $actualDiffs[$caseId].Category) {
            $drifted.Add("$caseId : known=$($known[$caseId].Category) actual=$($actualDiffs[$caseId].Category) -- $($actualDiffs[$caseId].Reason)")
        }
    }

    $staleNowPassing = [System.Collections.Generic.List[string]]::new()
    $staleCaseId = [System.Collections.Generic.List[string]]::new()
    foreach ($caseId in $known.Keys) {
        if (-not $gridIds.Contains($caseId)) {
            $staleCaseId.Add($caseId)
        }
        elseif (-not $actualDiffs.ContainsKey($caseId)) {
            $staleNowPassing.Add($caseId)
        }
    }

    $problems = $regressions.Count + $drifted.Count + $staleNowPassing.Count + $staleCaseId.Count
    if ($problems -eq 0) {
        Write-Host "PASS: $($actualDiffs.Count) differing case(s), all accounted for in $KnownDiffPath; no regression, no drift, no stale row." -ForegroundColor Green
    }
    else {
        Write-Host 'FAIL' -ForegroundColor Red
        if ($regressions.Count -gt 0) {
            Write-Host "  $($regressions.Count) unlisted differing case(s) (regression or newly-covered case not yet recorded):" -ForegroundColor Red
            foreach ($r in $regressions) { Write-Host "    $r" }
        }
        if ($drifted.Count -gt 0) {
            Write-Host "  $($drifted.Count) case(s) whose category no longer matches known-diff.tsv:" -ForegroundColor Red
            foreach ($d in $drifted) { Write-Host "    $d" }
        }
        if ($staleNowPassing.Count -gt 0) {
            Write-Host "  $($staleNowPassing.Count) known-diff.tsv row(s) that now match and must be pruned:" -ForegroundColor Red
            foreach ($s in $staleNowPassing) { Write-Host "    $s" }
        }
        if ($staleCaseId.Count -gt 0) {
            Write-Host "  $($staleCaseId.Count) known-diff.tsv row(s) whose case_id no longer exists in the grid:" -ForegroundColor Red
            foreach ($s in $staleCaseId) { Write-Host "    $s" }
        }
        Write-Host ''
        Write-Host 'Regenerate with: pwsh scripts/verify-swetest-diff.ps1 -Regenerate -Reason "..." (review the diff before committing -- see this script''s own .DESCRIPTION on -Regenerate).'
        $exitCode = 1
    }
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
exit $exitCode
