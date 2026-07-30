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
#>

[CmdletBinding()]
param(
    [ValidateSet('Analytic', 'Files', 'Both')]
    [string] $Grid = 'Both',

    [string] $CDumpPath,
    [string] $NetDumpPath,
    [string] $KnownDiffPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

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

$exitCode = 0
try {
    $project = Join-Path $repoRoot 'Tools/OracleVerify/OracleVerify.csproj'
    Write-Host "Building $project (Release)..."
    $buildOutput = & dotnet build $project -c Release --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        throw 'dotnet build Tools/OracleVerify failed.'
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
