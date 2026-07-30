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

    STALE-DUMP CHECK

    Before comparing anything, this script checks external/.c-reference/oracle-provenance.tsv
    (written by scripts/run-oracle-dump.ps1) against what is on disk now: the two grids' current
    content, sedump.exe and sedump-2.08.exe as they currently sit on disk, and SwissEphNet.dll as
    a fresh build of current source now produces it (not the copy scripts/run-oracle-dump.ps1
    left behind, which does not change just because the source did -- rehashing that file as-is
    would never catch the scenario this check exists for: edit SwissEphNet/CPort/, then run this
    gate without re-running scripts/run-oracle-dump.ps1 first). Any mismatch is a hard failure
    naming exactly what changed, before this script builds or runs anything else: comparing
    dumps that no longer reflect the current grids or the current port is not a check worth
    running, and reporting PASS on it would be worse than not checking at all. This script never
    repairs the mismatch itself -- see scripts/run-oracle-dump.ps1. Skipped when
    -CDumpPath/-NetDumpPath/-KnownDiffPath point this script at non-default files, since the
    provenance sidecar only describes scripts/run-oracle-dump.ps1's own default outputs.

.PARAMETER Grid
    'Analytic', 'Files' or 'Both' (default). 'Both' runs the check for each grid in turn and
    exits non-zero if either fails; -CDumpPath/-NetDumpPath/-KnownDiffPath cannot be combined
    with 'Both' since a single set of overrides cannot name two grids' worth of files -- pass a
    single grid name to use them.

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
#>

[CmdletBinding()]
param(
    [ValidateSet('Analytic', 'Files', 'Both')]
    [string] $Grid = 'Both',

    [string] $CDumpPath,
    [string] $NetDumpPath,
    [string] $KnownDiffPath,
    [string] $ProvenancePath
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
    return [pscustomobject]@{
        CDumpPath     = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03-files.tsv'
        NetDumpPath   = Join-Path $repoRoot 'external/.c-reference/dump-net-files.tsv'
        KnownDiffPath = Join-Path $repoRoot 'Tests/oracle/known-diff-files.tsv'
    }
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
    if (-not (Test-Path -LiteralPath $cPath -PathType Leaf)) {
        Write-Host "FAIL: C dump not found at $cPath. Run: pwsh scripts/run-oracle-dump.ps1" -ForegroundColor Red
        return 1
    }
    if (-not (Test-Path -LiteralPath $netPath -PathType Leaf)) {
        Write-Host "FAIL: .NET dump not found at $netPath. Run: pwsh scripts/run-oracle-dump.ps1" -ForegroundColor Red
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
    return $LASTEXITCODE
}

# ---------------------------------------------------------------------------------------
# Stale-dump check -- see this script's own STALE-DUMP CHECK header section. Kept here rather
# than delegated to Tools/OracleVerify, which never opens a grid file or the built library at
# all; this is orchestration (hashing files, rebuilding SwissEphNet.csproj to check it), the same
# kind of work scripts/run-oracle-dump.ps1 already does for the C side.
# ---------------------------------------------------------------------------------------

function Get-Sha256Hex {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-Provenance {
    param([string] $Path)

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

    $required = @('grid_analytic', 'grid_files', 'swisseph_net_dll', 'sedump_exe', 'sedump_208_exe')
    $missingRows = @($required | Where-Object { -not $rows.ContainsKey($_) })
    if ($missingRows.Count -gt 0) {
        throw "$Path is missing required row(s): $($missingRows -join ', '). Re-run: pwsh scripts/run-oracle-dump.ps1"
    }

    return $rows
}

# Returns a list of human-readable mismatch descriptions; an empty list means every recorded
# input still matches what is on disk now. Never modifies $Rows, the dumps, or anything else --
# this function only looks.
function Get-ProvenanceMismatches {
    param([hashtable] $Rows, [string] $RepoRoot)

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

    Test-RecordedFile -Key 'grid_analytic' -Description 'The analytic grid (Tools/OracleGrid/grid-analytic.tsv)'
    Test-RecordedFile -Key 'grid_files' -Description 'The files grid (Tools/OracleGrid/grid-files.tsv)'
    Test-RecordedFile -Key 'sedump_exe' -Description 'sedump.exe (2.10.03, Tools/CReference/sedump.c)'
    Test-RecordedFile -Key 'sedump_208_exe' -Description 'sedump-2.08.exe'

    # swisseph_net_dll is checked against a *fresh build* of current source rather than the
    # recorded path's current bytes -- see this script's STALE-DUMP CHECK header section for why: that
    # path only changes when scripts/run-oracle-dump.ps1 (or a manual rebuild pointed at it)
    # runs again, so rehashing it as-is would never catch a CPort/ edit that has not been
    # rebuilt into a dump yet, which is exactly the scenario this check exists for. Building
    # here adds no new dependency this script does not already have: it builds
    # Tools/OracleVerify below regardless.
    $recordedDll = $Rows['swisseph_net_dll']
    $scratchDir = Join-Path ([System.IO.Path]::GetTempPath()) "oracle-verify-dll-check-$([guid]::NewGuid())"
    try {
        $swissEphNetProj = Join-Path $RepoRoot 'SwissEphNet/SwissEphNet.csproj'
        Write-Host 'Building SwissEphNet/SwissEphNet.csproj (net10.0, Release) to check its current hash...'
        # -p:ContinuousIntegrationBuild=true, matching scripts/run-oracle-dump.ps1's own build of
        # the same project (as a dependency of Tools/OracleDump) -- without it, two builds of
        # byte-identical source hash differently just for having run at different times/paths
        # (measured: same unchanged source, ~4454d459... without the flag on one run,
        # ~7c3ba954... consistently with it, on this machine), which would make this check refuse
        # forever even right after a clean scripts/run-oracle-dump.ps1 run. Both sides of the
        # comparison need the same flag for the hashes to mean anything.
        $buildOutput = & dotnet build $swissEphNetProj -c Release -f net10.0 -o $scratchDir --nologo -v minimal -p:ContinuousIntegrationBuild=true 2>&1
        if ($LASTEXITCODE -ne 0) {
            $buildOutput | Write-Host
            $mismatches.Add('SwissEphNet/SwissEphNet.csproj failed to build while checking provenance -- cannot tell whether SwissEphNet.dll is still what the dumps were generated from.')
        }
        else {
            $freshDllPath = Join-Path $scratchDir 'SwissEphNet.dll'
            if (-not (Test-Path -LiteralPath $freshDllPath -PathType Leaf)) {
                $mismatches.Add("dotnet build reported success but $freshDllPath does not exist -- cannot check SwissEphNet.dll's provenance.")
            }
            else {
                $currentDllSha = Get-Sha256Hex -Path $freshDllPath
                if ($currentDllSha -ne $recordedDll.Sha256) {
                    $mismatches.Add("SwissEphNet.dll changed since the dumps were generated: recorded $($recordedDll.Sha256) (from $($recordedDll.Path)), a fresh build of current source now hashes to $currentDllSha.")
                }
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $scratchDir) {
            Remove-Item -LiteralPath $scratchDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # The leading comma matters: PowerShell enumerates an IEnumerable placed on the output
    # stream, so a bare `return $mismatches` would unroll a one-element List[string] into that
    # single string instead of the list -- exactly the shape scripts/run-oracle-dump.ps1's own
    # provenance-row construction hit earlier (see the comment there). The comma operator forces
    # $mismatches through as one object.
    return , $mismatches
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

    if ($overridesGiven) {
        Write-Host "NOTE: -CDumpPath/-NetDumpPath/-KnownDiffPath point this run at non-default dumps; skipping the stale-dump check against $ProvenancePath, which only describes scripts/run-oracle-dump.ps1's own default outputs." -ForegroundColor Yellow
        Write-Host ''
    }
    else {
        Write-Host "Checking dump provenance ($ProvenancePath)..."
        $provenanceRows = Read-Provenance -Path $ProvenancePath
        $mismatches = Get-ProvenanceMismatches -Rows $provenanceRows -RepoRoot $repoRoot
        if ($mismatches.Count -gt 0) {
            Write-Host ''
            Write-Host 'FAIL: the dumps no longer reflect what is on disk:' -ForegroundColor Red
            foreach ($m in $mismatches) { Write-Host "  - $m" -ForegroundColor Red }
            Write-Host ''
            throw 'Run: pwsh scripts/run-oracle-dump.ps1, then re-run this gate.'
        }
        Write-Host 'PASS: grids, SwissEphNet.dll and the sedump executables all still match the recorded provenance.' -ForegroundColor Green
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
