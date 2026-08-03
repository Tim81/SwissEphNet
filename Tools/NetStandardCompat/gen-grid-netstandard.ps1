#Requires -Version 7.3
<#
.SYNOPSIS
    Regenerates grid-netstandard.tsv, the committed input set for the netstandard2.0-vs-net10.0
    swe_calc comparison (scripts/verify-netstandard-compat.ps1).

.DESCRIPTION
    README.md's "V:2.10.3" section asserts, in prose, that .NET Framework 4.8 differs from .NET 10
    on some of a `swe_calc` sweep, because .NET Framework 4.8's `Math.Sin`/`Math.Tan` are less
    accurate near pi -- and that net8.0/net10.0 agree on all of it. Nothing before this addition
    reproduced that measurement: it was taken once, ad hoc, and never wired to a committed
    instrument. This script is the input half of that instrument: the ONLY place that decides
    which `swe_calc` calls get compared. Tools/NetStandardCompat/NetStandardCompatDump/Program.cs
    replays this file under each target framework; it builds no grid of its own.

    BODIES

    Two disjoint sets, 34 bodies total:

      Real bodies, ipl 0-14 (SE_SUN..SE_EARTH: Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn,
      Uranus, Neptune, Pluto, mean/true node, mean/oscillating apogee, Earth). The same 15 bodies
      Tools/OracleGrid/gen-grid-analytic.ps1's own $Bodies sweeps, and for the same reason: under
      SEFLG_MOSEPH every one of them is Moshier-analytic, so the call touches no ephemeris file
      and needs no data this repo would have to ship differently per target framework.

      Fictitious ("Hamburger"/Uranian and other) bodies, ipl 40-58 (SE_CUPIDO through
      SE_WALDEMATH, external/swisseph/swephexp.h:141-160) -- every fictitious-body constant that
      header defines. These are dispatched separately from SEFLG_MOSEPH/SEFLG_SWIEPH entirely
      (external/swisseph/swemplan.c's swi_osc_el_plan/read_elements_file): the C first tries to
      open seorbel.txt and, only when that fails, falls back to a built-in element table
      (plan_oscu_elem[]) sized SE_NFICT_ELEM = 15 (swephexp.h:134). This grid never sets a real
      ephemeris path (see SentinelEpheDir in the dump tool), so that lookup always fails and every
      row always takes the built-in-table branch: ipl 40-54 (the first 15 fictitious bodies)
      compute from it, and ipl 55-58 (Vulcan, White Moon, Proserpina, Waldemath -- the four beyond
      SE_NFICT_ELEM) hit read_elements_file's own "no elements for fictitious body" ERR return
      instead. Both are included: the ERR rows are a legitimate comparison too (same retc, same
      serr text expected on every target framework), not a gap in coverage.

    Astrodienst's earlier ad hoc note (see git history on this paragraph) cited "37 bodies"; this
    script's own two sets sum to 34. That number was never derived from a committed file before
    this addition, so this script does not attempt to reverse-engineer whatever body list would
    reproduce 37 -- see this repository's working notes on why re-deriving beats assuming. 34 is
    what SE_SUN..SE_EARTH plus every SE_* fictitious-body constant in swephexp.h actually is.

    EPOCHS

    Three Julian day numbers (TT, since swe_calc's own tjd parameter is terrestrial time, not
    UT), spread around J2000.0 the way the ad hoc note's own "1850-2050 sweep" (see the commit
    that first measured FICT_CUPIDO's ULP drift) was: 2415020.5 (1900-01-01 00:00), 2451545.0
    (J2000.0 exactly, 2000-01-01 12:00 TT) and 2488069.5 (2100-01-01 00:00). Chosen for spread, not
    because any one of them is known in advance to land a trig argument near pi -- this script
    does not assume the causal claim, scripts/verify-netstandard-compat.ps1's own measurement does.

    FLAG

    SEFLG_MOSEPH (4) OR SEFLG_SPEED (256) = 260, fixed on every row: MOSEPH for the same
    file-independence reason gen-grid-analytic.ps1 always ORs it in, and SPEED because the ad hoc
    note's own worst-case divergence was in a longitude *speed* field (xx[3]), which is not
    populated by every code path without it.

    FUNCTION

    swe_calc only (not swe_calc_ut, not swe_calc_pctr): the ad hoc note's own call-count arithmetic
    ("37 bodies, 3 epochs" = a plain body x epoch cross product) implies one function, and swe_calc
    is the one actually named.

    CALL COUNT

    34 bodies x 3 epochs = 102 rows/calls -- not the ad hoc note's 111. See
    scripts/verify-netstandard-compat.ps1 and the README section it backs for what was actually
    measured against this grid, and CONTRIBUTING.md/README.md for where the corrected numbers
    landed.

    COLUMN LAYOUT (documented again, verbatim, at the top of the generated file itself)

    Tab-separated, LF line endings, one call per data row. Lines starting with '#' are comments;
    the first non-comment line is the column-name header, which the dump tool asserts against
    verbatim: case_id, ipl, tjd, iflag.

.PARAMETER Reason
    Required (outside -SelfTest). This grid is the ONLY place that decides which swe_calc calls
    scripts/verify-netstandard-compat.ps1 compares -- every Tests/netstandard-compat/
    known-diff-<fw>.tsv file downstream of it is invalidated the instant the grid's shape changes,
    silently or otherwise. MEDIUM 5's own review: before this parameter existed, this script took
    no parameters, required no reason and appended to no log, so a change that dropped exactly the
    epochs where net48 diverges made every known-diff-<fw>.tsv legitimately header-only and every
    downstream gate green -- a shrunken grid, not a fixed divergence. Recorded to the same sidecar
    scripts/regenerate-netstandard-compat-known-diff.ps1 already writes to
    (Tests/netstandard-compat/regenerations.log), since a grid change and a known-diff regeneration
    are both "why did this instrument's measurement change" questions a reviewer needs answered the
    same way.

.PARAMETER PR
    Optional. The pull request number this regeneration belongs to -- same convention as
    scripts/regenerate-known-fail.ps1's own -PR.

.PARAMETER SelfTest
    Asserts -Reason is required (a real child-process invocation, matching
    scripts/regenerate-netstandard-compat-known-diff.ps1's own self-test of the identical guard)
    and this script's own log-line formatting, against scratch files in a temporary directory.
    Never writes grid-netstandard.tsv or Tests/netstandard-compat/regenerations.log.

.NOTES
    Deterministic by construction: no timestamps, no randomness, no machine-dependent state.
    Running this script twice with the same -Reason (and -PR) must produce a byte-identical
    grid-netstandard.tsv; the log entry it appends is the only non-deterministic output.
#>

param(
    [string] $Reason,
    # Restricted to bare digits: this value is interpolated directly into the log entry appended to
    # Tests/netstandard-compat/regenerations.log, and an unvalidated value could carry a newline
    # that forges a second, backdated-looking entry -- see scripts/classify-oracle-versions.ps1's
    # own -PR guard (MEDIUM 4's reference fix) for the identical reasoning. This script was one of
    # the five left unguarded when that one shipped.
    [ValidatePattern('^\d+$')]
    [string] $PR,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# MEDIUM 4's own fix, continued: -Reason must not carry a newline either, for the identical reason
# -PR is restricted to bare digits above -- it too is interpolated directly into the log entry.
# Matches scripts/classify-oracle-versions.ps1's own guard. Checked here, ahead of -SelfTest below,
# so -SelfTest's own real child-process cases can exercise it without ever writing the grid.
if ($Reason -and ($Reason -match "`r" -or $Reason -match "`n")) {
    Write-Error "-Reason must not contain a newline: it is written directly into Tests/netstandard-compat/regenerations.log, and a newline could be read as the start of a second, forged log entry."
    exit 1
}

$outputPath = Join-Path $PSScriptRoot 'grid-netstandard.tsv'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logPath = Join-Path $repoRoot 'Tests/netstandard-compat/regenerations.log'

function Get-DataRowCount {
    # Data rows only: '#'-prefixed comment lines and the column-name header line excluded, matching
    # this script's own generated-file shape (see $headerLines/$columnHeader below).
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 0 }
    $lines = @(Get-Content -LiteralPath $Path)
    $dataLines = @($lines | Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') -and $_ -ne 'case_id	ipl	tjd	iflag' })
    return $dataLines.Count
}

function Write-RegenerationLogLine {
    param([int] $Before, [int] $After, [string] $ReasonText, [string] $PrNumber)
    $prLabel = if ([string]::IsNullOrWhiteSpace($PrNumber)) { 'no PR yet' } else { "PR #$PrNumber" }
    $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    $delta = if ($After -eq $Before) { 'no change in row count' }
    elseif ($After -gt $Before) { "$($After - $Before) more row(s)" }
    else { "$($Before - $After) fewer rows" }
    $line = "$date $prLabel (grid-netstandard.tsv: $Before -> $After, $delta): $ReasonText"
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8NoBOM
    return $line
}

if ($SelfTest) {
    $failures = 0
    $lab = Join-Path ([System.IO.Path]::GetTempPath()) ('gen-grid-netstandard-selftest-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $lab | Out-Null
    try {
        function Assert-True {
            param([string] $Case, [bool] $Condition, [string] $Detail = '')
            if ($Condition) { Write-Host "  PASS  $Case" -ForegroundColor DarkGray }
            else { Write-Host "  FAIL  $Case`n          $Detail" -ForegroundColor Red; $script:failures++ }
        }

        Write-Host 'gen-grid-netstandard self-test'
        Write-Host ''

        # -Reason is required outside -SelfTest -- run as a real child-process invocation so the
        # top-level guard actually fires, matching
        # scripts/regenerate-netstandard-compat-known-diff.ps1's own self-test of the identical
        # guard.
        $pwshExe = (Get-Process -Id $PID).Path
        $output = & $pwshExe -NoProfile -File $PSCommandPath *>&1
        $code = $LASTEXITCODE
        $text = (@($output) -join "`n")
        Assert-True '-Reason is required and refused before the grid is written' `
            ($code -ne 0 -and $text -match '-Reason is required') "exit=$code output: $text"

        # MEDIUM 4's own fix: -PR is restricted to bare digits, and -Reason must not contain a
        # newline -- both are interpolated directly into Tests/netstandard-compat/regenerations.log,
        # and the demonstrated bypass forges a second, backdated-looking entry through either one.
        $prOutput = & $pwshExe -NoProfile -File $PSCommandPath -Reason 'test' -PR "34`n2020-01-01 forged entry" *>&1
        $prCode = $LASTEXITCODE
        Assert-True '-PR rejects a value that is not bare digits (log-injection guard)' `
            ($prCode -ne 0) "exit=$prCode output: $(@($prOutput) -join ' | ')"

        $reasonOutput = & $pwshExe -NoProfile -File $PSCommandPath -Reason "line one`nline two" *>&1
        $reasonCode = $LASTEXITCODE
        $reasonText = (@($reasonOutput) -join "`n")
        Assert-True '-Reason rejects a value containing a newline (log-injection guard)' `
            ($reasonCode -ne 0 -and $reasonText -match 'must not contain a newline') "exit=$reasonCode output: $reasonText"

        $script:logPath = Join-Path $lab 'regenerations.log'
        New-Item -ItemType File -Path $script:logPath -Force | Out-Null

        $lineGrew = Write-RegenerationLogLine -Before 96 -After 102 -ReasonText 'test' -PrNumber '99'
        Assert-True 'growth is logged as "N more row(s)"' ($lineGrew -match '96 -> 102, 6 more row\(s\)') "got: $lineGrew"

        $lineSame = Write-RegenerationLogLine -Before 102 -After 102 -ReasonText 'test' -PrNumber '99'
        Assert-True 'no change is logged as "no change in row count"' ($lineSame -match '102 -> 102, no change in row count') "got: $lineSame"

        $lineNoPr = Write-RegenerationLogLine -Before 0 -After 102 -ReasonText 'test' -PrNumber ''
        Assert-True 'an empty -PR logs the "no PR yet" placeholder' ($lineNoPr -match 'no PR yet') "got: $lineNoPr"

        $gridFixture = Join-Path $lab 'grid-fixture.tsv'
        [System.IO.File]::WriteAllText($gridFixture, (@(
            '# a comment line'
            'case_id	ipl	tjd	iflag'
            "NSC|0|2415020.5`t0`t2415020.5`t260"
            "NSC|0|2451545`t0`t2451545`t260"
        ) -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
        Assert-True 'Get-DataRowCount counts only data rows, not comments or the header' `
            ((Get-DataRowCount -Path $gridFixture) -eq 2)
        Assert-True 'Get-DataRowCount on a nonexistent file is zero' `
            ((Get-DataRowCount -Path (Join-Path $lab 'does-not-exist.tsv')) -eq 0)

        Write-Host ''
        if ($failures -gt 0) {
            Write-Host "FAIL: $failures self-test case(s) did not behave as required." -ForegroundColor Red
            exit 1
        }
        Write-Host 'PASS: all gen-grid-netstandard self-test cases behaved as required.' -ForegroundColor Green
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($Reason)) {
    Write-Error "-Reason is required: this grid is the only place that decides which swe_calc calls scripts/verify-netstandard-compat.ps1 compares, and every known-diff-<fw>.tsv file downstream of it is invalidated the moment the grid's shape changes. See this script's own -Reason parameter help."
    exit 1
}

$beforeRowCount = Get-DataRowCount -Path $outputPath

# swephexp.h constants used below, named rather than inlined -- same reasoning as
# Tools/OracleGrid/gen-grid-analytic.ps1's own constant block.
$SEFLG_MOSEPH = 4
$SEFLG_SPEED  = 256
$IFlag        = $SEFLG_MOSEPH -bor $SEFLG_SPEED

function Fmt {
    param([double] $Value)
    return $Value.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}

function FmtI {
    param([int] $Value)
    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# Real bodies: SE_SUN(0)..SE_EARTH(14) -- see this script's own .DESCRIPTION.
$RealBodies = 0..14

# Fictitious bodies: SE_CUPIDO(40)..SE_WALDEMATH(58) -- every SE_* fictitious-body constant
# external/swisseph/swephexp.h:141-160 defines, in ascending order.
$FictitiousBodies = 40..58

$Bodies = @($RealBodies) + @($FictitiousBodies)

# 1900-01-01 00:00 UT, J2000.0 (2000-01-01 12:00 TT), 2100-01-01 00:00 UT -- see this script's own
# .DESCRIPTION for why these three.
$Epochs = @(2415020.5, 2451545.0, 2488069.5)

function New-CalcRow {
    param([int] $Ipl, [double] $Tjd)
    $caseId = "NSC|$(FmtI $Ipl)|$(Fmt $Tjd)"
    $fields = @($caseId, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag))
    return ($fields -join "`t")
}

$rows = [System.Collections.Generic.List[string]]::new()
foreach ($ipl in $Bodies) {
    foreach ($tjd in $Epochs) {
        $rows.Add((New-CalcRow -Ipl $ipl -Tjd $tjd))
    }
}

$headerLines = @(
    '# Tools/NetStandardCompat/grid-netstandard.tsv -- generated by gen-grid-netstandard.ps1. Do'
    '# not hand-edit; re-run that script instead. See its own header comment for the full'
    '# rationale (34 bodies: SE_SUN..SE_EARTH plus every SE_* fictitious-body constant, x 3'
    '# epochs, SEFLG_MOSEPH|SEFLG_SPEED, swe_calc only -- 102 rows).'
    '#'
    '# case_id  func-agnostic (this grid has one func, swe_calc, so no func column)'
    '# ipl      body number (external/swisseph/swephexp.h)'
    '# tjd      Julian day, terrestrial time'
    '# iflag    SEFLG_MOSEPH|SEFLG_SPEED (260), fixed on every row'
)
$columnHeader = 'case_id' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag'

$writer = [System.IO.StreamWriter]::new($outputPath, $false, [System.Text.UTF8Encoding]::new($false))
try {
    $writer.NewLine = "`n"
    foreach ($headerLine in $headerLines) { $writer.WriteLine($headerLine) }
    $writer.WriteLine($columnHeader)
    foreach ($row in $rows) { $writer.WriteLine($row) }
}
finally {
    $writer.Dispose()
}

$afterRowCount = Get-DataRowCount -Path $outputPath
$logLine = Write-RegenerationLogLine -Before $beforeRowCount -After $afterRowCount -ReasonText $Reason -PrNumber $PR

Write-Host "PASS: wrote $($rows.Count) data row(s) to $outputPath" -ForegroundColor Green
Write-Host "  Real bodies        $($RealBodies.Count)"
Write-Host "  Fictitious bodies  $($FictitiousBodies.Count)"
Write-Host "  Epochs             $($Epochs.Count)"
Write-Host "Logged: $logLine"
