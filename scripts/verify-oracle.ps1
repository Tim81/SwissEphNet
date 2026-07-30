#Requires -Version 7
<#
.SYNOPSIS
    Builds Tools/OracleVerify and runs it against the committed dumps and known-diff list: the
    gate half of the bit-exact comparison harness (scripts/run-oracle-dump.ps1 is the generation
    half).

.DESCRIPTION
    Reads external/.c-reference/dump-c-2.10.03.tsv and dump-net.tsv (both written by
    scripts/run-oracle-dump.ps1), keys rows by case_id, and checks every hex column, the integer
    return code, and the serr text. A row that does not match outright must be accounted for by
    Tests/oracle/known-diff.tsv -- see Tools/OracleVerify/OracleVerifyReport.cs for the three-way
    check this runs: a differing row absent from the list (or listed under a category whose
    failure shape no longer fits) is a regression; a listed row that now matches outright must be
    pruned; a listed row whose case_id has fallen out of the grid is stale. It also gates on
    magnitude, not just category: each known-diff.tsv row carries the maximum ULP distance
    observed the last time it was regenerated (or the literal text "categorical" for a row that
    differs by a NaN on one side and a finite value on the other, which has no meaningful
    magnitude to compare), and a row whose current ULP distance exceeds that recorded maximum, or
    whose categorical/numeric state has flipped either way, fails even though it is still "on the
    list". That is deliberately stronger than the correctness oracle's known-fail.tsv, which
    compares only the category and so cannot detect a listed mismatch quietly growing worse --
    Tests/SwissEphNet.Conformance.Tests/ConformanceReport.cs records that limitation itself.

    This script does not regenerate anything -- see scripts/regenerate-oracle-known-diff.ps1 for
    the only supported way to change Tests/oracle/known-diff.tsv.

.PARAMETER CDumpPath
    Defaults to external/.c-reference/dump-c-2.10.03.tsv.

.PARAMETER NetDumpPath
    Defaults to external/.c-reference/dump-net.tsv.

.PARAMETER KnownDiffPath
    Defaults to Tests/oracle/known-diff.tsv.
#>

[CmdletBinding()]
param(
    [string] $CDumpPath,
    [string] $NetDumpPath,
    [string] $KnownDiffPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CDumpPath) { $CDumpPath = Join-Path $repoRoot 'external/.c-reference/dump-c-2.10.03.tsv' }
if (-not $NetDumpPath) { $NetDumpPath = Join-Path $repoRoot 'external/.c-reference/dump-net.tsv' }
if (-not $KnownDiffPath) { $KnownDiffPath = Join-Path $repoRoot 'Tests/oracle/known-diff.tsv' }

$CDumpPath = [System.IO.Path]::GetFullPath($CDumpPath)
$NetDumpPath = [System.IO.Path]::GetFullPath($NetDumpPath)
$KnownDiffPath = [System.IO.Path]::GetFullPath($KnownDiffPath)

function Fail($message) {
    # Thrown, not exited: matches scripts/run-oracle-dump.ps1's own Fail helper, so every failure
    # in this script gets the same "FAIL: ..." banner from the catch block below.
    throw $message
}

$exitCode = 0
try {
    # Missing-dump and zero-row/row-count-mismatch checks both matter here, but only the first is
    # this script's own job: a missing dump means nothing has been generated yet, which is a
    # different problem from "generated, but the two sides disagree", and only the first one has
    # an obvious next step (run the other script) worth naming explicitly. Row-count and case-id-set
    # mismatches are caught by OracleVerify.exe itself (Tools/OracleVerify/Program.cs's
    # LoadAndCompare), which already produces a specific, actionable message for each -- duplicating
    # that logic here would just be a second place for the two checks to drift out of sync.
    if (-not (Test-Path -LiteralPath $CDumpPath -PathType Leaf)) {
        Fail "C dump not found at $CDumpPath. Run: pwsh scripts/run-oracle-dump.ps1"
    }
    if (-not (Test-Path -LiteralPath $NetDumpPath -PathType Leaf)) {
        Fail ".NET dump not found at $NetDumpPath. Run: pwsh scripts/run-oracle-dump.ps1"
    }

    $project = Join-Path $repoRoot 'Tools/OracleVerify/OracleVerify.csproj'
    Write-Host "Building $project (Release)..."
    $buildOutput = & dotnet build $project -c Release --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        Fail 'dotnet build Tools/OracleVerify failed.'
    }

    Write-Host ''
    Write-Host "C dump:        $CDumpPath"
    Write-Host ".NET dump:     $NetDumpPath"
    Write-Host "Known-diff:    $KnownDiffPath"
    Write-Host ''

    & dotnet run --project $project -c Release --no-build -- verify $CDumpPath $NetDumpPath $KnownDiffPath
    if ($LASTEXITCODE -ne 0) {
        $exitCode = $LASTEXITCODE
    }
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}

exit $exitCode
