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

      swe_solcross / swe_solcross_ut / swe_mooncross / swe_mooncross_ut / swe_mooncross_node /
                                 swe_mooncross_node_ut / swe_helio_cross / swe_helio_cross_ut --
                                 the same eight crossing functions gen-grid-analytic.ps1 covers
                                 under SEFLG_MOSEPH, here under SEFLG_SWIEPH so the file-backed
                                 code path is exercised too. Every date used is chosen with enough
                                 margin from both the 1200/2399 span edges and the 1800 file
                                 boundary that even the slowest crossing search these rows can
                                 trigger cannot walk past the two shipped files' combined
                                 coverage -- see the per-function value blocks below for the
                                 margin reasoning specific to each. swe_helio_cross's own body
                                 list is narrower here than gen-grid-analytic.ps1's for the same
                                 reason: a heliocentric search can take up to about one full
                                 orbital period to converge, and Uranus/Neptune/Pluto's periods
                                 (84/165/248 years) are close enough to this grid's own file-era
                                 margins that including them would risk a search walking past the
                                 shipped files' span -- gen-grid-analytic.ps1's unconstrained
                                 Moshier range has no such limit, so those three stay there only.

      swe_houses_ex / swe_nod_aps_ut -- two of the six new funcs gen-grid-analytic.ps1 adds
                                 (HOUSES_EX/AYANAMSA_UT/SIDTIME/AZALT/HOUSE_NAME/NOD_APS_UT), the
                                 two where reading a real .se1 file changes what gets exercised:
                                 some SIDEREAL sid_modes drive the ayanamsa through a file-backed
                                 swe_calc, which SEFLG_MOSEPH can never reach, and SE_CHIRON (which
                                 swe_nod_aps_ut can special-case with a hardcoded mean speed) has
                                 no Moshier model at all. See the COLUMNS section below for the
                                 method/hsys columns this needs.

      swe_houses_ex2 / swe_houses_armc_ex2 -- new in 2.10.03 (absent from
                                 external/pyswisseph-2.08/swephexp.h entirely -- verified: zero
                                 matches for either name anywhere under that tree). The oracle
                                 already reaches both on every HOUSES/HOUSES_EX row, because
                                 swe_houses/swe_houses_ex delegate to them (swehouse.c:173, :186),
                                 but always with cusp_speed/ascmc_speed/serr hardcoded NULL, so
                                 h.do_speed/h.do_hspeed (swehouse.c:642-647) stay FALSE and the 2.10
                                 speed feature is switched off in every row that reaches it that way.
                                 This grid calls swe_houses_ex2/swe_houses_armc_ex2 directly, with
                                 real cusp_speed/ascmc_speed arrays, so do_speed/do_hspeed are TRUE
                                 and the speed writes (swehouse.c:663,671,685) actually execute.
                                 Same input columns HOUSES_EX/HOUSES_ARMC already use (armc/eps are
                                 new to this grid only because it never carried HOUSES_ARMC rows of
                                 its own before -- see the COLUMNS section below); reuses this
                                 grid's own sid_mode/hsys sweeps rather than a wider one, matching
                                 how every other file-backed func here is a smaller cross-section of
                                 its grid-analytic.tsv counterpart. Guarded behind
                                 SWISSEPH_HAS_HOUSES_EX2 in sedump.c, the same
                                 compiled-in-2.10.03-only pattern SWISSEPH_HAS_CROSSING already uses
                                 for the eight crossing functions -- see sedump.c's own top-of-file
                                 comment. Tools/OracleDump/Program.cs has no SWISSEPH_HAS_* symbol
                                 at all, correctly, since the port is single-version and has
                                 nothing to guard; only sedump.c is compiled against two library
                                 versions.

      swe_fixstar2_mag -- both drivers previously called only swe_fixstar_mag (the plain form);
                                 swe_fixstar2_mag (present in 2.08 too -- external/pyswisseph-2.08/
                                 swephexp.h:708 -- so it needs no version guard) had no grid row of
                                 its own anywhere. Same star-name sweep as FIXSTAR_MAG.

      swe_calc_pctr / swe_get_current_file_data -- the remaining two of the twelve entry points
                                 new in 2.10.03 (absent from external/pyswisseph-2.08/swephexp.h
                                 entirely, guarded behind SWISSEPH_HAS_CALC_PCTR/
                                 SWISSEPH_HAS_GET_CURRENT_FILE_DATA in sedump.c). Both are
                                 files-grid-only: swe_calc_pctr forces SEFLG_BARYCTR
                                 unconditionally (sweph.c:8061), and
                                 SEFLG_BARYCTR|SEFLG_MOSEPH is rejected before any geometry runs
                                 (sweph.c:634-638), so grid-analytic.tsv's forced-SEFLG_MOSEPH rows
                                 could only ever reach that reject -- the same SE_CHIRON category
                                 error this script's own $HelioCrossIplFiles comment already
                                 documents for a different func. swe_get_current_file_data reads
                                 swed.fidat, which grid-analytic.tsv's rows never populate at all.
                                 PCTR: three body pairs x two dates x three iflag combinations
                                 (PLAIN, SPEED, SIDEREAL), plus one row exercising the "ipl and
                                 iplctr must not be identical" reject. GET_CURRENT_FILE_DATA: the
                                 boundary and empty-fnam reject branches, plus real data for ifno 0
                                 (planet file, via an optional preceding swe_calc), ifno 1 (Moon
                                 file, populated for free by this grid's own swe_set_ephe_path
                                 call), ifno 2 (main asteroid file, via the same preceding
                                 swe_calc mechanism) and ifno 4 (star file, via an optional
                                 preceding swe_fixstar2) -- see New-PctrRow's and
                                 New-GetCurrentFileDataRow's own comments for the full detail,
                                 including why ifno 3 has no real-data row in this grid at all.

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

      case_id, func, ipl, tjd, iflag, star, geolon, geolat, height, sid_mode, x2cross, dir, t0,
      ayan_t0, method, hsys, armc, eps

    Eighteen columns today. method and hsys were appended for NOD_APS_UT/HOUSES_EX; armc and eps
    are this addition's own appension, needed only because HOUSES_ARMC_EX2 is the first func this
    grid has ever carried that takes an armc/eps pair directly (HOUSES_EX/HOUSES_EX2 derive armc
    from geolon/tjd internally and never need it as an input column) -- additive, at the end, for
    the same reason every earlier column here landed at the end rather than interleaved: every
    column an existing func already reads keeps the same index it always had.
    grid-jpl.tsv carries this header byte-for-byte (see gen-grid-jpl.ps1's own header for why), so
    this addition is a two-grid schema change, not a one-grid one.

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
$SEFLG_TRUEPOS    = 16
$SEFLG_SPEED      = 256
$SEFLG_BARYCTR    = 16 * 1024
$SEFLG_TOPOCTR    = 32 * 1024
$SEFLG_SIDEREAL   = 64 * 1024
$SE_SIDM_LAHIRI   = 1
$SEFLG_RADIANS    = 8 * 1024
# Deliberately a literal, not read off any assembly's SwissEph.SE_NSIDM_PREDEF -- see
# gen-grid-analytic.ps1's own copy of this constant and comment for why (this script runs
# standalone PowerShell and loads no .NET assembly, so there is nothing to read the constant off
# in the first place; the value is pinned here anyway, matching Tools/BaselineMatrix/Ayanamsa.cs's
# own literal, since the sid-mode sweep below is a property of what this grid covers).
$SidModeSweepCount = 47

# swe_nod_aps(_ut)'s own reject check -- see gen-grid-analytic.ps1's identical copy of these
# constants and comment for the C citations.
$SE_MEAN_NODE   = 10
$SE_TRUE_NODE   = 11
$SE_MEAN_APOG   = 12
$SE_OSCU_APOG   = 13
$SE_EARTH       = 14
$SE_CHIRON      = 15
$SE_NPLANETS    = 23
$SE_AST_OFFSET  = 10000
$SE_NODBIT_MEAN     = 1
$SE_NODBIT_OSCU     = 2
$SE_NODBIT_OSCU_BAR = 4
$SE_NODBIT_FOPOINT  = 256

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

# Cycles deterministically through the 47 predefined sidereal modes (0..46) -- see
# gen-grid-analytic.ps1's own copy of this function and its comment for the full reasoning (widens
# the SEFLG_SIDEREAL CALC/CALC_UT rows this grid already had beyond the single hardcoded
# $SE_SIDM_LAHIRI they used before, without multiplying row count by 47). A separate counter from
# gen-grid-analytic.ps1's own: the two scripts run as separate processes over separate grids, so
# there is no shared state to keep in sync between them.
$script:sidModeCycleNext = 0
function Get-NextSidMode {
    $mode = $script:sidModeCycleNext % $SidModeSweepCount
    $script:sidModeCycleNext++
    return $mode
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
        $SidMode,
        $T0,
        $AyanT0
    )
    $prefix = if ($Func -eq 'CALC') { 'CALC' } else { 'CALCUT' }
    $caseId = "$prefix|$(FmtI $Ipl)|$(Fmt $Tjd)|$FlagName"
    $geolonField  = if ($null -eq $GeoLon)  { '' } else { Fmt ([double]$GeoLon) }
    $geolatField  = if ($null -eq $GeoLat)  { '' } else { Fmt ([double]$GeoLat) }
    $heightField  = if ($null -eq $Height)  { '' } else { Fmt ([double]$Height) }
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $t0Field      = if ($null -eq $T0)      { '' } else { Fmt ([double]$T0) }
    $ayanT0Field  = if ($null -eq $AyanT0)  { '' } else { Fmt ([double]$AyanT0) }
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        $geolonField, $geolatField, $heightField, $sidModeField, '', '',
        $t0Field, $ayanT0Field, '', '', '', '', '', ''
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
        '', '', '', '', '', '', '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# FIXSTAR_MAG (swe_fixstar_mag) and FIXSTAR2_MAG (swe_fixstar2_mag) share this shape -- neither
# takes a date or flag, only the star search string -- with a distinct case_id prefix per func so
# Tools/OracleVerify/FieldLabels.cs's func-token dispatch (case_id.Split('|')[0]) tells the two
# apart; both resolve to the same one-double (mag) shape there. swe_fixstar2_mag needs no version
# guard: it is declared in external/pyswisseph-2.08/swephexp.h:708, unlike swe_houses_ex2/
# swe_houses_armc_ex2 below.
function New-FixstarMagRow {
    param([string] $Prefix, [string] $Func, [string] $Star)
    $caseId = "$Prefix|$Star"
    $fields = @(
        $caseId, $Func, '', '', '', $Star,
        '', '', '', '', '', '', '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-NameRow {
    param([int] $Ipl)
    $caseId = "NAME|$(FmtI $Ipl)"
    $fields = @(
        $caseId, 'GET_PLANET_NAME', (FmtI $Ipl), '', '', '',
        '', '', '', '', '', '', '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_solcross/_ut and swe_mooncross/_ut share one C signature shape (x2cross, tjd, iflag, serr)
# and one row shape here -- matches gen-grid-analytic.ps1's New-SolarLunarCrossRow, minus the
# hsys/geolon/geolat/height/armc/eps columns this grid does not carry at all and with a star
# column (always empty for these rows) in their place.
function New-SolarLunarCrossRow {
    param(
        [string] $Prefix,
        [string] $Func,
        [double] $X2Cross,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag
    )
    $caseId = "$Prefix|$(Fmt $X2Cross)|$(Fmt $Tjd)|$FlagName"
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', (Fmt $X2Cross), '', '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_mooncross_node/_ut takes no target longitude -- it finds a zero-*latitude* node crossing,
# not a crossing over a caller-supplied longitude -- so x2cross stays empty here, unlike
# New-SolarLunarCrossRow above.
function New-MoonCrossNodeRow {
    param(
        [string] $Prefix,
        [string] $Func,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag
    )
    $caseId = "$Prefix|$(Fmt $Tjd)|$FlagName"
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', '', '', '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_helio_cross/_ut is the one crossing function that takes a body (ipl, reusing the same column
# swe_calc's rows already carry) and a search direction (dir, the new trailing column).
function New-HelioCrossRow {
    param(
        [string] $Prefix,
        [string] $Func,
        [int]    $Ipl,
        [double] $X2Cross,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag,
        [int]    $Dir
    )
    $caseId = "$Prefix|$(FmtI $Ipl)|$(Fmt $X2Cross)|$(Fmt $Tjd)|$FlagName|$(FmtI $Dir)"
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', (Fmt $X2Cross), (FmtI $Dir), '', '', '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_houses_ex -- the sidereal/radians-capable sibling of swe_houses; see
# gen-grid-analytic.ps1's own New-HousesExRow for the full rationale. This grid's own reason to
# carry it at all (rather than leaving it to grid-analytic.tsv) is its SIDEREAL rows: some
# sid_modes drive the ayanamsa through a file-backed swe_calc (e.g. the true-position-of-a-star
# modes), which SEFLG_MOSEPH can never reach -- see this script's own header. Shared by
# New-HousesEx2Row below (Func distinguishes swe_houses_ex from swe_houses_ex2 -- same input shape,
# see sedump.c's own process_houses_ex/process_houses_ex2 for the output-side difference).
function New-HousesExRow {
    param([string] $Prefix, [string] $Func, [char] $Hsys, [double] $GeoLat, [double] $GeoLon, [double] $Tjd, [string] $FlagName, [int] $IFlag, $SidMode)
    $caseId = "$Prefix|$Hsys|$(Fmt $GeoLat)|$(Fmt $GeoLon)|$(Fmt $Tjd)|$FlagName"
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), '',
        (Fmt $GeoLon), (Fmt $GeoLat), '', $sidModeField, '', '', '', '',
        '', "$Hsys", '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_houses_armc_ex2 -- new in 2.10.03 (absent from external/pyswisseph-2.08/swephexp.h entirely;
# see this script's own header). Unlike HOUSES_EX2 above, this grid never carried a plain
# HOUSES_ARMC row before this addition (grid-analytic.tsv already covers swe_houses_armc, and
# swe_houses_armc_ex2 itself never opens a file -- see this script's own header on why it is added
# here anyway, for dispatch/schema parity with grid-analytic.tsv rather than for file-layer
# coverage), so armc and eps are new columns (see the COLUMN LAYOUT section of this script's own
# header) rather than reused ones.
function New-HousesArmcEx2Row {
    param([char] $Hsys, [double] $GeoLat, [double] $Eps, [double] $Armc)
    $caseId = "HOUSESARMCEX2|$Hsys|$(Fmt $GeoLat)|$(Fmt $Eps)|$(Fmt $Armc)"
    $fields = @(
        $caseId, 'HOUSES_ARMC_EX2', '', '', '', '',
        '', (Fmt $GeoLat), '', '', '', '', '', '',
        '', "$Hsys", (Fmt $Armc), (Fmt $Eps), '', ''
    )
    return ($fields -join "`t")
}

# swe_nod_aps_ut -- see gen-grid-analytic.ps1's own New-NodApsUtRow for the full rationale. This
# grid's own reason to carry it (rather than leaving it to grid-analytic.tsv) is SE_CHIRON: it has
# no Moshier model, so its mean-speed override (external/swisseph/swecl.c:8551-8552's sibling in
# swe_nod_aps's own "true"/osculating branch) is reachable only against a real seas_12.se1/
# seas_18.se1 file -- the same reason gen-grid-files.ps1's own $HelioCrossIplFiles carries it.
function New-NodApsUtRow {
    param([int] $Ipl, [double] $Tjd, [int] $IFlag, [int] $Method)
    $caseId = "NODAPSUT|$(FmtI $Ipl)|$(Fmt $Tjd)|$(FmtI $Method)"
    $fields = @(
        $caseId, 'NOD_APS_UT', (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', '', '', '', '',
        (FmtI $Method), '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_calc_pctr -- new in 2.10.03 (absent from external/pyswisseph-2.08/swephexp.h entirely; see
# this script's own header). Reuses ipl/tjd/iflag, the same columns CALC already carries, for its
# first body; iplctr (this addition's own new, additive-tail column) is the second. iplctr = Ipl is
# a legitimate row shape here, not a mistake to guard against: it exercises swe_calc_pctr's own
# "ipl and iplctr must not be identical" reject (sweph.c:8050-8054) rather than the geometry path.
function New-PctrRow {
    param([int] $Ipl, [int] $Iplctr, [double] $Tjd, [string] $FlagName, [int] $IFlag, $SidMode)
    $caseId = "PCTR|$(FmtI $Ipl)|$(FmtI $Iplctr)|$(Fmt $Tjd)|$FlagName"
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, 'PCTR', (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', $sidModeField, '', '', '', '',
        '', '', '', '', (FmtI $Iplctr), ''
    )
    return ($fields -join "`t")
}

# swe_get_current_file_data -- new in 2.10.03 (absent from external/pyswisseph-2.08/swephexp.h
# entirely; see this script's own header). Ifno alone (Ipl/Tjd/Star all $null) tests the
# boundary/no-data branches -- see process_get_current_file_data's own comment in
# Tools/CReference/sedump.c for exactly which slot is already populated with no other input on the
# row at all (ifno 1, the Moon file, via main()'s/AttachEpheDir's own swe_set_ephe_path call), and
# which is not. Ipl+Tjd (Star left $null) trigger a preceding swe_calc, reusing the same columns
# CALC already carries, to populate ifno 0 (planet file) or ifno 2 (main asteroid file) with real
# data instead; Star+Tjd (Ipl left $null) trigger a preceding swe_fixstar2 instead, for ifno 4 (the
# star file). Ifno 3 (SEI_FILE_ANY_AST -- an individually-numbered asteroid or planetary-moon file)
# has no row here that reaches it with real data: this repo's ephemeris checkout ships no such
# file (only sepl/semo/seas_{12,18}.se1 and sefstars.txt -- see -EpheDir's own manifest,
# Tests/conformance/required-ephemeris-files.tsv), so every ifno-3 row this script emits only ever
# exercises the empty-fnam reject branch (sweph.c:8301), the same branch an ifno-0/2/4 row with no
# preceding call also exercises.
function New-GetCurrentFileDataRow {
    param([string] $Label, [int] $Ifno, $Ipl, $Tjd, $IFlag, $Star)
    $caseId = "GETCURRENTFILEDATA|$Label|$(FmtI $Ifno)"
    $iplField   = if ($null -eq $Ipl)  { '' } else { FmtI ([int]$Ipl) }
    $tjdField   = if ($null -eq $Tjd)  { '' } else { Fmt ([double]$Tjd) }
    $iflagField = if ($null -eq $IFlag) { '' } else { FmtI ([int]$IFlag) }
    $starField  = if ($null -eq $Star) { '' } else { [string]$Star }
    $fields = @(
        $caseId, 'GET_CURRENT_FILE_DATA', $iplField, $tjdField, $iflagField, $starField,
        '', '', '', '', '', '', '', '',
        '', '', '', '', '', (FmtI $Ifno)
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
# Crossing-function grid values (swe_solcross/_ut, swe_mooncross/_ut, swe_mooncross_node/_ut,
# swe_helio_cross/_ut) under SEFLG_SWIEPH. See this script's own .DESCRIPTION for why the body
# list and dates below are narrower than gen-grid-analytic.ps1's own crossing coverage -- every
# value here is chosen to keep the crossing search (which can walk up to about one full period
# away from its start date) from ever needing a date outside the two shipped files' combined
# 1200-2399 span.
# ---------------------------------------------------------------------------------------

# Four target longitudes -- smaller than gen-grid-analytic.ps1's six on purpose, matching how
# $FlagCombos above is already smaller than grid-analytic.tsv's own iflag matrix for the same
# "prove the file layer is exercised, do not re-cover ground grid-analytic.tsv already covers"
# reason. Still includes 0.0 and 359.9999, the two ends of the wraparound swe_degnorm folds
# together.
$CrossX2 = @(0.0, 90.0, 180.0, 359.9999)

# Two of the ten SWIEPH dates above, by index: 1 (1500, era _12) and 7 (2100, era _18) -- both
# comfortably mid-era (100+ years from either file-span edge), which is all swe_solcross/
# swe_mooncross need: the Sun's and Moon's crossing search only ever walks up to about one solar
# (365-day) or lunar (27.32-day) period ahead of its start date.
$CrossTjdFiles = @($CalcJdsFiles[1], $CalcJdsFiles[7])

$SolMoonCrossFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';   Flag = 0 }
    [pscustomobject]@{ Name = 'TRUEPOS'; Flag = $SEFLG_TRUEPOS }
)

# Three of the ten SWIEPH dates above, by index -- the same three $FixstarJds already uses (0:
# 1300 era _12, 5: 1810 era _18, 8: 2300 era _18), reused here rather than picked fresh since
# swe_mooncross_node's own search horizon (about one lunar month) is even shorter than
# swe_solcross/swe_mooncross's, so the same margin reasoning applies without needing a new date
# set.
$MoonCrossNodeTjdFiles = @($CalcJdsFiles[0], $CalcJdsFiles[5], $CalcJdsFiles[8])

# swe_helio_cross(_ut)'s search can take up to about one full heliocentric orbital period to
# converge, so the body list here is narrower than gen-grid-analytic.ps1's: SE_SUN and SE_MOON
# (one representative pick from two of the function's three reject disjuncts -- the SUN check and
# the MOON check; the node/apogee-range disjunct is not repeated here, since gen-grid-analytic.ps1
# already proves it fires and this grid's job is proving the file layer, not re-covering the
# reject logic in full) plus the bodies whose period comfortably fits inside the margins below:
# Mercury..Saturn (88 days to 29.5 years), SE_EARTH (1 year) and SE_CHIRON (50.7 years).
#
# SE_CHIRON is the ONE place either grid tests it, deliberately: it is the one body
# swe_helio_cross(_ut) overrides with a hardcoded mean speed instead of the speed swe_calc itself
# returns (external/swisseph/sweph.c:8551-8552), but Chiron has no Moshier analytic model, so
# swe_calc needs a real seas_12.se1/seas_18.se1 segment for it regardless of iflag -- a
# requirement gen-grid-analytic.ps1's SEFLG_MOSEPH grid cannot satisfy by definition (see that
# script's own $HelioCrossValidIpl comment for the 16-row false start this caused there). This
# grid opens -EpheDir, which does ship both files (Tests/conformance/required-ephemeris-files.tsv),
# with dates chosen so Chiron's mean-speed branch is genuinely reached, not just its file-not-found
# path -- see the date-margin comment below.
#
# Uranus/Neptune/Pluto (84/165/248-year periods) are left to gen-grid-analytic.ps1's unconstrained
# Moshier range; their orbits are analytic-only bodies with no data-file dependency, so nothing is
# lost by not repeating them here.
$HelioCrossIplFiles = @(0, 1, 2, 3, 4, 5, 6, 14, 15)  # SE_SUN, SE_MOON, Mercury..Saturn, SE_EARTH, SE_CHIRON
$HelioCrossX2Files = @(0.0, 180.0)
# Index 0 (1300, era _12: margin 100 years to the 1200 edge, 499 to the 1799 edge) and index 7
# (2100, era _18: margin 300 years to either edge) -- both margins comfortably exceed SE_CHIRON's
# 50.7-year period, the slowest body this list includes, on both sides (a search can walk forward
# OR backward depending on -Dir).
$HelioCrossTjdFiles = @($CalcJdsFiles[0], $CalcJdsFiles[7])
$HelioCrossDir = @(1, -1)

# ---------------------------------------------------------------------------------------
# swe_houses_ex grid values -- see this script's own header for why this func is here at all
# (its SIDEREAL rows can drive the ayanamsa through a file-backed swe_calc, unlike
# grid-analytic.tsv's forced-SEFLG_MOSEPH rows). Smaller than gen-grid-analytic.ps1's own
# HOUSES_EX sweep, matching how every file-backed func in this grid is sized smaller than its
# grid-analytic.tsv counterpart: this grid's job is proving the file layer is exercised, not
# re-covering the geolat/geolon/iflag spread grid-analytic.tsv already covers.
# ---------------------------------------------------------------------------------------
# Every house-system letter SwissEphNet/CPort/SweHouse.cs actually implements a case for --
# matches gen-grid-analytic.ps1's own $HouseLetters exactly (same comment there on why 'J' is
# deliberately excluded). This grid never carries swe_houses/swe_houses_armc rows of its own, so
# it has no such list already in scope; HOUSES_EX is the first func here that needs one.
$HouseLetters = @(
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'i', 'K', 'L', 'M', 'N', 'O',
    'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y'
)
$HousesExGeoLats = @(-66, 66)
$HousesExGeoLon = -118.24
$HousesExJdsFiles = @($CalcJdsFiles[1], $CalcJdsFiles[7])
$HousesExFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;               NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsSid = $true }
    [pscustomobject]@{ Name = 'RADIANS';  Flag = $SEFLG_RADIANS;  NeedsSid = $false }
)

# ---------------------------------------------------------------------------------------
# swe_houses_armc_ex2 grid values -- see this script's own header and New-HousesArmcEx2Row's own
# comment for why this func is added here despite touching no file itself (dispatch/schema parity
# with grid-analytic.tsv, not file-layer coverage). Same $HouseLetters sweep as HOUSES_EX/
# HOUSES_EX2 above (includes 'I'/'i' -- the hsys the saved_sundec static and the ascmc[9] == 99
# branch both concern; see New-HousesArmcEx2Row and sedump.c's own process_houses_armc_ex2 for why
# ascmc[9] is zero-initialized on every row here exactly as HOUSES_ARMC already is, so this grid
# never exercises the ascmc[9] == 99 read branch either). Armc/eps are a small, representative set
# -- gen-grid-analytic.ps1's own HOUSES_ARMC already covers the geometry in full; this grid's job
# is dispatch coverage, not re-proving arithmetic gen-grid-analytic.ps1 already proves.
# ---------------------------------------------------------------------------------------
$HousesArmcEx2GeoLats = @(-66, 66)
$HousesArmcEx2Eps = 23.4392911
$HousesArmcEx2Armcs = @(0.0, 90.0, 180.0, 270.0)

# ---------------------------------------------------------------------------------------
# swe_nod_aps_ut grid values -- see this script's own header for why SE_CHIRON is included here
# (the one place either grid tests it): unlike gen-grid-analytic.ps1's accepted-ipl list, there is
# no orbital-period search-margin concern for swe_nod_aps_ut the way there is for
# swe_helio_cross(_ut) -- nod_aps evaluates only near tjd_et itself (a small NODE_CALC_INTV
# offset for the speed derivative, external/swisseph/swecl.c:5256-5266), it does not walk forward
# or backward searching for a crossing -- so the full Moshier-accepted body list plus SE_CHIRON
# can all be swept without a file-era margin.
# ---------------------------------------------------------------------------------------
$NodApsAcceptedIplFiles = @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, $SE_EARTH, $SE_CHIRON)
$NodApsRejectedIplFiles = @($SE_MEAN_NODE, $SE_TRUE_NODE, $SE_MEAN_APOG, $SE_OSCU_APOG, -1, $SE_NPLANETS, $SE_AST_OFFSET)
$NodApsMethodsFiles = @(
    0,
    $SE_NODBIT_MEAN,
    $SE_NODBIT_OSCU,
    $SE_NODBIT_OSCU_BAR,
    ($SE_NODBIT_FOPOINT),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_MEAN),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_OSCU),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_OSCU_BAR)
)
$NodApsTjdFiles = @($CalcJdsFiles[1], $CalcJdsFiles[7])
$NodApsRejectedTjdFiles = $CalcJdsFiles[1]

# ---------------------------------------------------------------------------------------
# swe_calc_pctr grid values -- see this script's own header and New-PctrRow's own comment for why
# this func is here rather than grid-analytic.tsv (SEFLG_BARYCTR|SEFLG_MOSEPH is rejected before
# any geometry runs, sweph.c:634-638, and grid-analytic.tsv never configures an ephemeris path, so
# every row there would hit only that reject). Three body pairs -- Mars-from-Jupiter, Sun-from-
# Earth (heliocentric Earth is just the antipode of geocentric Sun, an unusual but legal
# planetocentric target) and Venus-from-Earth (nearly geocentric, but computed through
# swe_calc_pctr's own light-time/aberration algorithm rather than swe_calc's, so a genuinely
# different code path from a CALC row using the same two bodies) -- crossed with two of the ten
# SWIEPH dates above and three iflag combinations (PLAIN, SPEED -- the iterative apparent-speed
# branch at sweph.c:8079-8104,8119-8122,8164-8167 -- and SIDEREAL -- the ayanamsa branch at
# sweph.c:8217-8243). Smaller than gen-grid-analytic.ps1-style sweeps on purpose, matching how
# every other file-backed-only func in this grid is sized smaller than a full cross product: this
# grid's job is proving the real (non-Moshier) path is exercised, not re-covering every body pair.
# ---------------------------------------------------------------------------------------
$PctrBodyPairs = @(
    [pscustomobject]@{ Ipl = 4;  Iplctr = 5 }   # Mars as seen from Jupiter
    [pscustomobject]@{ Ipl = 0;  Iplctr = 14 }  # Sun as seen from Earth (heliocentric Earth)
    [pscustomobject]@{ Ipl = 3;  Iplctr = 14 }  # Venus as seen from Earth
)
$PctrTjdFiles = @($CalcJdsFiles[1], $CalcJdsFiles[7])
$PctrFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;               NeedsSid = $false }
    [pscustomobject]@{ Name = 'SPEED';    Flag = $SEFLG_SPEED;    NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsSid = $true }
)
# One representative pair for the "ipl and iplctr must not be identical" reject (sweph.c:8050-8054)
# -- New-PctrRow's own comment explains why iplctr = ipl is a deliberate row shape, not a mistake.
$PctrIdenticalIpl = 3  # Venus

# ---------------------------------------------------------------------------------------
# swe_get_current_file_data grid values -- see this script's own header and
# New-GetCurrentFileDataRow's own comment. $GetCurrentFileDataBoundary/NoData/AutoReal cover the
# five-line C function's every branch with no other input on the row at all (main()'s/
# AttachEpheDir's own swe_set_ephe_path call already populates ifno 1 before any row-specific func
# runs); $GetCurrentFileDataPreCalc/PreFixstar additionally trigger a preceding swe_calc/
# swe_fixstar2 to reach ifno 0/2/4 with real (non-Moshier) file data instead of the empty-fnam
# reject every other ifno here exercises.
# ---------------------------------------------------------------------------------------
$GetCurrentFileDataBoundary = @(-1, 5)                 # sweph.c:8299's ifno < 0 || ifno > 4
$GetCurrentFileDataNoData = @(0, 2, 3, 4)              # sweph.c:8301's strlen(fnam) == 0
$GetCurrentFileDataAutoReal = 1                        # SEI_FILE_MOON -- populated for free
$GetCurrentFileDataPreCalcIpl = 0                       # SE_SUN -> SEI_FILE_PLANET (ifno 0)
# SE_CERES (17): one of the six bodies swi_get_denum's own dispatch (sweph.c:2423-2429) routes to
# SEI_FILE_MAIN_AST -- an arbitrary numbered asteroid (e.g. SE_AST_OFFSET-relative) routes to
# SEI_FILE_ANY_AST (ifno 3) instead, not ifno 2 -- matches
# Tests/SwissEphNet.Tests/GetCurrentFileDataCoverageTest.cs's own choice of SE_CERES for the same
# real-ifno-2-data assertion.
$GetCurrentFileDataPreCalcAsteroidIpl = 17              # SE_CERES -> SEI_FILE_MAIN_AST (ifno 2)
$GetCurrentFileDataPreCalcTjd = $CalcJdsFiles[1]
$GetCurrentFileDataPreCalcIFlag = $SEFLG_SWIEPH
$GetCurrentFileDataPreFixstarStar = 'Sirius'            # -> SEI_FILE_FIXSTAR (ifno 4)

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
$solCrossCount = 0
$solCrossUtCount = 0
$moonCrossCount = 0
$moonCrossUtCount = 0
$moonCrossNodeCount = 0
$moonCrossNodeUtCount = 0
$helioCrossCount = 0
$helioCrossUtCount = 0
$housesExCount = 0
$housesEx2Count = 0
$housesArmcEx2Count = 0
$fixstar2MagCount = 0
$nodApsUtCount = 0
$pctrCount = 0
$getCurrentFileDataCount = 0

foreach ($ipl in $Bodies) {
    foreach ($tjd in $CalcJdsFiles) {
        foreach ($combo in $FlagCombos) {
            $iflag = $SEFLG_SWIEPH -bor $combo.Flag
            $geolon  = if ($combo.NeedsTopo) { $TopoGeoLon } else { $null }
            $geolat  = if ($combo.NeedsTopo) { $TopoGeoLat } else { $null }
            $height  = if ($combo.NeedsTopo) { $TopoHeight } else { $null }
            # Cycled, not pinned to $SE_SIDM_LAHIRI -- see Get-NextSidMode's own comment.
            $sidMode = if ($combo.NeedsSid)  { Get-NextSidMode } else { $null }

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
    $rows.Add((New-FixstarMagRow -Prefix 'FIXSTARMAG' -Func 'FIXSTAR_MAG' -Star $star))
    $fixstarMagCount++

    $rows.Add((New-FixstarMagRow -Prefix 'FIXSTAR2MAG' -Func 'FIXSTAR2_MAG' -Star $star))
    $fixstar2MagCount++
}

foreach ($ipl in $NameBodies) {
    $rows.Add((New-NameRow -Ipl $ipl))
    $nameCount++
}
$rows.Add((New-NameRow -Ipl $NameAsteroidOffsetBody))
$nameCount++

foreach ($x2 in $CrossX2) {
    foreach ($tjd in $CrossTjdFiles) {
        foreach ($combo in $SolMoonCrossFlagCombos) {
            $iflag = $SEFLG_SWIEPH -bor $combo.Flag

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSS' -Func 'SOLCROSS' -X2Cross $x2 -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $solCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSSUT' -Func 'SOLCROSS_UT' -X2Cross $x2 -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $solCrossUtCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSS' -Func 'MOONCROSS' -X2Cross $x2 -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $moonCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSSUT' -Func 'MOONCROSS_UT' -X2Cross $x2 -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
            $moonCrossUtCount++
        }
    }
}

foreach ($tjd in $MoonCrossNodeTjdFiles) {
    foreach ($combo in $SolMoonCrossFlagCombos) {
        $iflag = $SEFLG_SWIEPH -bor $combo.Flag

        $rows.Add((New-MoonCrossNodeRow -Prefix 'MOONCROSSNODE' -Func 'MOONCROSS_NODE' -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
        $moonCrossNodeCount++

        $rows.Add((New-MoonCrossNodeRow -Prefix 'MOONCROSSNODEUT' -Func 'MOONCROSS_NODE_UT' -Tjd $tjd -FlagName $combo.Name -IFlag $iflag))
        $moonCrossNodeUtCount++
    }
}

foreach ($ipl in $HelioCrossIplFiles) {
    foreach ($x2 in $HelioCrossX2Files) {
        foreach ($tjd in $HelioCrossTjdFiles) {
            foreach ($dir in $HelioCrossDir) {
                $iflag = $SEFLG_SWIEPH

                $rows.Add((New-HelioCrossRow -Prefix 'HELIOCROSS' -Func 'HELIO_CROSS' -Ipl $ipl -X2Cross $x2 -Tjd $tjd -FlagName 'PLAIN' -IFlag $iflag -Dir $dir))
                $helioCrossCount++

                $rows.Add((New-HelioCrossRow -Prefix 'HELIOCROSSUT' -Func 'HELIO_CROSS_UT' -Ipl $ipl -X2Cross $x2 -Tjd $tjd -FlagName 'PLAIN' -IFlag $iflag -Dir $dir))
                $helioCrossUtCount++
            }
        }
    }
}

foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HousesExGeoLats) {
        foreach ($tjd in $HousesExJdsFiles) {
            foreach ($combo in $HousesExFlagCombos) {
                $iflag = $combo.Flag
                $sidMode = if ($combo.NeedsSid) { Get-NextSidMode } else { $null }

                $rows.Add((New-HousesExRow -Prefix 'HOUSESEX' -Func 'HOUSES_EX' -Hsys $hsys -GeoLat $geolat -GeoLon $HousesExGeoLon -Tjd $tjd `
                    -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
                $housesExCount++

                $rows.Add((New-HousesExRow -Prefix 'HOUSESEX2' -Func 'HOUSES_EX2' -Hsys $hsys -GeoLat $geolat -GeoLon $HousesExGeoLon -Tjd $tjd `
                    -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
                $housesEx2Count++
            }
        }
    }
}

foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HousesArmcEx2GeoLats) {
        foreach ($armc in $HousesArmcEx2Armcs) {
            $rows.Add((New-HousesArmcEx2Row -Hsys $hsys -GeoLat $geolat -Eps $HousesArmcEx2Eps -Armc $armc))
            $housesArmcEx2Count++
        }
    }
}

foreach ($ipl in $NodApsAcceptedIplFiles) {
    foreach ($method in $NodApsMethodsFiles) {
        foreach ($tjd in $NodApsTjdFiles) {
            $rows.Add((New-NodApsUtRow -Ipl $ipl -Tjd $tjd -IFlag $SEFLG_SWIEPH -Method $method))
            $nodApsUtCount++
        }
    }
}
foreach ($ipl in $NodApsRejectedIplFiles) {
    $rows.Add((New-NodApsUtRow -Ipl $ipl -Tjd $NodApsRejectedTjdFiles -IFlag $SEFLG_SWIEPH -Method 0))
    $nodApsUtCount++
}

foreach ($pair in $PctrBodyPairs) {
    foreach ($tjd in $PctrTjdFiles) {
        foreach ($combo in $PctrFlagCombos) {
            $iflag = $SEFLG_SWIEPH -bor $combo.Flag
            $sidMode = if ($combo.NeedsSid) { Get-NextSidMode } else { $null }

            $rows.Add((New-PctrRow -Ipl $pair.Ipl -Iplctr $pair.Iplctr -Tjd $tjd -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
            $pctrCount++
        }
    }
}
$rows.Add((New-PctrRow -Ipl $PctrIdenticalIpl -Iplctr $PctrIdenticalIpl -Tjd $PctrTjdFiles[0] -FlagName 'PLAIN' -IFlag $SEFLG_SWIEPH -SidMode $null))
$pctrCount++

foreach ($ifno in $GetCurrentFileDataBoundary) {
    $rows.Add((New-GetCurrentFileDataRow -Label 'BOUNDARY' -Ifno $ifno -Ipl $null -Tjd $null -IFlag $null -Star $null))
    $getCurrentFileDataCount++
}
foreach ($ifno in $GetCurrentFileDataNoData) {
    $rows.Add((New-GetCurrentFileDataRow -Label 'NODATA' -Ifno $ifno -Ipl $null -Tjd $null -IFlag $null -Star $null))
    $getCurrentFileDataCount++
}
$rows.Add((New-GetCurrentFileDataRow -Label 'AUTOREAL' -Ifno $GetCurrentFileDataAutoReal -Ipl $null -Tjd $null -IFlag $null -Star $null))
$getCurrentFileDataCount++
$rows.Add((New-GetCurrentFileDataRow -Label 'PRECALC' -Ifno 0 -Ipl $GetCurrentFileDataPreCalcIpl -Tjd $GetCurrentFileDataPreCalcTjd -IFlag $GetCurrentFileDataPreCalcIFlag -Star $null))
$getCurrentFileDataCount++
$rows.Add((New-GetCurrentFileDataRow -Label 'PRECALC' -Ifno 2 -Ipl $GetCurrentFileDataPreCalcAsteroidIpl -Tjd $GetCurrentFileDataPreCalcTjd -IFlag $GetCurrentFileDataPreCalcIFlag -Star $null))
$getCurrentFileDataCount++
$rows.Add((New-GetCurrentFileDataRow -Label 'PREFIXSTAR' -Ifno 4 -Ipl $null -Tjd $GetCurrentFileDataPreCalcTjd -IFlag $GetCurrentFileDataPreCalcIFlag -Star $GetCurrentFileDataPreFixstarStar))
$getCurrentFileDataCount++

$totalRows = $rows.Count
$expectedTotal = $calcCount + $calcUtCount + $fixstarCount + $fixstarUtCount + $fixstar2Count + $fixstar2UtCount + $fixstarMagCount + $fixstar2MagCount + $nameCount +
    $solCrossCount + $solCrossUtCount + $moonCrossCount + $moonCrossUtCount + $moonCrossNodeCount + $moonCrossNodeUtCount + $helioCrossCount + $helioCrossUtCount +
    $housesExCount + $housesEx2Count + $housesArmcEx2Count + $nodApsUtCount + $pctrCount + $getCurrentFileDataCount
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
    '# swe_fixstar / swe_fixstar_ut / swe_fixstar2 / swe_fixstar2_ut / swe_fixstar_mag /'
    '# swe_fixstar2_mag: eight star search strings covering a plain name, a multi-word name, a'
    '# leading-comma Bayer-designation-only string, and a combined "name,bayer" string, crossed'
    '# with three Julian days and two iflag combinations (swe_fixstar_mag/swe_fixstar2_mag take'
    '# neither a date nor a flag, so each gets one row per star name).'
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
    '# swe_solcross / swe_solcross_ut / swe_mooncross / swe_mooncross_ut / swe_mooncross_node /'
    '# swe_mooncross_node_ut / swe_helio_cross / swe_helio_cross_ut: the same eight crossing'
    '# functions gen-grid-analytic.ps1 covers under SEFLG_MOSEPH, here under SEFLG_SWIEPH so the'
    '# file-backed code path is exercised too. Every date is chosen with enough margin from the'
    '# 1200/2399 span edges and the 1800 file boundary that even the slowest crossing search these'
    '# rows can trigger cannot walk past the two shipped files'' combined coverage; swe_helio_cross''s'
    '# own body list is narrower than gen-grid-analytic.ps1''s for the same reason (a heliocentric'
    '# search can take up to about one orbital period to converge, and Uranus/Neptune/Pluto''s'
    '# 84/165/248-year periods are close enough to this grid''s file-era margins to risk it).'
    '#'
    '# swe_houses_ex2 / swe_houses_armc_ex2: new in 2.10.03 (absent from'
    '# external/pyswisseph-2.08/swephexp.h entirely). Reached today only through swe_houses/'
    '# swe_houses_ex, which always pass cusp_speed/ascmc_speed/serr as NULL (swehouse.c:173,186),'
    '# so h.do_speed/h.do_hspeed (swehouse.c:642-647) stay FALSE and the 2.10 speed feature is'
    '# switched off in every row that reaches it that way. These rows call the _ex2 forms directly,'
    '# with real cusp_speed/ascmc_speed arrays, so the speed writes actually execute. Guarded'
    '# behind SWISSEPH_HAS_HOUSES_EX2 in sedump.c, the same pattern SWISSEPH_HAS_CROSSING already'
    '# uses for the eight crossing functions -- Tools/OracleDump/Program.cs has no SWISSEPH_HAS_*'
    '# symbol at all, correctly, since the port is single-version and has nothing to guard; only'
    '# sedump.c is compiled against two library versions. HOUSES_ARMC_EX2 touches no file itself (pure'
    '# geometry, like HOUSES_ARMC), so it is added here for dispatch/schema parity with'
    '# grid-analytic.tsv, not file-layer coverage -- see New-HousesArmcEx2Row''s own comment.'
    '#'
    '# swe_calc_pctr (PCTR) / swe_get_current_file_data (GET_CURRENT_FILE_DATA): the remaining two'
    '# of the twelve entry points new in 2.10.03, and files-grid-only. swe_calc_pctr forces'
    '# SEFLG_BARYCTR unconditionally (sweph.c:8061), and SEFLG_BARYCTR|SEFLG_MOSEPH is rejected'
    '# outright before any geometry runs (sweph.c:634-638) -- grid-analytic.tsv''s forced-'
    '# SEFLG_MOSEPH rows could only ever reach that reject, the same SE_CHIRON category error this'
    '# script''s own $HelioCrossIplFiles comment already documents for a different func, so PCTR'
    '# rows live here instead. Three body pairs (Mars-from-Jupiter, Sun-from-Earth, Venus-from-'
    '# Earth) crossed with two dates and three iflag combinations (PLAIN, SPEED, SIDEREAL), plus'
    '# one row exercising the "ipl and iplctr must not be identical" reject (sweph.c:8050-8054) --'
    '# see New-PctrRow''s own comment. swe_get_current_file_data reads swed.fidat, which'
    '# grid-analytic.tsv''s rows never populate at all, so it too is files-grid-only. Ifno alone'
    '# (-1, 5 out of range; 0/2/3/4 in range but not yet populated) covers the boundary and'
    '# empty-fnam reject branches; ifno 1 (the Moon file) reports real data with no other input on'
    '# the row at all, because this grid''s own swe_set_ephe_path call already opens it (sweph.c:'
    '# 1343-1350); ifno 0/2/4 reach real data too, through an optional preceding swe_calc or'
    '# swe_fixstar2 this func''s own row reuses ipl/tjd/iflag/star to trigger -- see'
    '# New-GetCurrentFileDataRow''s own comment for why ifno 3 has no real-data row at all in this'
    '# grid.'
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
    '#   func       CALC | CALC_UT | FIXSTAR | FIXSTAR_UT | FIXSTAR2 | FIXSTAR2_UT | FIXSTAR_MAG |'
    '#              FIXSTAR2_MAG | GET_PLANET_NAME | SOLCROSS | SOLCROSS_UT | MOONCROSS |'
    '#              MOONCROSS_UT | MOONCROSS_NODE | MOONCROSS_NODE_UT | HELIO_CROSS |'
    '#              HELIO_CROSS_UT | HOUSES_EX | HOUSES_EX2 | HOUSES_ARMC_EX2 | NOD_APS_UT | PCTR |'
    '#              GET_CURRENT_FILE_DATA'
    '#   ipl        body number                                        [CALC, CALC_UT,'
    '#              GET_PLANET_NAME, HELIO_CROSS, HELIO_CROSS_UT, NOD_APS_UT, PCTR (first body);'
    '#              GET_CURRENT_FILE_DATA (optional, triggers a preceding swe_calc)]'
    '#   tjd        Julian day (ET for CALC/FIXSTAR/FIXSTAR2/SOLCROSS/MOONCROSS/MOONCROSS_NODE/'
    '#              HELIO_CROSS/PCTR; UT for the corresponding _UT funcs)'
    '#              [CALC, CALC_UT, FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT, SOLCROSS,'
    '#              SOLCROSS_UT, MOONCROSS, MOONCROSS_UT, MOONCROSS_NODE, MOONCROSS_NODE_UT,'
    '#              HELIO_CROSS, HELIO_CROSS_UT, PCTR; GET_CURRENT_FILE_DATA (optional, paired with'
    '#              ipl or star to trigger a preceding swe_calc/swe_fixstar2)]'
    '#   iflag      swe_calc/swe_fixstar/crossing-func/swe_calc_pctr iflag, with SEFLG_SWIEPH'
    '#              already OR-ed in [CALC, CALC_UT, FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT,'
    '#              SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT, MOONCROSS_NODE,'
    '#              MOONCROSS_NODE_UT, HELIO_CROSS, HELIO_CROSS_UT, PCTR; GET_CURRENT_FILE_DATA'
    '#              (optional, paired with ipl or star)]'
    '#   star       star name or search string                         [FIXSTAR, FIXSTAR_UT,'
    '#              FIXSTAR2, FIXSTAR2_UT, FIXSTAR_MAG, FIXSTAR2_MAG; GET_CURRENT_FILE_DATA'
    '#              (optional, triggers a preceding swe_fixstar2)]'
    '#   geolon     geographic longitude, degrees east                 [CALC/CALC_UT topo rows; HOUSES_EX/HOUSES_EX2]'
    '#   geolat     geographic latitude, degrees north                 [CALC/CALC_UT topo rows; HOUSES_EX/HOUSES_EX2; HOUSES_ARMC_EX2]'
    '#   height     observer height above sea level, metres            [CALC/CALC_UT topo rows only]'
    '#   sid_mode   swe_set_sid_mode mode, applied before the row runs [CALC/CALC_UT/HOUSES_EX/'
    '#              HOUSES_EX2/PCTR rows whose iflag carries SEFLG_SIDEREAL]; cycled across all 47'
    '#              predefined modes (Get-NextSidMode), not pinned to one'
    '#   x2cross    target ecliptic longitude to cross, degrees        [SOLCROSS, SOLCROSS_UT,'
    '#              MOONCROSS, MOONCROSS_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   dir        swe_helio_cross(_ut) search direction: >= 0 forward, < 0 backward'
    '#              [HELIO_CROSS, HELIO_CROSS_UT]'
    '#   t0         SE_SIDM_USER reference epoch, TT; always empty in this grid today (this grid''s'
    '#              own SIDEREAL rows use only predefined modes) -- present so the schema matches'
    '#              gen-grid-analytic.ps1''s, which does use it'
    '#   ayan_t0    SE_SIDM_USER ayanamsa at t0, degrees; same emptiness note as t0'
    '#   method     swe_nod_aps_ut method bitmask                        [NOD_APS_UT]'
    '#   hsys       house-system letter               [HOUSES_EX, HOUSES_EX2, HOUSES_ARMC_EX2]'
    '#   armc       ARMC, degrees                                       [HOUSES_ARMC_EX2]'
    '#   eps        obliquity of the ecliptic, degrees                  [HOUSES_ARMC_EX2]'
    '#   iplctr     swe_calc_pctr''s second body (the planetocentric center)        [PCTR]'
    '#   ifno       swe_get_current_file_data''s file-slot index, 0-4 in range      [GET_CURRENT_FILE_DATA]'
    '#'
    '# x2cross and dir are appended after sid_mode, and t0/ayan_t0 after those, rather than'
    '# interleaved among the original ten columns, so every column this grid''s other funcs already'
    '# used keeps the same index it always had -- the same additive-not-renumbering choice'
    '# gen-grid-analytic.ps1 makes. method/hsys are a second additive tail after ayan_t0, for the'
    '# same reason: HOUSES_EX and NOD_APS_UT are the two of grid-analytic.tsv''s six new funcs'
    '# (HOUSES_EX/AYANAMSA_UT/SIDTIME/AZALT/HOUSE_NAME/NOD_APS_UT) where reading a real .se1 file'
    '# changes what gets exercised -- the sidereal ayanamsa behind swe_houses_ex, and'
    '# swe_nod_aps_ut''s planetary positions (including SE_CHIRON, which has no Moshier model at'
    '# all) -- so only those two get a func token here; the other four open no file, or would be'
    '# an identical code path to their grid-analytic.tsv coverage, and are covered there instead.'
    '# hsys sits where it does, rather than reusing a column HOUSES/HOUSES_ARMC might have shared'
    '# the way grid-analytic.tsv''s HOUSES_EX rows reuse fields[5], because this grid never carries'
    '# swe_houses/swe_houses_armc rows of its own and so never allocated an hsys column at all'
    '# until HOUSES_EX needed one. armc and eps are a THIRD additive tail, for the'
    '# same reason again: HOUSES_ARMC_EX2 is the first func this grid has ever carried that takes'
    '# an armc/eps pair as direct input (grid-analytic.tsv already carries them, for its own'
    '# HOUSES_ARMC rows). iplctr and ifno are a FOURTH additive tail, at the very end, for PCTR/'
    '# GET_CURRENT_FILE_DATA -- the two funcs this addition brings in reuse ipl/tjd/iflag/star for'
    '# everything else they need, so iplctr and ifno are the only genuinely new inputs. Each of'
    '# these additive tails is a two-grid schema change, since grid-jpl.tsv carries this header'
    '# byte-for-byte (see gen-grid-jpl.ps1''s own header).'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- both drivers assert it matches verbatim before reading any'
    '# data.'
)
$columnHeader = 'case_id' + "`t" + 'func' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag' + "`t" +
    'star' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'sid_mode' + "`t" +
    'x2cross' + "`t" + 'dir' + "`t" + 't0' + "`t" + 'ayan_t0' + "`t" + 'method' + "`t" + 'hsys' + "`t" +
    'armc' + "`t" + 'eps' + "`t" + 'iplctr' + "`t" + 'ifno'

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
Write-Host "  CALC               $calcCount"
Write-Host "  CALC_UT            $calcUtCount"
Write-Host "  FIXSTAR            $fixstarCount"
Write-Host "  FIXSTAR_UT         $fixstarUtCount"
Write-Host "  FIXSTAR2           $fixstar2Count"
Write-Host "  FIXSTAR2_UT        $fixstar2UtCount"
Write-Host "  FIXSTAR_MAG        $fixstarMagCount"
Write-Host "  FIXSTAR2_MAG       $fixstar2MagCount"
Write-Host "  GET_PLANET_NAME    $nameCount"
Write-Host "  SOLCROSS           $solCrossCount"
Write-Host "  SOLCROSS_UT        $solCrossUtCount"
Write-Host "  MOONCROSS          $moonCrossCount"
Write-Host "  MOONCROSS_UT       $moonCrossUtCount"
Write-Host "  MOONCROSS_NODE     $moonCrossNodeCount"
Write-Host "  MOONCROSS_NODE_UT  $moonCrossNodeUtCount"
Write-Host "  HELIO_CROSS        $helioCrossCount"
Write-Host "  HELIO_CROSS_UT     $helioCrossUtCount"
Write-Host "  HOUSES_EX          $housesExCount"
Write-Host "  HOUSES_EX2         $housesEx2Count"
Write-Host "  HOUSES_ARMC_EX2    $housesArmcEx2Count"
Write-Host "  NOD_APS_UT         $nodApsUtCount"
Write-Host "  PCTR               $pctrCount"
Write-Host "  GET_CURRENT_FILE_DATA $getCurrentFileDataCount"
