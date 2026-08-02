#Requires -Version 7
<#
.SYNOPSIS
    Builds Tools/OracleVerify and runs it against the committed dumps and known-diff lists for
    both oracle grids: the gate half of the bit-exact comparison harness
    (scripts/run-oracle-dump.ps1 is the generation half).

.DESCRIPTION
    Reads external/.c-reference/dump-c-2.10.03{,-files}.tsv and dump-net{,-files}.tsv (both
    written by scripts/run-oracle-dump.ps1), keys rows by case_id, and checks every hex column,
    the integer return code, and the serr text. A row that does not match outright must be
    accounted for by that grid's known-diff list -- see Tools/OracleVerify/OracleVerifyReport.cs
    for the three-way check this runs: a differing row absent from the list (or listed under a
    category whose failure shape no longer fits) is a regression; a listed row that now matches
    outright must be pruned; a listed row whose case_id has fallen out of the grid is stale. It
    also gates on magnitude, not just category: each known-diff entry carries the maximum ULP
    distance observed the last time it was regenerated (or the literal text "categorical" for a
    row that differs by a NaN on one side and a finite value on the other, which has no
    meaningful magnitude to compare), and a row whose current ULP distance exceeds that recorded
    maximum, or whose categorical/numeric state has flipped either way, fails even though it is
    still "on the list". That is deliberately stronger than the correctness oracle's
    known-fail.tsv, which compares only the category and so cannot detect a listed mismatch
    quietly growing worse -- Tests/SwissEphNet.Conformance.Tests/ConformanceReport.cs records
    that limitation itself.

    This script does not regenerate anything -- see scripts/regenerate-oracle-known-diff.ps1 for
    the only supported way to change either known-diff list.

    FILE-LEVEL SHA-256 CHECK

    The row-level check above decides hex-column equality via Tools/OracleVerify/UlpMath.cs's
    Distance, which compares the two values' raw 64-bit patterns (BitConverter.DoubleToInt64Bits),
    not double.Equals -- a signed-zero or NaN-payload divergence now fails the row-level check
    directly, rather than being silently absorbed as "zero ULPs of difference" the way an earlier
    double.Equals-based first branch did. That leaves this file-level check covering what the
    row-level comparator still cannot see by design: the decimal (non-hex) column, row order, and
    incidental file bytes -- blank lines, line endings -- that OracleDump.exe/sedump.exe wrote but
    that never pass through a per-field comparison at all. When a grid's known-diff list is empty
    (see Assert-DumpsByteIdentical below), this script also compares the two dump files' SHA-256
    hashes directly and fails if they differ, even though the row-level check above passed --
    that is a check on the bytes OracleDump.exe and sedump.exe actually wrote, independent of how
    OracleVerify chooses to interpret them. Skipped when the known-diff list carries recorded
    exceptions, since the two dumps are not expected to be byte-identical in that case.

    STALE-DUMP CHECK

    Before comparing anything, this script checks external/.c-reference/oracle-provenance.tsv
    (written by scripts/run-oracle-dump.ps1) against what is on disk now: the two grids' current
    content -- three under -Grid Jpl, which additionally requires and rehashes the grid_jpl and
    jpl_ephe_file rows, the latter being the DE file itself, so that swapping one DE file for
    another under the same name cannot go unnoticed -- sedump.exe and sedump-2.08.exe as they
    currently sit on disk, and the port's own
    source under SwissEphNet/ -- every *.cs file plus SwissEphNet.csproj, excluding bin/ and
    obj/ -- rehashed the same way scripts/run-oracle-dump.ps1 hashed it. Recomputing that hash at
    verify time is what catches the scenario this check exists for: edit SwissEphNet/CPort/, then
    run this gate without re-running scripts/run-oracle-dump.ps1 first. Any mismatch is a hard
    failure naming exactly what changed, before this script builds or runs anything else:
    comparing dumps that no longer reflect the current grids or the current port is not a check
    worth running, and reporting PASS on it would be worse than not checking at all. This script
    never repairs the mismatch itself -- see scripts/run-oracle-dump.ps1. Skipped when
    -CDumpPath/-NetDumpPath/-KnownDiffPath point this script at non-default files, since the
    provenance sidecar only describes scripts/run-oracle-dump.ps1's own default outputs.

    Hashing source, rather than building SwissEphNet.csproj and hashing the resulting DLL, means
    this check needs no build of its own. An earlier version did build here, purely to hash the
    DLL it produced -- but that DLL embeds the current git commit (SourceLink, turned on under CI
    by Directory.Build.props), so its hash changed on every commit regardless of whether
    SwissEphNet/ itself changed. A documentation-only commit was enough to fail this check even
    though the dumps still matched the code that produced them.

.PARAMETER Grid
    'Analytic', 'Files', 'Jpl' or 'Both' (default). 'Both' means Analytic and Files -- it does NOT
    include Jpl, and deliberately so: that grid's dumps only exist when scripts/run-oracle-dump.ps1
    was opted in with a DE file this repo does not ship, so folding it into the default would turn
    every CI run and every contributor without a 190 MB DE file red for a reason that has nothing
    to do with the port. Pass -Grid Jpl explicitly to check it, after a run that produced its
    dumps. 'Both' runs the check for each of its grids in turn and exits non-zero if either fails;
    -CDumpPath/-NetDumpPath/-KnownDiffPath cannot be combined with 'Both' since a single set of
    overrides cannot name two grids' worth of files -- pass a single grid name to use them.

.PARAMETER CDumpPath
    Only valid with -Grid Analytic or -Grid Files. Defaults to
    external/.c-reference/dump-c-2.10.03.tsv (Analytic) or
    external/.c-reference/dump-c-2.10.03-files.tsv (Files).

.PARAMETER NetDumpPath
    Only valid with -Grid Analytic or -Grid Files. Defaults to external/.c-reference/dump-net.tsv
    (Analytic) or external/.c-reference/dump-net-files.tsv (Files).

.PARAMETER KnownDiffPath
    Only valid with -Grid Analytic or -Grid Files. Defaults to Tests/oracle/known-diff.tsv
    (Analytic) or Tests/oracle/known-diff-files.tsv (Files).

.PARAMETER ProvenancePath
    The sidecar scripts/run-oracle-dump.ps1 writes on a successful run -- see STALE-DUMP CHECK
    above. Defaults to external/.c-reference/oracle-provenance.tsv.

.PARAMETER SelfTest
    Plants the known provenance bypasses (an old-format sidecar with no dump_c_*/dump_net_* rows;
    a dump substituted for another dump after the sidecar recorded it; a sidecar naming a
    self-consistent decoy grid instead of the committed one) into scratch files under a temporary
    directory and asserts Read-Provenance/Get-ProvenanceMismatches refuse each. Builds nothing,
    runs no dotnet project, and touches no tracked file.
#>

[CmdletBinding()]
param(
    [ValidateSet('Analytic', 'Files', 'Jpl', 'Both')]
    [string] $Grid = 'Both',

    [string] $CDumpPath,
    [string] $NetDumpPath,
    [string] $KnownDiffPath,
    [string] $ProvenancePath,

    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ProvenancePath) { $ProvenancePath = Join-Path $repoRoot 'external/.c-reference/oracle-provenance.tsv' }
$ProvenancePath = [System.IO.Path]::GetFullPath($ProvenancePath)

$overridesGiven = $CDumpPath -or $NetDumpPath -or $KnownDiffPath
if ($Grid -eq 'Both' -and $overridesGiven) {
    Write-Host 'FAIL: -CDumpPath/-NetDumpPath/-KnownDiffPath require a single -Grid (Analytic or Files); they cannot be combined with -Grid Both.' -ForegroundColor Red
    exit 1
}

function Get-GridDefaults {
    param([string] $GridName)
    if ($GridName -eq 'Analytic') {
        return [pscustomobject]@{
            CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03.tsv'
            NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net.tsv'
            KnownDiffPath = Join-Path $repoRoot 'Tests/oracle/known-diff.tsv'
        }
    }
    if ($GridName -eq 'Jpl') {
        return [pscustomobject]@{
            CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-jpl.tsv'
            NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net-jpl.tsv'
            KnownDiffPath = Join-Path $repoRoot 'Tests/oracle/known-diff-jpl.tsv'
        }
    }
    return [pscustomobject]@{
        CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-files.tsv'
        NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net-files.tsv'
        KnownDiffPath = Join-Path $repoRoot 'Tests/oracle/known-diff-files.tsv'
    }
}

# True only when a resolved -CDumpPath/-NetDumpPath/-KnownDiffPath actually differs from that
# grid's own default -- not merely when the parameter was passed. Without this, `-CDumpPath
# external/.c-reference/dump-c-2.10.03.tsv` (spelled out explicitly, but identical to what the
# default would have resolved to anyway) disabled the stale-dump check below just as completely as
# pointing it at a genuinely different file: $overridesGiven only ever asked "was a parameter
# passed", never "does it name something other than the file the provenance sidecar already
# describes". The provenance sidecar's own rows are unaffected by which parameter spelling named
# them, so skipping the check in that case skipped a check that was still perfectly meaningful.
function Test-ResolvedOverridesDiffer {
    param(
        [string] $GridName,
        [string] $GivenCDumpPath,
        [string] $GivenNetDumpPath,
        [string] $GivenKnownDiffPath
    )
    if ($GridName -eq 'Both') { return $false }
    $defaults = Get-GridDefaults -GridName $GridName
    $pairs = @(
        @{ Given = $GivenCDumpPath; Default = $defaults.CDumpPath }
        @{ Given = $GivenNetDumpPath; Default = $defaults.NetDumpPath }
        @{ Given = $GivenKnownDiffPath; Default = $defaults.KnownDiffPath }
    )
    foreach ($pair in $pairs) {
        if (-not $pair.Given) { continue }
        $givenFull = [System.IO.Path]::GetFullPath($pair.Given)
        $defaultFull = [System.IO.Path]::GetFullPath($pair.Default)
        if (-not [string]::Equals($givenFull, $defaultFull, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

# True when a known-diff TSV (Tests/oracle/known-diff.tsv or -files.tsv) has no data rows below
# its header -- i.e. the grid claims every row is bit-identical, with nothing on the exemption
# list. That claim is what the file-level SHA-256 check below actually verifies; a non-empty list
# means at least one row is a recorded, reviewed exception, so a byte-for-byte dump comparison
# would trivially fail on it and proves nothing.
function Test-KnownDiffEmpty {
    param([string] $Path)
    $dataLines = @(Get-Content -LiteralPath $Path | Select-Object -Skip 1 | Where-Object { $_.Length -gt 0 })
    return $dataLines.Count -eq 0
}

# Row-level "bit-identical" (OracleVerify's "verify" mode, called below) means every hex column,
# the retc and the serr text compare equal per Tools/OracleVerify/RowOutcome.cs -- and hex-column
# equality is decided by Tools/OracleVerify/UlpMath.cs's Distance, which compares the two values'
# raw 64-bit patterns directly (BitConverter.DoubleToInt64Bits), not double.Equals: a row where
# the C dump's hex column decodes to -0.0 and the .NET dump's decodes to +0.0 -- two different bit
# patterns, two different hex strings on disk -- is a real, reported ULP difference now, not
# silently accepted as matching the way an earlier double.Equals-based first branch treated it.
# What this file-level check still covers, that the row-level comparator by design does not: the
# decimal (non-hex) text column each hex column sits next to in the dump format (DumpRow.Parse
# only reads the hex fields into Values -- the decimal text is never part of the row-level
# comparison at all), row order, and incidental file bytes (blank lines, line endings) that
# OracleDump.exe/sedump.exe wrote but that no per-field comparison ever inspects. A SHA-256
# comparison of the whole file catches exactly that, independent of what any row-level comparator
# considers "equal" -- it is a check on the bytes OracleDump.exe and sedump.exe actually wrote,
# not on how OracleVerify chooses to interpret them.
function Assert-DumpsByteIdentical {
    param([string] $CPath, [string] $NetPath, [string] $KnownDiffPath)

    if (-not (Test-KnownDiffEmpty -Path $KnownDiffPath)) {
        Write-Host "SKIP: $KnownDiffPath lists recorded exception(s), so the two dumps are not expected to be byte-identical." -ForegroundColor Yellow
        return 0
    }

    $cHash = Get-Sha256Hex -Path $CPath
    $netHash = Get-Sha256Hex -Path $NetPath
    if ($cHash -ne $netHash) {
        Write-Host "FAIL: $KnownDiffPath is empty (claims every row is bit-identical), but the dump files themselves differ: $CPath is $cHash, $NetPath is $netHash." -ForegroundColor Red
        Write-Host '      The row-level check above can still report PASS here: it never inspects the decimal (non-hex) text column, row order, or incidental bytes (blank lines, line endings) -- a raw file hash catches a divergence in any of those that no per-field comparison would.' -ForegroundColor Red
        return 1
    }

    Write-Host "PASS: $KnownDiffPath is empty and $CPath / $NetPath are SHA-256 identical ($cHash) -- bit-identical at the file level, not only per the row-level comparator." -ForegroundColor Green
    return 0
}

function Invoke-GridVerify {
    param([string] $GridName, [string] $Project)

    $defaults = Get-GridDefaults -GridName $GridName
    $cPath = if ($CDumpPath) { $CDumpPath } else { $defaults.CDumpPath }
    $netPath = if ($NetDumpPath) { $NetDumpPath } else { $defaults.NetDumpPath }
    $knownDiffPathResolved = if ($KnownDiffPath) { $KnownDiffPath } else { $defaults.KnownDiffPath }

    $cPath = [System.IO.Path]::GetFullPath($cPath)
    $netPath = [System.IO.Path]::GetFullPath($netPath)
    $knownDiffPathResolved = [System.IO.Path]::GetFullPath($knownDiffPathResolved)

    Write-Host "=== $GridName grid ===" -ForegroundColor Cyan

    # Missing-dump and zero-row/row-count-mismatch checks both matter here, but only the first is
    # this script's own job: a missing dump means nothing has been generated yet, which is a
    # different problem from "generated, but the two sides disagree", and only the first one has
    # an obvious next step (run the other script) worth naming explicitly. Row-count and case-id-set
    # mismatches are caught by OracleVerify.exe itself (Tools/OracleVerify/Program.cs's
    # LoadAndCompare), which already produces a specific, actionable message for each -- duplicating
    # that logic here would just be a second place for the two checks to drift out of sync.
    $regenerateHint = if ($GridName -eq 'Jpl') {
        'Run: pwsh scripts/run-oracle-dump.ps1 -JplFile <path-to-a-DE-file> (or set SWISSEPH_ORACLE_JPL_FILE) -- this grid is opt-in and produces no dumps otherwise'
    } else {
        'Run: pwsh scripts/run-oracle-dump.ps1'
    }
    if (-not (Test-Path -LiteralPath $cPath -PathType Leaf)) {
        Write-Host "FAIL: C dump not found at $cPath. $regenerateHint" -ForegroundColor Red
        return 1
    }
    if (-not (Test-Path -LiteralPath $netPath -PathType Leaf)) {
        Write-Host "FAIL: .NET dump not found at $netPath. $regenerateHint" -ForegroundColor Red
        return 1
    }

    Write-Host "C dump:        $cPath"
    Write-Host ".NET dump:     $netPath"
    Write-Host "Known-diff:    $knownDiffPathResolved"
    Write-Host ''

    # Captured, then explicitly written to host: a bare, uncaptured native-command call inside a
    # PowerShell function folds its stdout into the function's own return value alongside
    # whatever this function later `return`s, silently turning the caller's single exit-code
    # check into a comparison against a multi-line array. Capturing first and writing with
    # Write-Host keeps this function's return value to the exit code alone.
    $verifyOutput = & dotnet run --project $Project -c Release --no-build -- verify $cPath $netPath $knownDiffPathResolved
    $verifyOutput | Write-Host
    $rowLevelExitCode = $LASTEXITCODE
    if ($rowLevelExitCode -ne 0) {
        return $rowLevelExitCode
    }

    Write-Host ''
    return Assert-DumpsByteIdentical -CPath $cPath -NetPath $netPath -KnownDiffPath $knownDiffPathResolved
}

# ---------------------------------------------------------------------------------------
# Stale-dump check -- see this script's own STALE-DUMP CHECK header section. Kept here rather
# than delegated to Tools/OracleVerify, which never opens a grid file or reads SwissEphNet/'s
# source at all; this is orchestration (hashing files), the same kind of work
# scripts/run-oracle-dump.ps1 already does for the C side.
# ---------------------------------------------------------------------------------------

function Get-Sha256Hex {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Fingerprints the port's own source (every *.cs under SwissEphNet/ plus
# SwissEphNet/SwissEphNet.csproj, excluding bin/ and obj/) -- see the STALE-DUMP CHECK header
# section above for why. Kept as its own copy here, matching how this script already keeps its
# own copies of Get-Sha256Hex and the rest of the toolchain code above instead of dot-sourcing
# scripts/run-oracle-dump.ps1. Modeled on scripts/verify-freeze.ps1's Get-Fingerprint:
# -LiteralPath and -Force (this repo has SwissEphNet/[Events].cs; square brackets are wildcards
# under -Path, and a -Force-less enumeration hides dotfiles on Unix but not Windows), an ordinal
# sort of repo-relative paths so the order never depends on culture, hashing the path alongside
# the content so moving code between files counts as a change, and line-ending normalization so a
# CRLF/LF difference between checkouts does not read as a source change. Must produce the exact
# same hash as scripts/run-oracle-dump.ps1's copy for the same tree, or every run would show a
# false mismatch.
function Get-PortSourceHash {
    param([string] $RepoRoot)

    $srcDir = Join-Path $RepoRoot 'SwissEphNet'
    $csprojPath = Join-Path $srcDir 'SwissEphNet.csproj'
    if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
        throw "SwissEphNet.csproj not found at $csprojPath."
    }

    $allFiles = @(Get-ChildItem -LiteralPath $srcDir -Recurse -File -Force)
    $csFiles = @($allFiles | Where-Object {
        $_.Extension -eq '.cs' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    })
    $files = [object[]] (@($csFiles) + @(Get-Item -LiteralPath $csprojPath))

    $keys = [string[]] ($files | ForEach-Object { $_.FullName.Substring($RepoRoot.Length).Replace([char]92, [char]47) })
    [System.Array]::Sort($keys, $files, [System.StringComparer]::Ordinal)

    $contents = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $files.Count; $i++) {
        $text = [System.IO.File]::ReadAllText($files[$i].FullName)
        $normalized = ($text -split "`r`n|`n|`r") -join "`n"
        [void]$contents.Add($keys[$i])
        [void]$contents.Add($normalized)
    }

    return [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes(($contents -join "`n")))).Replace('-', '').ToLowerInvariant()
}

function Read-Provenance {
    param([string] $Path, [bool] $RequireJpl)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Provenance sidecar not found at $Path. Run: pwsh scripts/run-oracle-dump.ps1"
    }

    $dataLines = @(Get-Content -LiteralPath $Path | Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') })
    if ($dataLines.Count -eq 0) {
        throw "$Path has no header row. Re-run: pwsh scripts/run-oracle-dump.ps1"
    }

    $expectedHeader = @('name', 'path', 'sha256') -join "`t"
    if ($dataLines[0] -ne $expectedHeader) {
        throw "$Path`: expected header '$expectedHeader', got '$($dataLines[0])'."
    }

    $rows = @{}
    for ($i = 1; $i -lt $dataLines.Count; $i++) {
        $parts = $dataLines[$i] -split "`t"
        if ($parts.Count -ne 3) {
            throw "$Path`: expected 3 tab-separated columns, got $($parts.Count): '$($dataLines[$i])'"
        }
        $rows[$parts[0]] = [pscustomobject]@{ Path = $parts[1]; Sha256 = $parts[2] }
    }

    # dump_c_*/dump_net_* are as required as the grids themselves: without them, a dump swapped
    # for another dump after a legitimate run left every recorded row still matching, since nothing
    # here ever hashed the dumps -- only what produced them. Measured directly: copying
    # dump-c-2.10.03.tsv over dump-net.tsv against a sidecar with only the five older rows still
    # reported "grids ... still match the recorded provenance" and this gate exited 0.
    $required = @(
        'grid_analytic', 'grid_files',
        'dump_c_analytic', 'dump_net_analytic', 'dump_c_files', 'dump_net_files',
        'swisseph_net_source', 'sedump_exe', 'sedump_208_exe'
    )
    $missingRows = @($required | Where-Object { -not $rows.ContainsKey($_) })
    if ($missingRows.Count -gt 0) {
        throw "$Path is missing required row(s): $($missingRows -join ', '). Re-run: pwsh scripts/run-oracle-dump.ps1"
    }

    # grid_jpl/dump_c_jpl/dump_net_jpl/jpl_ephe_file are written only by a run that actually
    # replayed the JPL grid, which needs a DE file this repo does not ship. Their absence is the
    # normal case and not an error in itself -- it only becomes one when this run was asked to
    # check that grid, in which case the JPL dumps on disk (if any) are left over from an earlier
    # run and must not be trusted.
    if ($RequireJpl) {
        $missingJpl = @(@('grid_jpl', 'dump_c_jpl', 'dump_net_jpl', 'jpl_ephe_file') | Where-Object { -not $rows.ContainsKey($_) })
        if ($missingJpl.Count -gt 0) {
            throw "$Path is missing row(s) $($missingJpl -join ', '), so the last run of scripts/run-oracle-dump.ps1 did not replay the JPL grid. Any dump-*-jpl.tsv on disk is from an earlier run. Re-run with -JplFile (or SWISSEPH_ORACLE_JPL_FILE) pointing at a DE file, then re-run this gate."
        }
    }

    return $rows
}

# Returns a list of human-readable mismatch descriptions; an empty list means every recorded
# input still matches what is on disk now. Never modifies $Rows, the dumps, or anything else --
# this function only looks.
function Get-ProvenanceMismatches {
    param([hashtable] $Rows, [string] $RepoRoot, [bool] $CheckJpl)

    $mismatches = [System.Collections.Generic.List[string]]::new()

    function Test-RecordedFile {
        param([string] $Key, [string] $Description)
        $recorded = $Rows[$Key]
        if (-not (Test-Path -LiteralPath $recorded.Path -PathType Leaf)) {
            $mismatches.Add("$Description ($($recorded.Path)) no longer exists.")
            return
        }
        $current = Get-Sha256Hex -Path $recorded.Path
        if ($current -ne $recorded.Sha256) {
            $mismatches.Add("$Description ($($recorded.Path)) changed since the dumps were generated: recorded $($recorded.Sha256), now $current.")
        }
    }

    # A recorded row is only as trustworthy as the path it names. Rehashing $recorded.Path proves
    # that path's content has not moved since generation, but proves nothing about WHICH file that
    # path is -- a sidecar can name any file at all, including one nobody reviewed, and pass this
    # far by construction (scripts/run-oracle-dump.ps1 hashes whatever -GridPath it is given, so a
    # 1-row decoy grid produces a perfectly self-consistent row). This is the second, independent
    # check: the recorded path itself must equal the one committed file this repo actually ships at
    # that name, not merely some file that has not changed since it was hashed.
    function Test-RecordedPathIsCanonical {
        param([string] $Key, [string] $ExpectedPath, [string] $Description)
        $recorded = $Rows[$Key]
        $recordedFull = [System.IO.Path]::GetFullPath($recorded.Path)
        $expectedFull = [System.IO.Path]::GetFullPath($ExpectedPath)
        if (-not [string]::Equals($recordedFull, $expectedFull, [StringComparison]::OrdinalIgnoreCase)) {
            $mismatches.Add("$Description was generated from a different file than this repo ships: recorded path $recordedFull, expected $expectedFull. A sidecar naming any other file passes the content-hash check on its own; this is the check that the path itself is the committed one.")
        }
    }

    Test-RecordedFile -Key 'grid_analytic' -Description 'The analytic grid (Tools/OracleGrid/grid-analytic.tsv)'
    Test-RecordedFile -Key 'grid_files' -Description 'The files grid (Tools/OracleGrid/grid-files.tsv)'
    Test-RecordedFile -Key 'dump_c_analytic' -Description 'The C dump (analytic grid, dump-c-2.10.03.tsv)'
    Test-RecordedFile -Key 'dump_net_analytic' -Description 'The .NET dump (analytic grid, dump-net.tsv)'
    Test-RecordedFile -Key 'dump_c_files' -Description 'The C dump (files grid, dump-c-2.10.03-files.tsv)'
    Test-RecordedFile -Key 'dump_net_files' -Description 'The .NET dump (files grid, dump-net-files.tsv)'
    Test-RecordedFile -Key 'sedump_exe' -Description 'sedump.exe (2.10.03, Tools/CReference/sedump.c)'
    Test-RecordedFile -Key 'sedump_208_exe' -Description 'sedump-2.08.exe'

    Test-RecordedPathIsCanonical -Key 'grid_analytic' -ExpectedPath (Join-Path $RepoRoot 'Tools/OracleGrid/grid-analytic.tsv') -Description 'The analytic grid'
    Test-RecordedPathIsCanonical -Key 'grid_files' -ExpectedPath (Join-Path $RepoRoot 'Tools/OracleGrid/grid-files.tsv') -Description 'The files grid'

    # HIGH 1 fix: the content-hash check above (Test-RecordedFile) proves the path the sidecar
    # names has not changed since it was hashed -- it proves nothing about WHICH file that path is.
    # Before this, only the two grid rows and (under -CheckJpl) grid_jpl got the canonical-path
    # check; the six dump rows and both sedump executables did not, so repointing e.g.
    # dump_net_analytic's path column at a kept-aside REGRESSED copy of dump-net.tsv, with that
    # copy's own true SHA-256 recorded alongside it, passed Test-RecordedFile outright (the hash
    # matches the file the path now names) and this whole gate reported PASS -- the exact bypass
    # 38e9d1c was written to close, reopened by only ever having closed it for the two grid rows.
    # Measured directly: one column edit (the path, not the hash) in an otherwise-honest sidecar,
    # against a dump-net.tsv regressed by one ULP with the regression hidden behind a copy of the
    # C dump, exited 0 before this fix and is refused by it now -- see -SelfTest cases 5 and 6.
    # NOT Get-GridDefaults: that function closes over the top-level script's own $repoRoot
    # variable rather than taking one as a parameter, so calling it from inside this function
    # would resolve against the real repository root even when this function itself was called
    # with a different -RepoRoot (exactly what -SelfTest below does) -- built directly against the
    # $RepoRoot this function received instead, matching Get-GridDefaults' own relative paths.
    Test-RecordedPathIsCanonical -Key 'dump_c_analytic' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-c-2.10.03.tsv') -Description 'The C dump (analytic grid)'
    Test-RecordedPathIsCanonical -Key 'dump_net_analytic' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-net.tsv') -Description 'The .NET dump (analytic grid)'
    Test-RecordedPathIsCanonical -Key 'dump_c_files' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-c-2.10.03-files.tsv') -Description 'The C dump (files grid)'
    Test-RecordedPathIsCanonical -Key 'dump_net_files' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-net-files.tsv') -Description 'The .NET dump (files grid)'
    Test-RecordedPathIsCanonical -Key 'sedump_exe' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/oracle-dump-c/sedump.exe') -Description 'sedump.exe (2.10.03, Tools/CReference/sedump.c)'
    Test-RecordedPathIsCanonical -Key 'sedump_208_exe' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/sedump-2.08.exe') -Description 'sedump-2.08.exe'

    # Only when this run was asked to check the JPL grid -- Read-Provenance has already refused
    # outright if the rows are missing in that case, so all four keys exist by the time this runs.
    # Re-hashing the DE file is the point of recording it: it is the one input that lives outside
    # the repo, and swapping DE406 for DE431 under the same name would change every value in the
    # dump with nothing else noticing. jpl_ephe_file carries no canonical in-repo path to pin
    # against -- it names wherever the caller's DE file happens to sit -- so only its content hash
    # is checked, the same as before.
    if ($CheckJpl) {
        Test-RecordedFile -Key 'grid_jpl' -Description 'The JPL grid (Tools/OracleGrid/grid-jpl.tsv)'
        Test-RecordedFile -Key 'dump_c_jpl' -Description 'The C dump (JPL grid, dump-c-2.10.03-jpl.tsv)'
        Test-RecordedFile -Key 'dump_net_jpl' -Description 'The .NET dump (JPL grid, dump-net-jpl.tsv)'
        Test-RecordedFile -Key 'jpl_ephe_file' -Description 'The JPL DE file the dumps were generated against'
        Test-RecordedPathIsCanonical -Key 'grid_jpl' -ExpectedPath (Join-Path $RepoRoot 'Tools/OracleGrid/grid-jpl.tsv') -Description 'The JPL grid'
        Test-RecordedPathIsCanonical -Key 'dump_c_jpl' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-c-2.10.03-jpl.tsv') -Description 'The C dump (JPL grid)'
        Test-RecordedPathIsCanonical -Key 'dump_net_jpl' -ExpectedPath (Join-Path $RepoRoot 'external/.c-reference/dump-net-jpl.tsv') -Description 'The .NET dump (JPL grid)'
    }

    # swisseph_net_source is rehashed straight from the tree with Get-PortSourceHash -- see this
    # script's STALE-DUMP CHECK header section for why that makes this comparison mean something
    # on every commit.
    $recordedSource = $Rows['swisseph_net_source']
    $currentSourceSha = Get-PortSourceHash -RepoRoot $RepoRoot
    if ($currentSourceSha -ne $recordedSource.Sha256) {
        $mismatches.Add("The port source under SwissEphNet/ changed since the dumps were generated: recorded $($recordedSource.Sha256), now $currentSourceSha.")
    }

    # The leading comma matters: PowerShell enumerates an IEnumerable placed on the output
    # stream, so a bare `return $mismatches` would unroll a one-element List[string] into that
    # single string instead of the list -- exactly the shape scripts/run-oracle-dump.ps1's own
    # provenance-row construction hit earlier (see the comment there). The comma operator forces
    # $mismatches through as one object.
    return , $mismatches
}

# ---------------------------------------------------------------------------------------
# Self-test -- see -SelfTest above. Placed after every function this gate needs (Get-Sha256Hex,
# Get-PortSourceHash, Read-Provenance, Get-ProvenanceMismatches) and before the real run, so it
# exercises those functions directly rather than shelling out to a full run of this script.
# ---------------------------------------------------------------------------------------

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-oracle-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        function New-LabFile {
            param([string] $RelPath, [string] $Content)
            $full = Join-Path $lab $RelPath
            New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent) | Out-Null
            [System.IO.File]::WriteAllText($full, $Content, (New-Object System.Text.UTF8Encoding $false))
            return $full
        }

        # Minimal port source tree so Get-PortSourceHash has something real to hash -- this gate's
        # provenance check always rehashes SwissEphNet/ unconditionally, so every case needs one.
        New-LabFile 'SwissEphNet/SwissEphNet.csproj' '<Project Sdk="Microsoft.NET.Sdk"></Project>' | Out-Null
        New-LabFile 'SwissEphNet/Foo.cs' 'class Foo {}' | Out-Null

        $gridAnalytic = New-LabFile 'Tools/OracleGrid/grid-analytic.tsv' "case_id`tfoo`nA|1`t1`n"
        $gridFiles = New-LabFile 'Tools/OracleGrid/grid-files.tsv' "case_id`tfoo`nF|1`t1`n"
        # A file that is NOT Tools/OracleGrid/grid-analytic.tsv, but whose content a sidecar could
        # still hash truthfully -- the shape HIGH 1's second bypass needs: self-consistent, wrong
        # file.
        $decoyGrid = New-LabFile 'Tools/OracleGrid/decoy-grid.tsv' "case_id`tfoo`nA|1`t1`n"

        # Deliberately different content on the two sides: case 3 below swaps one into the other's
        # file, which is only a real test of the substitution check if that swap actually changes
        # what is on disk.
        $dumpCAnalytic = New-LabFile 'external/.c-reference/dump-c-2.10.03.tsv' "A|1`t0x1`n"
        $dumpNetAnalytic = New-LabFile 'external/.c-reference/dump-net.tsv' "A|1`t0x2`n"
        $dumpCFiles = New-LabFile 'external/.c-reference/dump-c-2.10.03-files.tsv' "F|1`t0x1`n"
        $dumpNetFiles = New-LabFile 'external/.c-reference/dump-net-files.tsv' "F|1`t0x1`n"
        $sedumpExe = New-LabFile 'external/.c-reference/oracle-dump-c/sedump.exe' 'fake-exe'
        $sedump208Exe = New-LabFile 'external/.c-reference/sedump-2.08.exe' 'fake-exe-208'

        $sourceHash = Get-PortSourceHash -RepoRoot $lab

        function Write-Sidecar {
            param([string] $Path, [System.Collections.Specialized.OrderedDictionary] $Rows)
            $lines = [System.Collections.Generic.List[string]]::new()
            $lines.Add('# selftest sidecar')
            $lines.Add((@('name', 'path', 'sha256') -join "`t"))
            foreach ($key in $Rows.Keys) {
                $r = $Rows[$key]
                $lines.Add((@($key, $r.Path, $r.Sha256) -join "`t"))
            }
            [System.IO.File]::WriteAllText($Path, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
        }

        function New-BaseRows {
            $rows = [ordered]@{}
            $rows['grid_analytic'] = @{ Path = $gridAnalytic; Sha256 = (Get-Sha256Hex -Path $gridAnalytic) }
            $rows['grid_files'] = @{ Path = $gridFiles; Sha256 = (Get-Sha256Hex -Path $gridFiles) }
            $rows['dump_c_analytic'] = @{ Path = $dumpCAnalytic; Sha256 = (Get-Sha256Hex -Path $dumpCAnalytic) }
            $rows['dump_net_analytic'] = @{ Path = $dumpNetAnalytic; Sha256 = (Get-Sha256Hex -Path $dumpNetAnalytic) }
            $rows['dump_c_files'] = @{ Path = $dumpCFiles; Sha256 = (Get-Sha256Hex -Path $dumpCFiles) }
            $rows['dump_net_files'] = @{ Path = $dumpNetFiles; Sha256 = (Get-Sha256Hex -Path $dumpNetFiles) }
            $rows['swisseph_net_source'] = @{ Path = (Join-Path $lab 'SwissEphNet'); Sha256 = $sourceHash }
            $rows['sedump_exe'] = @{ Path = $sedumpExe; Sha256 = (Get-Sha256Hex -Path $sedumpExe) }
            $rows['sedump_208_exe'] = @{ Path = $sedump208Exe; Sha256 = (Get-Sha256Hex -Path $sedump208Exe) }
            return $rows
        }

        # Read-Provenance's real return shape is @{ key -> pscustomobject{Path;Sha256} } -- built
        # here directly from a Write-Sidecar row set, rather than round-tripped through a real
        # sidecar file, for the cases that exercise Get-ProvenanceMismatches alone.
        function Convert-RowsToHashtable {
            param([System.Collections.Specialized.OrderedDictionary] $Rows)
            $ht = @{}
            foreach ($key in $Rows.Keys) {
                $ht[$key] = [pscustomobject]@{ Path = $Rows[$key].Path; Sha256 = $Rows[$key].Sha256 }
            }
            return $ht
        }

        function Assert-NoMismatches {
            param([string] $Case, [hashtable] $Rows)
            $mismatches = Get-ProvenanceMismatches -Rows $Rows -RepoRoot $lab -CheckJpl $false
            if ($mismatches.Count -eq 0) {
                Write-Host "  PASS  $Case (accepted)" -ForegroundColor DarkGray
            }
            else {
                Write-Host "  FAIL  $Case`n          expected no mismatches, got:`n$((($mismatches | ForEach-Object { "            $_" }) -join "`n"))" -ForegroundColor Red
                $script:failures++
            }
        }

        function Assert-Mismatch {
            param([string] $Case, [hashtable] $Rows, [string] $Matching)
            $mismatches = Get-ProvenanceMismatches -Rows $Rows -RepoRoot $lab -CheckJpl $false
            $joined = $mismatches -join ' | '
            if ($mismatches.Count -gt 0 -and $joined -match $Matching) {
                Write-Host "  PASS  $Case (refused: $joined)" -ForegroundColor DarkGray
            }
            else {
                Write-Host "  FAIL  $Case`n          expected a mismatch matching /$Matching/, got: $joined" -ForegroundColor Red
                $script:failures++
            }
        }

        Write-Host 'verify-oracle self-test'
        Write-Host ''

        # 1. Control: a fully valid, self-consistent, canonically-pathed provenance record must
        #    pass with zero mismatches -- otherwise every refusal case below proves only that this
        #    harness makes the check red no matter what.
        Assert-NoMismatches 'a fully valid provenance record is accepted' (Convert-RowsToHashtable (New-BaseRows))

        # 2. HIGH 1, first half: an old-format sidecar recording only the original five rows (no
        #    dump_c_*/dump_net_* at all) must be rejected by Read-Provenance's required-row check
        #    before Get-ProvenanceMismatches ever runs -- this is exactly what let a dump
        #    substitution go completely unrecorded before this fix.
        $oldRows = New-BaseRows
        $oldRows.Remove('dump_c_analytic'); $oldRows.Remove('dump_net_analytic')
        $oldRows.Remove('dump_c_files'); $oldRows.Remove('dump_net_files')
        $oldSidecar = Join-Path $lab 'old-provenance.tsv'
        Write-Sidecar -Path $oldSidecar -Rows $oldRows
        $threw = $false
        $message = $null
        try { Read-Provenance -Path $oldSidecar -RequireJpl $false | Out-Null }
        catch { $threw = $true; $message = $_.Exception.Message }
        if ($threw -and $message -match 'dump_c_analytic') {
            Write-Host "  PASS  an old-format sidecar with no dump rows is rejected (refused: $message)" -ForegroundColor DarkGray
        }
        else {
            Write-Host "  FAIL  an old-format sidecar with no dump rows is rejected`n          expected Read-Provenance to throw naming dump_c_analytic, got: threw=$threw message=$message" -ForegroundColor Red
            $failures++
        }

        # 3. HIGH 1, second half: with the dump rows present, substituting the C dump's content
        #    into the .NET dump's file AFTER the sidecar recorded the .NET dump's own (different)
        #    hash must be caught -- the exact "copy dump-c-2.10.03.tsv over dump-net.tsv"
        #    reproduction from the review, run against this lab's own files.
        $rows = New-BaseRows
        $originalNetContent = [System.IO.File]::ReadAllText($dumpNetAnalytic)
        [System.IO.File]::WriteAllText($dumpNetAnalytic, [System.IO.File]::ReadAllText($dumpCAnalytic), (New-Object System.Text.UTF8Encoding $false))
        try {
            Assert-Mismatch 'a dump swapped for another dump after recording is caught' (Convert-RowsToHashtable $rows) 'dump-net\.tsv\) changed'
        }
        finally {
            [System.IO.File]::WriteAllText($dumpNetAnalytic, $originalNetContent, (New-Object System.Text.UTF8Encoding $false))
        }

        # 4. HIGH 1, second bypass: a sidecar naming a self-consistent DECOY grid (its recorded
        #    path is not Tools/OracleGrid/grid-analytic.tsv, but its hash matches that decoy's own
        #    content) must still be refused -- a content-hash check alone cannot see this, since
        #    the decoy is internally consistent by construction. Only path pinning catches it.
        $decoyRows = New-BaseRows
        $decoyRows['grid_analytic'] = @{ Path = $decoyGrid; Sha256 = (Get-Sha256Hex -Path $decoyGrid) }
        Assert-Mismatch 'a sidecar naming a self-consistent decoy grid is refused' (Convert-RowsToHashtable $decoyRows) 'different file than this repo ships'

        # 5. HIGH 1, the exact review reproduction: a REGRESSED .NET dump kept aside at a
        #    non-canonical path, with the sidecar's dump_net_analytic PATH COLUMN repointed at it
        #    and its SHA-256 recorded truthfully (the hash of the regressed file itself, not of
        #    dump-net.tsv). Test-RecordedFile alone cannot see this -- the recorded path's content
        #    matches its own recorded hash by construction -- so before this fix this case would
        #    have shown no mismatch at all. dump-net.tsv on disk (the canonical path) is left
        #    untouched by this case, standing in for "the regression was never actually written to
        #    the file this repo ships at that name".
        $regressedNetDump = New-LabFile 'external/.c-reference/kept-aside-regressed-dump-net.tsv' "A|1`t0xBAD`n"
        $repointedRows = New-BaseRows
        $repointedRows['dump_net_analytic'] = @{ Path = $regressedNetDump; Sha256 = (Get-Sha256Hex -Path $regressedNetDump) }
        Assert-Mismatch 'a dump row repointed at a self-consistent regressed file (path, not content) is refused' `
            (Convert-RowsToHashtable $repointedRows) 'different file than this repo ships'

        # 6. The same shape as case 5, applied to sedump_exe rather than a dump row -- proving the
        #    fix reaches "both sedump executables" as required, not only the six dump rows.
        $decoySedumpExe = New-LabFile 'external/.c-reference/kept-aside-sedump.exe' 'a different sedump.exe'
        $repointedSedumpRows = New-BaseRows
        $repointedSedumpRows['sedump_exe'] = @{ Path = $decoySedumpExe; Sha256 = (Get-Sha256Hex -Path $decoySedumpExe) }
        Assert-Mismatch 'sedump_exe repointed at a self-consistent decoy executable is refused' `
            (Convert-RowsToHashtable $repointedSedumpRows) 'different file than this repo ships'

        # 7. LOW: Test-ResolvedOverridesDiffer must say "no" for an override spelled out identically
        #    to the default it would have resolved to anyway (the provenance check must NOT be
        #    skipped in that case -- see this function's own comment), and "yes" for one that
        #    actually names something else. Exercised against the real repo's own Analytic defaults
        #    (Get-GridDefaults closes over the real script's $repoRoot, not $lab, so this case does
        #    not use the lab fixtures above at all).
        $realAnalyticDefaults = Get-GridDefaults -GridName 'Analytic'
        $sameAsDefault = Test-ResolvedOverridesDiffer -GridName 'Analytic' -GivenCDumpPath $realAnalyticDefaults.CDumpPath -GivenNetDumpPath $null -GivenKnownDiffPath $null
        $differentFromDefault = Test-ResolvedOverridesDiffer -GridName 'Analytic' -GivenCDumpPath (Join-Path $lab 'not-the-default.tsv') -GivenNetDumpPath $null -GivenKnownDiffPath $null
        if (-not $sameAsDefault -and $differentFromDefault) {
            Write-Host '  PASS  Test-ResolvedOverridesDiffer: identical-to-default is false, genuinely-different is true' -ForegroundColor DarkGray
        }
        else {
            Write-Host "  FAIL  Test-ResolvedOverridesDiffer`n          expected `$false/`$true, got sameAsDefault=$sameAsDefault differentFromDefault=$differentFromDefault" -ForegroundColor Red
            $script:failures++
        }

        Write-Host ''
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all verify-oracle self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$exitCode = 0
try {
    $project = Join-Path $repoRoot 'Tools/OracleVerify/OracleVerify.csproj'
    Write-Host "Building $project (Release)..."
    $buildOutput = & dotnet build $project -c Release --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        throw 'dotnet build Tools/OracleVerify failed.'
    }
    Write-Host ''

    if ($overridesGiven -and (Test-ResolvedOverridesDiffer -GridName $Grid -GivenCDumpPath $CDumpPath -GivenNetDumpPath $NetDumpPath -GivenKnownDiffPath $KnownDiffPath)) {
        Write-Host "NOTE: -CDumpPath/-NetDumpPath/-KnownDiffPath point this run at non-default dumps; skipping the stale-dump check against $ProvenancePath, which only describes scripts/run-oracle-dump.ps1's own default outputs." -ForegroundColor Yellow
        Write-Host ''
    }
    else {
        Write-Host "Checking dump provenance ($ProvenancePath)..."
        $checkJpl = ($Grid -eq 'Jpl')
        $provenanceRows = Read-Provenance -Path $ProvenancePath -RequireJpl $checkJpl
        $mismatches = Get-ProvenanceMismatches -Rows $provenanceRows -RepoRoot $repoRoot -CheckJpl $checkJpl
        if ($mismatches.Count -gt 0) {
            Write-Host ''
            Write-Host 'FAIL: the dumps no longer reflect what is on disk:' -ForegroundColor Red
            foreach ($m in $mismatches) { Write-Host "  - $m" -ForegroundColor Red }
            Write-Host ''
            throw 'Run: pwsh scripts/run-oracle-dump.ps1, then re-run this gate.'
        }
        $provenanceSubject = if ($checkJpl) { 'grids (including the JPL grid and the DE file it was run against), the port source and the sedump executables' } else { 'grids, the port source and the sedump executables' }
        Write-Host "PASS: $provenanceSubject all still match the recorded provenance." -ForegroundColor Green
        Write-Host ''
    }

    $grids = if ($Grid -eq 'Both') { @('Analytic', 'Files') } else { @($Grid) }
    $failed = @()
    foreach ($g in $grids) {
        $result = Invoke-GridVerify -GridName $g -Project $project
        Write-Host ''
        if ($result -ne 0) { $failed += $g }
    }

    if ($failed.Count -gt 0) {
        Write-Host "FAIL: $($failed -join ', ')" -ForegroundColor Red
        $exitCode = 1
    }
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}

exit $exitCode
