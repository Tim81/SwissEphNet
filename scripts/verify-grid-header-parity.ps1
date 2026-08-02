#Requires -Version 7
<#
.SYNOPSIS
    Asserts that Tools/OracleGrid/grid-files.tsv and Tools/OracleGrid/grid-jpl.tsv declare the
    identical column header.

.DESCRIPTION
    Tools/OracleGrid/gen-grid-jpl.ps1's $columnHeader (its own header comment: "grid-jpl.tsv
    carries this header byte-for-byte") is built as an independent PowerShell literal, duplicating
    Tools/OracleGrid/gen-grid-files.ps1's own $columnHeader line for line -- nothing compares the
    two, and no workflow even reads grid-jpl.tsv (it needs a 190 MB - 2.6 GB DE file this repo does
    not ship, so scripts/run-oracle-dump.ps1's JPL leg is opt-in and CI never exercises it). A
    column added to one generator's literal and not the other's would silently orphan the two grids
    from each other's schema, with nothing before this gate positioned to notice: it happened once
    already (the method/hsys columns), caught by hand rather than by any check.

    This needs no DE file and no dump run -- it compares the first non-comment line of the two
    already-committed grid TSVs directly, which is what makes it cheap enough to run on every push
    and pull request rather than only alongside the opt-in JPL leg.

.PARAMETER FilesGridPath
    Defaults to Tools/OracleGrid/grid-files.tsv.

.PARAMETER JplGridPath
    Defaults to Tools/OracleGrid/grid-jpl.tsv.

.PARAMETER SelfTest
    Plants a header mismatch between two scratch grid files and asserts this gate refuses; asserts
    it accepts when the two headers match. Touches no tracked file.
#>
[CmdletBinding()]
param(
    [string] $FilesGridPath,
    [string] $JplGridPath,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $FilesGridPath) { $FilesGridPath = Join-Path $repoRoot 'Tools/OracleGrid/grid-files.tsv' }
if (-not $JplGridPath) { $JplGridPath = Join-Path $repoRoot 'Tools/OracleGrid/grid-jpl.tsv' }

# The first line that is not a '#' comment and not blank -- both grid TSVs use '#' comment lines
# above their own column header, and gen-grid-files.ps1/gen-grid-jpl.ps1 both assert their own
# writer emits that header verbatim before any data row, so this is the same "first non-comment
# line" convention every reader of these files already relies on (see e.g.
# scripts/run-oracle-dump.ps1's Get-GridDataRowCount).
function Get-FirstNonCommentLine {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Path not found."
    }
    foreach ($line in [System.IO.File]::ReadLines($Path)) {
        if ($line.Length -eq 0) { continue }
        if ($line[0] -eq '#') { continue }
        return $line
    }
    throw "$Path has no non-comment line at all."
}

function Test-GridHeaderParity {
    param([string] $FilesPath, [string] $JplPath)
    $filesHeader = Get-FirstNonCommentLine -Path $FilesPath
    $jplHeader = Get-FirstNonCommentLine -Path $JplPath
    if ([string]::Equals($filesHeader, $jplHeader, [StringComparison]::Ordinal)) {
        return [pscustomobject]@{ Ok = $true; FilesHeader = $filesHeader; JplHeader = $jplHeader }
    }
    return [pscustomobject]@{ Ok = $false; FilesHeader = $filesHeader; JplHeader = $jplHeader }
}

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ("grid-header-parity-selftest-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        $matchingFiles = Join-Path $lab 'grid-files-match.tsv'
        $matchingJpl = Join-Path $lab 'grid-jpl-match.tsv'
        @('# comment', "case_id`tfunc`tipl`tmethod`thsys") | Set-Content -LiteralPath $matchingFiles -Encoding utf8
        @('# a different comment', "case_id`tfunc`tipl`tmethod`thsys") | Set-Content -LiteralPath $matchingJpl -Encoding utf8
        $r = Test-GridHeaderParity -FilesPath $matchingFiles -JplPath $matchingJpl
        if ($r.Ok) {
            Write-Host '  ok: matching headers (different comment lines above them) are accepted' -ForegroundColor DarkGray
        }
        else {
            Write-Host '  SELFTEST FAIL: matching headers were reported as differing' -ForegroundColor Red
            $failures++
        }

        # The reproduced bypass: grid-jpl.tsv's generator literal missing a trailing column
        # grid-files.tsv's already has (the actual method/hsys orphaning this gate exists to catch
        # the next occurrence of).
        $orphanedFiles = Join-Path $lab 'grid-files-orphaned.tsv'
        $orphanedJpl = Join-Path $lab 'grid-jpl-orphaned.tsv'
        @('# comment', "case_id`tfunc`tipl`tmethod`thsys`tiplctr`tifno") | Set-Content -LiteralPath $orphanedFiles -Encoding utf8
        @('# comment', "case_id`tfunc`tipl`tmethod`thsys") | Set-Content -LiteralPath $orphanedJpl -Encoding utf8
        $r2 = Test-GridHeaderParity -FilesPath $orphanedFiles -JplPath $orphanedJpl
        if (-not $r2.Ok) {
            Write-Host '  ok: a header that gained a column in one grid and not the other is refused' -ForegroundColor DarkGray
        }
        else {
            Write-Host '  SELFTEST FAIL: an orphaned header (one grid gained a column the other lacks) was accepted' -ForegroundColor Red
            $failures++
        }

        # A comment-only grid TSV (no data header at all) must fail loudly, not compare two empty
        # strings and call that a match.
        $commentOnly = Join-Path $lab 'comment-only.tsv'
        @('# only a comment, no header') | Set-Content -LiteralPath $commentOnly -Encoding utf8
        $threw = $false
        try { Get-FirstNonCommentLine -Path $commentOnly | Out-Null } catch { $threw = $true }
        if ($threw) {
            Write-Host '  ok: a grid TSV with no non-comment line throws rather than comparing nothing' -ForegroundColor DarkGray
        }
        else {
            Write-Host '  SELFTEST FAIL: a comment-only grid TSV did not throw' -ForegroundColor Red
            $failures++
        }

        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all verify-grid-header-parity self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$result = Test-GridHeaderParity -FilesPath $FilesGridPath -JplPath $JplGridPath
if (-not $result.Ok) {
    Write-Host "FAIL: $FilesGridPath and $JplGridPath declare different column headers." -ForegroundColor Red
    Write-Host "  $FilesGridPath`: $($result.FilesHeader)" -ForegroundColor Red
    Write-Host "  $JplGridPath`: $($result.JplHeader)" -ForegroundColor Red
    Write-Host 'Tools/OracleGrid/gen-grid-jpl.ps1''s own header comment says grid-jpl.tsv carries this header byte-for-byte with grid-files.tsv -- update whichever generator''s $columnHeader literal fell behind, then regenerate that grid.' -ForegroundColor Red
    exit 1
}
Write-Host "PASS: $FilesGridPath and $JplGridPath declare the identical column header ($($result.FilesHeader))." -ForegroundColor Green
exit 0
