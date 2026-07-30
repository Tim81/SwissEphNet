#Requires -Version 7.3
<#
.SYNOPSIS
    Regenerates grid-files.tsv, the committed input set for the bit-exact oracle harness's
    second stage: the file-backed code paths grid-analytic.tsv cannot reach.

.DESCRIPTION
    Tools/BaselineGen/Program.cs says outright that the characterization baseline uses only
    Moshier/analytic paths and never subscribes to OnLoadFile, and grid-analytic.tsv inherited
    that same restriction (every swe_calc/swe_calc_ut row there OR-s in SEFLG_MOSEPH). That
    leaves read_const, do_fread, get_new_segment, rot_back, swi_get_denum, load_dpsi_deps,
    swe_close, free_planets and the sefstars.txt fixed-star path with no bit-level coverage at
    all -- exactly the largest remaining slice of sweph.c's porting queue. This grid is the first
    one that actually opens the shipped .se1/.txt files.

    Same shape as gen-grid-analytic.ps1 and the same reason for existing as a separate,
    committed file: Tools/CReference/sedump.c and Tools/OracleDump/Program.cs both replay it
    rather than building their own inputs, so the two drivers cannot silently drift apart on
    what gets tested.

    COVERAGE

      swe_calc / swe_calc_ut -- bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating apogee,
                                 Earth), crossed with ten Julian days chosen to fall inside the
                                 two ephemeris files this repo ships (sepl/semo/seas_12.se1 covers
                                 calendar years 1200-1799, _18.se1 covers 1800-2399 -- see
                                 Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs's
                                 NeedsEraFileWeDoNotShip remarks for where those two numbers come
                                 from) and six iflag combinations, every one carrying
                                 SEFLG_SWIEPH so the port actually reads a segment instead of
                                 falling back to Moshier. Two of the ten dates (1790, 1810) sit on
                                 opposite sides of the 1800 file boundary on purpose, so the grid
                                 as a whole exercises both files, not just one; the other eight
                                 are spread widely enough inside each file's span that they cannot
                                 all land in a single segment.

      swe_fixstar / swe_fixstar_ut / swe_fixstar2 / swe_fixstar2_ut / swe_fixstar_mag -- eight
                                 star search strings spanning every input shape
                                 SwissEphNet/CPort/Sweph.cs's fixstar_format_search_name branches
                                 on (a plain traditional name, a multi-word traditional name, a
                                 leading-comma Bayer-designation-only string, and a
                                 "name,bayer" combined string), crossed with three Julian days
                                 (reusing three of the ten SWIEPH dates above) and two iflag
                                 combinations. swe_fixstar_mag takes no date or flag at all, so it
                                 gets one row per star name only.

      swe_get_planet_name -- every body number that resolves through a named switch case
                                 (0-22: Sun..Pluto, the four lunar-apogee/node variants, Earth,
                                 Chiron, Pholus, Ceres, Pallas, Juno, Vesta, the two interpolated
                                 apogee/perigee bodies) plus one arbitrary main-belt asteroid
                                 number (10005, Astraea) that this repo's sparse ephemeris
                                 checkout has no per-asteroid data file for, so that row exercises
                                 the file-not-found branch of the asteroid-name lookup on both
                                 sides instead of a case this port never has to open a file for.

    THE STAR NAME LOOKUP AND THE PLANET NAME ARE CARRIED IN THE err/serr COLUMN

    swe_get_planet_name returns a string, not a double, so it has no xx[]/mag output to hex-encode
    at all -- a GET_PLANET_NAME row has zero value columns, and the returned name is written into
    the same column both sedump.c and OracleDump already use for swe_calc's serr text. Comparing
    that column ordinal, byte for byte, is exactly the check a planet name needs; it costs no new
    column and no new comparison logic in Tools/OracleVerify. A GET_PLANET_NAME row that differs
    therefore shows up in Tools/OracleVerify's report as an "err differs" row, which is the honest
    description of what actually differs -- see FieldLabels.cs in Tools/OracleVerify.

    A FRESH LIBRARY INSTANCE PER ROW, PLUS A FRESH swe_set_ephe_path PER ROW

    Unlike grid-analytic.tsv, every row in this file touches file-backed state: which .se1
    segment is cached, which file handle is open, swed.ephepath itself. Tools/CReference/sedump.c
    calls swe_close() before every row exactly as it does for grid-analytic.tsv, and additionally
    calls swe_set_ephe_path() before every row when a third command-line argument is supplied
    (see that file's own header). Tools/OracleDump/Program.cs constructs a fresh SwissEph and
    attaches its OnLoadFile handler before every row for the same reason -- SwissEph does not read
    files directly, so the .NET side needs a handler at all, and scripts/run-oracle-dump.ps1's own
    header explains why the handler has to be attached before swe_set_ephe_path is called, not
    after.

    COLUMN LAYOUT (documented again, verbatim, at the top of the generated file itself)

    Tab-separated, LF line endings, one call per data row. Lines starting with '#' are comments;
    the first non-comment line is the column-name header, which both drivers assert against
    verbatim. Empty string means "does not apply to this row's func":

      case_id, func, ipl, tjd, iflag, star, geolon, geolat, height, sid_mode

    Ten columns, not grid-analytic.tsv's twelve: there is no house system here, so hsys, armc and
    eps are dropped, and a star column takes their place.

.NOTES
    Deterministic by construction: no timestamps, no randomness, no machine-dependent state (the
    Julian day values below come from a fixed Gregorian-calendar-to-Julian-day-number conversion
    run inside this script, not from any live clock or ephemeris lookup). Running this script
    twice must produce a byte-identical file.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# No native (non-PowerShell) commands run in this script -- see gen-grid-analytic.ps1's own copy
# of this line for why it is set anyway.
$PSNativeCommandUseErrorActionPreference = $false

$outputPath = Join-Path $PSScriptRoot 'grid-files.tsv'

# ---------------------------------------------------------------------------------------
# Swiss Ephemeris constants (SwissEphNet/SwissEph.swephexp.h.cs / external/swisseph/swephexp.h).
# ---------------------------------------------------------------------------------------

$SEFLG_SWIEPH     = 2
$SEFLG_HELCTR     = 8
$SEFLG_SPEED      = 256
$SEFLG_BARYCTR    = 16 * 1024
$SEFLG_TOPOCTR    = 32 * 1024
$SEFLG_SIDEREAL   = 64 * 1024
$SE_SIDM_LAHIRI   = 1

# ---------------------------------------------------------------------------------------
# Formatting -- matches gen-grid-analytic.ps1's Fmt/FmtI: invariant culture, "R" round-trip
# precision for doubles, so every machine that runs this script (and the drivers that later
# parse its output) reads the identical digits.
# ---------------------------------------------------------------------------------------

function Fmt {
    param([double] $Value)
    return $Value.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}

function FmtI {
    param([int] $Value)
    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# Fliegel & Van Flandern's proleptic-Gregorian-to-Julian-day-number conversion, the standard
# integer algorithm behind swe_julday's own Gregorian branch. Returns the Julian day number at
# 12:00 (noon) of the given calendar date, which is the convention swe_calc/swe_calc_ut's own tjd
# parameter uses. Self-contained rather than routed through Tools/BaselineMatrix or the library
# itself, for the same reason gen-grid-analytic.ps1's Get-JdSpread is self-contained: this script
# may not depend on the input-building code the drivers exist to check.
function Get-Jdn {
    param([int] $Year, [int] $Month, [int] $Day)
    $a = [math]::Floor(($Month - 14) / 12.0)
    $jdn = [math]::Floor((1461.0 * ($Year + 4800 + $a)) / 4.0) `
        + [math]::Floor((367.0 * ($Month - 2 - 12 * $a)) / 12.0) `
        - [math]::Floor((3.0 * [math]::Floor(($Year + 4900 + $a) / 100.0)) / 4.0) `
        + $Day - 32075
    return [double]$jdn
}

# ---------------------------------------------------------------------------------------
# Row builders
# ---------------------------------------------------------------------------------------

function New-CalcFileRow {
    param(
        [string] $Func,
        [int]    $Ipl,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag,
        $GeoLon,
        $GeoLat,
        $Height,
        $SidMode
    )
    $prefix = if ($Func -eq 'CALC') { 'CALC' } else { 'CALCUT' }
    $caseId = "$prefix|$(FmtI $Ipl)|$(Fmt $Tjd)|$FlagName"
    $geolonField  = if ($null -eq $GeoLon)  { '' } else { Fmt ([double]$GeoLon) }
    $geolatField  = if ($null -eq $GeoLat)  { '' } else { Fmt ([double]$GeoLat) }
    $heightField  = if ($null -eq $Height)  { '' } else { Fmt ([double]$Height) }
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        $geolonField, $geolatField, $heightField, $sidModeField
    )
    return ($fields -join "`t")
}

function New-FixstarRow {
    param(
        [string] $Prefix,
        [string] $Func,
        [string] $Star,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag
    )
    $caseId = "$Prefix|$Star|$(Fmt $Tjd)|$FlagName"
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), $Star,
        '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-FixstarMagRow {
    param([string] $Star)
    $caseId = "FIXSTARMAG|$Star"
    $fields = @(
        $caseId, 'FIXSTAR_MAG', '', '', '', $Star,
        '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-NameRow {
    param([int] $Ipl)
    $caseId = "NAME|$(FmtI $Ipl)"
    $fields = @(
        $caseId, 'GET_PLANET_NAME', (FmtI $Ipl), '', '', '',
        '', '', '', ''
    )
    return ($fields -join "`t")
}

# ---------------------------------------------------------------------------------------
# Grid values
# ---------------------------------------------------------------------------------------

# SE_SUN..SE_EARTH (0-14): same body set as grid-analytic.tsv, for the same reason -- see that
# script's own header. True node and osculating apogee, unlike their mean counterparts, are
# derived from the Moon's actual computed position, so they exercise the file-backed Moon
# segment even though their own body id carries no ephemeris file of its own.
$Bodies = 0..14

# Ten Julian days, all inside the combined span the two shipped era files cover (years
# 1200-2399), with 1790/1810 chosen specifically to straddle the 1800 file boundary (sepl_12
# et al. cover 1200-1799, sepl_18 et al. cover 1800-2399) and the rest spread widely enough
# across each file's span that they cannot land in one segment together.
$CalcJdsFiles = @(
    (Get-Jdn 1300 1 1),
    (Get-Jdn 1500 6 15),
    (Get-Jdn 1700 3 20),
    (Get-Jdn 1750 11 5),
    (Get-Jdn 1790 7 1),
    (Get-Jdn 1810 7 1),
    (Get-Jdn 1900 1 1),
    (Get-Jdn 2100 1 1),
    (Get-Jdn 2300 1 1),
    (Get-Jdn 2390 1 1)
)

$TopoGeoLon = -118.24
$TopoGeoLat = 34.05
$TopoHeight = 100.0

# Six combinations: a plain SEFLG_SWIEPH baseline plus the flags most likely to route through a
# different piece of the file-backed calculation (SPEED re-reads the segment for a derivative;
# TOPOCTR and BARYCTR both need a second, file-backed Earth/Sun position on top of the target
# body's own; SIDEREAL applies the ayanamsha correction after a file-backed calc). Smaller than
# grid-analytic.tsv's twelve-combination matrix on purpose -- this grid's job is proving the file
# layer is exercised at all, not re-covering every iflag bit grid-analytic.tsv already covers
# under SEFLG_MOSEPH.
$FlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;              NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'SPEED';    Flag = $SEFLG_SPEED;   NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'TOPOCTR';  Flag = $SEFLG_TOPOCTR; NeedsTopo = $true;  NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsTopo = $false; NeedsSid = $true }
    [pscustomobject]@{ Name = 'HELCTR';   Flag = $SEFLG_HELCTR;  NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'BARYCTR';  Flag = $SEFLG_BARYCTR; NeedsTopo = $false; NeedsSid = $false }
)

# Eight star search strings, chosen to cover every shape
# SwissEphNet/CPort/Sweph.cs's fixstar_format_search_name distinguishes: a single-word
# traditional name, a multi-word traditional name, a leading-comma Bayer-designation-only string
# (the format swe_fixstar's own doc comment calls out: "star name is not given, but its Bayer or
# Flamsteed designation, preceded by a comma"), and the combined "name,bayer" form sefstars.txt
# itself ships every record in. All eight resolve against external/swisseph/ephe/sefstars.txt
# (verified present by name in that file as of this grid's generation).
$StarNames = @(
    'Sirius',
    'Aldebaran',
    'Regulus',
    'Antares',
    'Spica',
    'Galactic Center',
    ',alTau',
    'Aldebaran,alTau'
)

# Three of the ten SWIEPH dates above, by index: 0 (1300, era _12), 5 (1810, era _18) and 8
# (2300, era _18). 1810 sits right after the 1800 file boundary defined above, not on the era
# _12 side of it -- the pair that actually straddles the boundary is index 4 (1790, era _12) and
# index 5 (1810, era _18), and only index 5 is used here; index 4 never appears in this list.
# Fixed-star calculation does not read a segment keyed to the target body the way swe_calc does,
# but it still needs a file-backed Earth/Sun position for aberration and light-time correction,
# so it is not exempt from the file layer either.
$FixstarJds = @($CalcJdsFiles[0], $CalcJdsFiles[5], $CalcJdsFiles[8])

$FixstarFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN'; Flag = 0 }
    [pscustomobject]@{ Name = 'SPEED'; Flag = $SEFLG_SPEED }
)

# Bodies swe_get_planet_name resolves through a named switch case: SE_SUN..SE_EARTH (0-14, same
# as $Bodies above), then Chiron/Pholus/Ceres/Pallas/Juno/Vesta (15-20) and the two interpolated
# apogee/perigee bodies (21-22) -- see SwissEphNet/CPort/Sweph.cs's swe_get_planet_name switch.
$NameBodies = 0..22

# One arbitrary main-belt asteroid this repo's sparse ephemeris checkout ships no per-asteroid
# data file for (Tests/conformance/required-ephemeris-files.tsv has no seasNNNN.se1 entries at
# all): 10005 is SE_AST_OFFSET (10000) + 5, Astraea -- not 433 Eros, which would be 10433 -- and
# is chosen only because it is not one of the six bodies with their own named case
# (Ceres/Pallas/Juno/Vesta/Chiron/Pholus already covered above), so it falls into the asteroid
# branch's file-open attempt, exercising the file-not-found path on both sides instead of a case
# that never touches a file.
$NameAsteroidOffsetBody = 10005

# ---------------------------------------------------------------------------------------
# Build rows
# ---------------------------------------------------------------------------------------

$rows = [System.Collections.Generic.List[string]]::new()
$calcCount = 0
$calcUtCount = 0
$fixstarCount = 0
$fixstarUtCount = 0
$fixstar2Count = 0
$fixstar2UtCount = 0
$fixstarMagCount = 0
$nameCount = 0

foreach ($ipl in $Bodies) {
    foreach ($tjd in $CalcJdsFiles) {
        foreach ($combo in $FlagCombos) {
            $iflag = $SEFLG_SWIEPH -bor $combo.Flag
            $geolon  = if ($combo.NeedsTopo) { $TopoGeoLon } else { $null }
            $geolat  = if ($combo.NeedsTopo) { $TopoGeoLat } else { $null }
            $height  = if ($combo.NeedsTopo) { $TopoHeight } else { $null }
            $sidMode = if ($combo.NeedsSid)  { $SE_SIDM_LAHIRI } else { $null }

            $rows.Add((New-CalcFileRow -Func 'CALC' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcCount++

            $rows.Add((New-CalcFileRow -Func 'CALC_UT' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcUtCount++
        }
    }
}

foreach ($star in $StarNames) {
    foreach ($tjd in $FixstarJds) {
        foreach ($combo in $FixstarFlagCombos) {
            $iflag = $SEFLG_SWIEPH -bor $combo.Flag

            $rows.Add((New-FixstarRow -Prefix 'FIXSTAR' -Func 'FIXSTAR' -Star $star -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $fixstarCount++

            $rows.Add((New-FixstarRow -Prefix 'FIXSTARUT' -Func 'FIXSTAR_UT' -Star $star -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $fixstarUtCount++

            $rows.Add((New-FixstarRow -Prefix 'FIXSTAR2' -Func 'FIXSTAR2' -Star $star -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $fixstar2Count++

            $rows.Add((New-FixstarRow -Prefix 'FIXSTAR2UT' -Func 'FIXSTAR2_UT' -Star $star -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $fixstar2UtCount++
        }
    }
}

foreach ($star in $StarNames) {
    $rows.Add((New-FixstarMagRow -Star $star))
    $fixstarMagCount++
}

foreach ($ipl in $NameBodies) {
    $rows.Add((New-NameRow -Ipl $ipl))
    $nameCount++
}
$rows.Add((New-NameRow -Ipl $NameAsteroidOffsetBody))
$nameCount++

$totalRows = $rows.Count
$expectedTotal = $calcCount + $calcUtCount + $fixstarCount + $fixstarUtCount + $fixstar2Count + $fixstar2UtCount + $fixstarMagCount + $nameCount
if ($totalRows -ne $expectedTotal) {
    throw 'Row count bookkeeping is inconsistent -- this is a bug in this script, not a data problem.'
}

# ---------------------------------------------------------------------------------------
# Header block
# ---------------------------------------------------------------------------------------

$headerLines = @(
    '# grid-files.tsv -- committed input vectors for the bit-exact C-vs-C# comparison harness'
    '# (stage 2, the file-backed code paths). Regenerated by Tools/OracleGrid/gen-grid-files.ps1'
    '# -- never hand-edit this file; a change here has to come from that script, committed'
    '# together with its regenerated output.'
    '#'
    '# WHY THIS GRID EXISTS ALONGSIDE grid-analytic.tsv'
    '#'
    '# grid-analytic.tsv OR-s SEFLG_MOSEPH into every swe_calc/swe_calc_ut row, so it never opens'
    '# an ephemeris data file -- the same restriction Tools/BaselineGen/Program.cs documents for'
    '# the characterization baseline. That leaves read_const, do_fread, get_new_segment,'
    '# rot_back, swi_get_denum, load_dpsi_deps, swe_close, free_planets and the sefstars.txt path'
    '# with no bit-level coverage at all. This grid requests SEFLG_SWIEPH instead, over dates that'
    '# fall inside the two era files this repo ships, so the port actually reads a segment.'
    '#'
    '# COVERAGE'
    '#'
    '# swe_calc / swe_calc_ut: bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating apogee,'
    '# Earth), crossed with ten Julian days inside the shipped files'' combined span (years'
    '# 1200-2399) and six iflag combinations, every one carrying SEFLG_SWIEPH. Two of the ten'
    '# dates straddle the 1800 file boundary (sepl/semo/seas_12.se1 covers 1200-1799, _18.se1'
    '# covers 1800-2399) on purpose, so the grid as a whole exercises both files.'
    '#'
    '# swe_fixstar / swe_fixstar_ut / swe_fixstar2 / swe_fixstar2_ut / swe_fixstar_mag: eight star'
    '# search strings covering a plain name, a multi-word name, a leading-comma'
    '# Bayer-designation-only string, and a combined "name,bayer" string, crossed with three'
    '# Julian days and two iflag combinations (swe_fixstar_mag takes neither a date nor a flag,'
    '# so it gets one row per star name).'
    '#'
    '# swe_get_planet_name: every body number that resolves through a named switch case (0-22),'
    '# plus one arbitrary asteroid number this repo ships no per-asteroid data file for, to'
    '# exercise the file-not-found branch of the asteroid-name lookup deliberately. Returns a'
    '# string, not a double -- see below for where that string is written.'
    '#'
    '# THE PLANET/STAR NAME IS CARRIED IN THE err/serr OUTPUT COLUMN'
    '#'
    '# A GET_PLANET_NAME row has zero value (xx/mag) columns. The name swe_get_planet_name'
    '# returns is written into the same output column Tools/CReference/sedump.c and'
    '# Tools/OracleDump/Program.cs already use for swe_calc''s serr text -- comparing that column'
    '# byte for byte is exactly the check a returned name needs, and costs no new column or new'
    '# comparison logic in Tools/OracleVerify.'
    '#'
    '# A FRESH LIBRARY INSTANCE PER ROW, AND A FRESH swe_set_ephe_path PER ROW'
    '#'
    '# Every row here touches file-backed state (which segment is cached, which file handle is'
    '# open, swed.ephepath itself), not just the hidden swe_houses_armc static grid-analytic.tsv'
    '# has to guard against. Both drivers reset all library state before every row -- see each'
    '# driver''s own header comment for how, and scripts/run-oracle-dump.ps1''s header for why the'
    '# .NET side''s OnLoadFile handler has to be attached before swe_set_ephe_path is called, not'
    '# after.'
    '#'
    '# COLUMNS (tab-separated, one call per line, LF line endings, empty string where a column'
    '# does not apply to that row''s func)'
    '#'
    '#   case_id    stable, unique, pipe-delimited id; ordinal comparison sorts it deterministically'
    '#   func       CALC | CALC_UT | FIXSTAR | FIXSTAR_UT | FIXSTAR2 | FIXSTAR2_UT | FIXSTAR_MAG | GET_PLANET_NAME'
    '#   ipl        body number                                        [CALC, CALC_UT, GET_PLANET_NAME]'
    '#   tjd        Julian day (ET for CALC/FIXSTAR/FIXSTAR2; UT for CALC_UT/FIXSTAR_UT/FIXSTAR2_UT)'
    '#              [CALC, CALC_UT, FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT]'
    '#   iflag      swe_calc/swe_fixstar iflag, with SEFLG_SWIEPH already OR-ed in'
    '#              [CALC, CALC_UT, FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT]'
    '#   star       star name or search string                         [FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT, FIXSTAR_MAG]'
    '#   geolon     geographic longitude, degrees east                 [CALC/CALC_UT topo rows only]'
    '#   geolat     geographic latitude, degrees north                 [CALC/CALC_UT topo rows only]'
    '#   height     observer height above sea level, metres            [CALC/CALC_UT topo rows only]'
    '#   sid_mode   swe_set_sid_mode mode, applied before the row runs [CALC/CALC_UT rows whose iflag carries SEFLG_SIDEREAL]'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- both drivers assert it matches verbatim before reading any'
    '# data.'
)
$columnHeader = 'case_id' + "`t" + 'func' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag' + "`t" +
    'star' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'sid_mode'

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

Write-Host "PASS: wrote $totalRows data row(s) to $outputPath" -ForegroundColor Green
Write-Host "  CALC          $calcCount"
Write-Host "  CALC_UT       $calcUtCount"
Write-Host "  FIXSTAR       $fixstarCount"
Write-Host "  FIXSTAR_UT    $fixstarUtCount"
Write-Host "  FIXSTAR2      $fixstar2Count"
Write-Host "  FIXSTAR2_UT   $fixstar2UtCount"
Write-Host "  FIXSTAR_MAG   $fixstarMagCount"
Write-Host "  GET_PLANET_NAME $nameCount"
