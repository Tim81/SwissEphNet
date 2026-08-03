#Requires -Version 7
<#
.SYNOPSIS
    Cross-checks Tools/CReference/build-c.ps1's MSVC build against pyswisseph 2.10.03.

.DESCRIPTION
    Tools/CReference/build-c.ps1 compiles Astrodienst's own C into external/.c-reference/. Those
    binaries become the reference side of a bit-exact oracle for this C# port, so a miscompile
    there would make every downstream comparison wrong while still looking green. This script is
    the check on the build itself, before anything else trusts it.

    The check is a trust hop, the same shape as scripts/validate-pyswisseph.py: pyswisseph
    packages the same upstream libswe 2.10.03. Its Windows wheel is also MSVC-built from the same
    upstream C, so this is not independent of MSVC as a compiler family -- the independence is in
    build configuration: a different translation-unit list, different flags, none of this repo's
    swetest patch (see New-PatchedSwetestSource in Tools/CReference/build-c.ps1), and different
    linkage. Run the MSVC-built swetest.exe and pyswisseph over the same fixed inputs; if they
    agree, the build is sound. This does not replace scripts/verify-baseline.ps1 or the
    conformance oracle -- it only answers "did this local build come out right", once, before
    either of those gates spends any trust on it.

    Agreement on values proves nothing about which pyswisseph was actually compared against
    unless the version is read and asserted, not assumed -- this script fails outright if
    pyswisseph does not report exactly swe.version == '2.10.03', and prints the Python
    executable, the pyswisseph version, and the resolved ephemeris directory before running any
    comparison, the reference-side counterpart to the toolchain.txt build-c.ps1 already writes
    for the build side.

    Fixed inputs cover five call shapes: geocentric planets, the Moon, the Sun in sidereal
    coordinates (SEFLG_SIDEREAL, iflag 65538 -- swe_get_ayanamsa is never called here), a house
    calculation, and a date away from J2000 (1950, which exercises a different
    precession/nutation regime than J2000 itself). swetest prints rounded decimal text, so the
    comparison rounds pyswisseph's double to the same number of decimal places swetest printed
    and compares the two strings -- no invented tolerance looser than what swetest itself throws
    away.

.PARAMETER CReferenceDir
    Where build-c.ps1 wrote its artifacts. Defaults to external/.c-reference.

.PARAMETER EpheDir
    Ephemeris directory passed to both swetest.exe (-edir) and pyswisseph (set_ephe_path).
    Defaults to external/swisseph/ephe.

.PARAMETER Python
    Python executable to run pyswisseph under. Defaults to 'python'.

.NOTES
    Run in CI: .github/workflows/oracle.yml's c-reference-validate job installs pyswisseph and
    runs this script as a gate after Tools/CReference/build-c.ps1's MSVC build. Also run by hand
    locally (Python with pyswisseph installed, plus the local MSVC build
    Tools/CReference/build-c.ps1 produces) after any change to that build or to this script itself.
#>
[CmdletBinding()]
param(
    [string] $CReferenceDir,
    [string] $EpheDir,
    [string] $Python = 'python'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $CReferenceDir) { $CReferenceDir = Join-Path $repoRoot 'external/.c-reference' }
if (-not $EpheDir) { $EpheDir = Join-Path $repoRoot 'external/swisseph/ephe' }

# Resolved once, up front, so a missing Python fails here with a clear message instead of as an
# opaque "file not found" from the & $Python invocation much further down, and so the full,
# resolved path -- not just whatever string -Python was passed -- can be printed as part of this
# script's own provenance (see the pyswisseph_version block below).
try {
    $resolvedPythonPath = (Get-Command $Python -ErrorAction Stop).Source
}
catch {
    Write-Host "FAIL: '$Python' did not resolve to a Python executable on PATH."
    exit 2
}

$swetestExe = Join-Path $CReferenceDir 'swetest.exe'
$toolchainFile = Join-Path $CReferenceDir 'toolchain.txt'

if (-not (Test-Path -LiteralPath $swetestExe) -or -not (Test-Path -LiteralPath $toolchainFile)) {
    Write-Host "FAIL: $swetestExe or $toolchainFile not found."
    Write-Host 'Run Tools/CReference/build-c.ps1 first, then re-run this script.'
    exit 1
}

# A missing ephemeris file does not fail loudly on its own: swe_calc and swetest both fall back
# to Moshier and keep printing well-formed output, which build-c.ps1's own smoke run (see
# Invoke-SwetestSmoke there) exists to catch for the build side. Checking here too means a
# missing file fails before the first swetest.exe invocation below, rather than showing up later
# as an unexplained value mismatch that looks like a real MSVC/pyswisseph disagreement.
if (-not (Test-Path -LiteralPath $EpheDir -PathType Container)) {
    Write-Host "FAIL: ephemeris directory not found at $EpheDir."
    exit 1
}
$requiredEpheFilesPath = Join-Path $repoRoot 'Tests/conformance/required-ephemeris-files.tsv'
if (-not (Test-Path -LiteralPath $requiredEpheFilesPath -PathType Leaf)) {
    Write-Host "FAIL: required ephemeris file list not found at $requiredEpheFilesPath."
    exit 1
}
$requiredEpheFiles = @(Get-Content -LiteralPath $requiredEpheFilesPath |
    Where-Object { $_.Trim() -ne '' -and -not $_.StartsWith('#') })
$missingEpheFiles = @($requiredEpheFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $EpheDir $_) -PathType Leaf) })
if ($missingEpheFiles.Count -gt 0) {
    Write-Host "FAIL: $EpheDir is missing required ephemeris file(s): $($missingEpheFiles -join ', ')."
    exit 1
}

Write-Host 'Toolchain used to build swetest.exe:'
Get-Content -LiteralPath $toolchainFile | ForEach-Object { Write-Host "  $_" }
Write-Host ''

# ---------------------------------------------------------------------------------------
# Fixed inputs. Planet numbers are swetest's -p digit codes (0=Sun .. 9=Pluto) and also
# pyswisseph's swe.calc ipl constants -- the two happen to be the same integers, verified
# against -hplan and the swisseph module's own SUN..PLUTO constants.
# ---------------------------------------------------------------------------------------

$PlanetNames = @{ 0 = 'Sun'; 1 = 'Moon'; 2 = 'Mercury'; 3 = 'Venus'; 4 = 'Mars'; 5 = 'Jupiter'; 6 = 'Saturn'; 7 = 'Uranus'; 8 = 'Neptune'; 9 = 'Pluto' }

# SEFLG_SWIEPH = 2, SEFLG_SIDEREAL = 65536. Jd is TT (Ephemeris Time): swetest's -bjJD with no
# -ut flag feeds the Julian day straight to swe_calc as TT, the same value swe.calc(jd_tt, ...)
# takes -- verified by comparing -bj2451545.0 (no -ut) against pyswisseph's calc(2451545.0, ...)
# directly, no delta-t conversion needed on either side.
$calcCases = @(
    [pscustomobject]@{ Case = 'planets-j2000';         Jd = 2451545.0; Bodies = @(0, 2, 3, 4, 5, 6, 7, 8, 9); Iflag = 2;     SidMode = $null }
    [pscustomobject]@{ Case = 'moon-j2000';             Jd = 2451545.0; Bodies = @(1);                          Iflag = 2;     SidMode = $null }
    [pscustomobject]@{ Case = 'sidereal-lahiri-j2000';  Jd = 2451545.0; Bodies = @(0);                          Iflag = 65538; SidMode = 1 }
    [pscustomobject]@{ Case = 'planets-1950';           Jd = 2433282.5; Bodies = @(0, 1, 3);                    Iflag = 2;     SidMode = $null }
)

# swe_houses takes jd_ut directly, so this one input uses -bjJD -ut instead: the Julian day is
# UT, matching swe.houses(jd_ut, ...) with no conversion on either side either.
$housesCase = [pscustomobject]@{ Case = 'houses-placidus-j2000'; JdUt = 2451545.0; Lat = 52.0; Lon = 10.0; Hsys = 'P' }

# swetest prints Ascendant, MC, ARMC, Vertex, equatorial Ascendant, co-Ascendant (Koch),
# co-Ascendant (Munkasey), Polar Ascendant, in that fixed order, right after the 12 house cusps.
# pyswisseph's ascmc tuple is the same 8 values in the same order -- verified directly against
# swetest's output for this exact case.
$AscmcLabels = @('Ascendant', 'MC', 'ARMC', 'Vertex', 'equat. Asc.', 'co-Asc. W.Koch', 'co-Asc Munkasey', 'Polar Asc.')

# ---------------------------------------------------------------------------------------
# Run swetest.exe. PowerShell's argument parser mangles options like -b1.1.2000 (it splits
# on the dots), so the command is built as one string and run through cmd /c, which does not.
# ---------------------------------------------------------------------------------------

function Invoke-Swetest {
    param([string] $Arguments)

    $full = "`"$swetestExe`" $Arguments"
    $output = cmd /c $full 2>&1
    # All five call sites below feed this function's output straight into a line-count assertion,
    # which happened to catch a nonzero exit incidentally (a Moshier fallback or a real crash
    # both add or remove lines and usually shift the count). That was never a checked, intentional
    # guard -- $LASTEXITCODE itself was never read. build-c.ps1 already checks the exit code of
    # this same binary for the same reason.
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: swetest.exe exited $LASTEXITCODE."
        Write-Host "  command: swetest.exe $Arguments"
        $output | ForEach-Object { Write-Host "  output: $_" }
        exit 1
    }
    return , @($output)
}

$swetestResults = [System.Collections.Generic.Dictionary[string, object]]::new()

foreach ($c in $calcCases) {
    $bodyDigits = ($c.Bodies -join '')
    $sidArg = if ($null -ne $c.SidMode) { " -sid$($c.SidMode)" } else { '' }
    # Not $args -- that is PowerShell's automatic variable for a function/script's unbound
    # arguments, and this script runs at script scope, not inside a function, so overwriting it
    # here is silently harmless today but a foot-gun waiting for the next edit that adds one.
    $calcArgs = "-bj$($c.Jd) -p$bodyDigits -eswe -edir`"$EpheDir`" -fPlbR -head$sidArg"
    $lines = @((Invoke-Swetest $calcArgs) | Where-Object { $_ -ne '' })

    if ($lines.Count -ne $c.Bodies.Count) {
        Write-Host "FAIL: swetest case '$($c.Case)' returned $($lines.Count) line(s), expected $($c.Bodies.Count)."
        Write-Host "  command: swetest.exe $calcArgs"
        $lines | ForEach-Object { Write-Host "  output: $_" }
        exit 1
    }

    $rows = [System.Collections.Generic.Dictionary[string, object]]::new()
    for ($i = 0; $i -lt $c.Bodies.Count; $i++) {
        $name = $PlanetNames[$c.Bodies[$i]]
        $m = [regex]::Match($lines[$i], '^(?<name>\S+)\s+(?<l>-?\d+\.\d+)\s+(?<b>-?\d+\.\d+)\s+(?<r>-?\d+\.\d+)\s*$')
        if (-not $m.Success -or $m.Groups['name'].Value -ne $name) {
            Write-Host "FAIL: could not parse swetest line for '$name' in case '$($c.Case)': $($lines[$i])"
            exit 1
        }
        $rows[$name] = [pscustomobject]@{ l = $m.Groups['l'].Value; b = $m.Groups['b'].Value; r = $m.Groups['r'].Value }
    }
    $swetestResults[$c.Case] = $rows
}

# -p0 keeps the planet block to a single line (Sun), which this case discards -- only the
# houses and Ascendant/MC/etc. block that follows it is compared.
$housesArgs = "-bj$($housesCase.JdUt) -ut -p0 -house$($housesCase.Lon),$($housesCase.Lat),$($housesCase.Hsys) -eswe -edir`"$EpheDir`" -fPl -head"
$housesLines = @((Invoke-Swetest $housesArgs) | Where-Object { $_ -ne '' })
$expectedHouseLineCount = 1 + 12 + $AscmcLabels.Count
if ($housesLines.Count -ne $expectedHouseLineCount) {
    Write-Host "FAIL: swetest houses case returned $($housesLines.Count) line(s), expected $expectedHouseLineCount."
    Write-Host "  command: swetest.exe $housesArgs"
    $housesLines | ForEach-Object { Write-Host "  output: $_" }
    exit 1
}

$houseRows = [System.Collections.Generic.Dictionary[string, object]]::new()
for ($i = 0; $i -lt 12; $i++) {
    $line = $housesLines[1 + $i]
    $m = [regex]::Match($line, '(-?\d+\.\d+)\s*$')
    if (-not $m.Success) {
        Write-Host "FAIL: could not parse house cusp line: $line"
        exit 1
    }
    $houseRows["house$($i + 1)"] = [pscustomobject]@{ l = $m.Groups[1].Value }
}
for ($i = 0; $i -lt $AscmcLabels.Count; $i++) {
    $line = $housesLines[13 + $i]
    $m = [regex]::Match($line, '(-?\d+\.\d+)\s*$')
    if (-not $m.Success) {
        Write-Host "FAIL: could not parse $($AscmcLabels[$i]) line: $line"
        exit 1
    }
    $houseRows[$AscmcLabels[$i]] = [pscustomobject]@{ l = $m.Groups[1].Value }
}
$swetestResults[$housesCase.Case] = $houseRows

# ---------------------------------------------------------------------------------------
# Run the equivalent calls through pyswisseph, against the same ephemeris directory.
# ---------------------------------------------------------------------------------------

# $resolvedPythonPath is resolved once, near the top of this script, from $Python; used below
# only for the provenance printout, not for running anything.
$pyLines = [System.Collections.Generic.List[string]]::new()
$pyLines.Add('import sys')
$pyLines.Add('import swisseph as swe')
# The whole point of this script is agreement with pyswisseph 2.10.03 specifically -- see
# .DESCRIPTION. Nothing else in this script reads swe.version, so without this check, a 2.08 or
# 2.10.02 pyswisseph either fails unattributably on real differences, or silently passes on
# whatever did not change between versions, and that pass gets recorded as "the 2.10.03 reference
# build is validated" when it validated nothing of the sort. Exit code 3 is this script's own
# signal, distinct from an ordinary Python exception (any other nonzero exit).
$pyLines.Add("if swe.version != '2.10.03':")
$pyLines.Add("    print(f'pyswisseph reports version {swe.version}, expected 2.10.03', file=sys.stderr)")
$pyLines.Add('    sys.exit(3)')
$pyLines.Add("print(f'pyswisseph_version\t{swe.version}')")
$pyLines.Add("swe.set_ephe_path(r'$EpheDir')")
$pyLines.Add('rows = []')
foreach ($c in $calcCases) {
    if ($null -ne $c.SidMode) {
        $pyLines.Add("swe.set_sid_mode($($c.SidMode))")
    }
    foreach ($b in $c.Bodies) {
        $name = $PlanetNames[$b]
        $pyLines.Add("xx, _rc = swe.calc($($c.Jd), $b, $($c.Iflag))")
        $pyLines.Add("rows.append(('$($c.Case)', '$name', 'l', format(xx[0], '.7f')))")
        $pyLines.Add("rows.append(('$($c.Case)', '$name', 'b', format(xx[1], '.7f')))")
        $pyLines.Add("rows.append(('$($c.Case)', '$name', 'r', format(xx[2], '.9f')))")
    }
}
$pyLines.Add("cusps, ascmc = swe.houses($($housesCase.JdUt), $($housesCase.Lat), $($housesCase.Lon), b'$($housesCase.Hsys)')")
$pyLines.Add('for i in range(12):')
$pyLines.Add("    rows.append(('$($housesCase.Case)', 'house' + str(i + 1), 'l', format(cusps[i], '.7f')))")
$labelsLiteral = "[" + (($AscmcLabels | ForEach-Object { "'$_'" }) -join ', ') + "]"
$pyLines.Add("labels = $labelsLiteral")
$pyLines.Add('for lbl, val in zip(labels, ascmc):')
$pyLines.Add("    rows.append(('$($housesCase.Case)', lbl, 'l', format(val, '.7f')))")
$pyLines.Add('for r in rows:')
$pyLines.Add("    print('\t'.join(r))")

$pyScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "validate-c-reference-$([System.Guid]::NewGuid().ToString('N')).py"
[System.IO.File]::WriteAllLines($pyScriptPath, $pyLines)

try {
    $pyOutput = & $Python $pyScriptPath 2>&1
    $pyExitCode = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $pyScriptPath -Force -ErrorAction SilentlyContinue
}

if ($pyExitCode -eq 3) {
    Write-Host 'FAIL: pyswisseph is not version 2.10.03 -- this script only validates agreement with the 2.10.03 reference build (see .DESCRIPTION).'
    $pyOutput | ForEach-Object { Write-Host "  $_" }
    exit 3
}
if ($pyExitCode -ne 0) {
    Write-Host 'FAIL: pyswisseph run failed.'
    $pyOutput | ForEach-Object { Write-Host "  $_" }
    exit 2
}

$pyswissephVersionLine = $pyOutput | Where-Object { $_ -match '^pyswisseph_version\t' } | Select-Object -First 1
if (-not $pyswissephVersionLine) {
    Write-Host 'FAIL: pyswisseph run produced no pyswisseph_version line -- cannot confirm which pyswisseph version this validated against.'
    $pyOutput | ForEach-Object { Write-Host "  $_" }
    exit 2
}
$pyswissephVersion = ($pyswissephVersionLine -split "`t")[1]
if ($pyswissephVersion -ne '2.10.03') {
    Write-Host "FAIL: pyswisseph reports version $pyswissephVersion, expected 2.10.03."
    exit 3
}

Write-Host 'Reference side (pyswisseph):'
Write-Host "  python executable    $resolvedPythonPath"
Write-Host "  pyswisseph version   $pyswissephVersion"
Write-Host "  ephemeris directory  $EpheDir"
Write-Host ''

$pyswissephResults = [System.Collections.Generic.Dictionary[string, object]]::new()
foreach ($line in $pyOutput) {
    $fields = $line -split "`t"
    if ($fields.Count -ne 4) { continue }
    $case, $label, $field, $value = $fields
    if (-not $pyswissephResults.ContainsKey($case)) {
        $pyswissephResults[$case] = [System.Collections.Generic.Dictionary[string, object]]::new()
    }
    if (-not $pyswissephResults[$case].ContainsKey($label)) {
        $pyswissephResults[$case][$label] = [System.Collections.Generic.Dictionary[string, string]]::new()
    }
    $pyswissephResults[$case][$label][$field] = $value
}

# ---------------------------------------------------------------------------------------
# Compare. Both sides are already rounded to the precision swetest prints (7 decimal places
# for longitude/latitude, 9 for distance), so this is a string comparison at that precision --
# not a tolerance invented to be looser than what swetest itself throws away.
# ---------------------------------------------------------------------------------------

$compared = 0
$failed = 0

Write-Host 'Case                       Label            Field  swetest.exe     pyswisseph      Result'
Write-Host ('-' * 90)

foreach ($caseName in $swetestResults.Keys) {
    $pyCase = $pyswissephResults[$caseName]
    foreach ($label in $swetestResults[$caseName].Keys) {
        $swetestRow = $swetestResults[$caseName][$label]
        $pyRow = $pyCase[$label]
        foreach ($field in $swetestRow.PSObject.Properties.Name) {
            $expected = $swetestRow.$field
            $actual = $pyRow[$field]
            $compared++
            if ($expected -eq $actual) {
                Write-Host ('{0,-26} {1,-16} {2,-6} {3,-15} {4,-15} PASS' -f $caseName, $label, $field, $expected, $actual)
            }
            else {
                Write-Host ('{0,-26} {1,-16} {2,-6} {3,-15} {4,-15} FAIL' -f $caseName, $label, $field, $expected, $actual)
                $failed++
            }
        }
    }
}

Write-Host ''
Write-Host "Compared: $compared  Failed: $failed"

if ($compared -eq 0) {
    Write-Host 'FAIL: zero cases were compared. That proves nothing, so it does not count as a pass.'
    exit 1
}

if ($failed -gt 0) {
    Write-Host 'FAIL: the MSVC build and pyswisseph disagree on at least one case.'
    exit 1
}

Write-Host 'PASS: the MSVC build agrees with pyswisseph on every case.'
exit 0
