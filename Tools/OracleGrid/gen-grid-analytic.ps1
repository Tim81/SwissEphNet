#Requires -Version 7.3
<#
.SYNOPSIS
    Regenerates grid-analytic.tsv, the committed input set for the bit-exact oracle harness's
    first stage.

.DESCRIPTION
    Tools/BaselineMatrix already sweeps a wide set of Swiss Ephemeris calls, but each of its area
    generators (Calc.cs, Houses.cs, ...) builds its own inputs and calls the .NET API in the same
    method. A C driver written against that shape would have to reimplement the same
    input-building logic in C, and the two implementations would drift apart from each other
    silently over time -- exactly the failure this harness exists to catch, so it cannot be built
    that way. Instead this script is the ONLY place that decides what gets tested. It writes one
    file, grid-analytic.tsv, that both Tools/CReference/sedump.c and Tools/OracleDump/Program.cs
    read and replay -- neither driver builds a grid of its own; both are interpreters over these
    rows. Extending coverage later means adding rows here, not editing two programs in step.

    Covers three function families, chosen because between them they are roughly 90% of the
    conformance corpus (Tests/SwissEphNet.Conformance.Tests) and the bulk of the numeric porting
    work:

      swe_calc / swe_calc_ut  -- bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating
                                  apogee, Earth), crossed with a spread of Julian days (including
                                  two outside the Moshier-valid window, to exercise the ERR/serr
                                  path) and twelve iflag combinations. SEFLG_MOSEPH is OR-ed into
                                  every one of them, so every result depends on no ephemeris data
                                  file and is reproducible on any machine -- a file-backed grid is
                                  a later, separate stage. The TOPOCTR combination additionally
                                  carries a fixed geoposition and the SIDEREAL combination a sid
                                  mode drawn from Get-NextSidMode's cycle (every predefined mode
                                  0..46 in turn, not one fixed mode), each applied via its own
                                  swe_set_* call before that row's swe_calc/swe_calc_ut runs.

      swe_houses / swe_houses_armc -- every house-system letter this port actually implements
                                  (SwissEphNet/CPort/SweHouse.cs's switch statements). Upstream
                                  2.10.03 adds 'J', which this port does not implement yet, so 'J'
                                  is deliberately absent here -- adding it once the port supports
                                  it is future work for whoever lands that porting PR, not this
                                  script's job to anticipate. Crossed with geographic latitudes
                                  (including near-polar and polar-circle extremes, where Placidus
                                  and Koch degenerate), longitudes, Julian days and ARMC values.

      swe_solcross / swe_solcross_ut / swe_mooncross / swe_mooncross_ut / swe_mooncross_node /
                                  swe_mooncross_node_ut / swe_helio_cross / swe_helio_cross_ut --
                                  the eight crossing functions, none of which any grid covered
                                  before this addition (see Tests/conformance/regenerations.log's
                                  Phase 6 entry, which found suite 10.5-10.8 mismatches against
                                  t.exp for swe_helio_cross and swe_mooncross_node without any way
                                  to tell a port defect from a t.exp-vintage artifact). All eight
                                  carry SEFLG_MOSEPH, matching the rest of this grid. swe_solcross
                                  and swe_mooncross are crossed with a spread of target longitudes
                                  (including both 0.0 and 360.0, the two ends of the wraparound
                                  swe_degnorm folds together), start dates and (most of) the flag
                                  combinations each function's own doc comment names -- SEFLG_HELCTR
                                  is the one documented flag deliberately left out, because it
                                  drives swe_solcross into an unbounded loop inside libswe itself;
                                  see $SolCrossFlagCombos below for the mechanism.
                                  swe_mooncross_node takes no target longitude at all (it finds a
                                  zero-latitude node crossing, not a longitude crossing), so it is
                                  crossed with start dates and flags only. swe_helio_cross is
                                  crossed with target longitudes, start dates, both search
                                  directions the API takes, and a body list that deliberately
                                  includes SE_SUN/SE_MOON/SE_TRUE_NODE (one representative body
                                  the function rejects from each disjunct of its own reject check)
                                  alongside the bodies it accepts under SEFLG_MOSEPH. SE_CHIRON,
                                  the one body the function overrides with a hardcoded mean speed
                                  instead of swe_calc's own, is deliberately NOT in this grid's
                                  body list: it has no Moshier model, so it needs a real data
                                  file regardless of SEFLG_MOSEPH, which would make it the only
                                  row in this whole grid to open one -- see gen-grid-files.ps1,
                                  where it is covered against the real files instead.

      swe_get_ayanamsa / swe_get_ayanamsa_ex / swe_get_ayanamsa_ex_ut -- direct coverage of the
                                  ayanamsa machinery itself. Before this addition, every sid_mode
                                  this grid carried was exercised only indirectly, through a
                                  SEFLG_SIDEREAL swe_calc/crossing row -- proof the ayanamsa was
                                  applied to something, never a comparison of the ayanamsa value
                                  itself. Crossed with all 47 predefined modes (0..46) and, for
                                  swe_get_ayanamsa_ex/_ex_ut, two iflag combinations (0, NONUT);
                                  also carries SE_SIDM_USER (mode 255) with three t0/ayan_t0 pairs,
                                  the one sid mode this grid could not express at all before the
                                  t0/ayan_t0 columns below were added -- see $AyanamsaUserParams.
                                  None of the three opens an ephemeris file, so all three belong
                                  here rather than in gen-grid-files.ps1.

    A FRESH LIBRARY INSTANCE PER ROW (both drivers, not this script)

    swe_houses_armc carries a hidden field emulating a C static (saved_sundec, see
    Tools/BaselineGen/Program.cs's own header and SwissEphNet/CPort/SweHouse.cs) that changes
    hsys 'I'/'i' results depending on what a PRIOR call on the same library instance computed.
    Both drivers reset all library state before every single row -- sedump.c calls swe_close(),
    OracleDump constructs a new SwissEph() -- so this grid does not need to worry about call
    order; every row is independent of every other row by construction.

    COLUMN LAYOUT (documented again, verbatim, at the top of the generated file itself, since
    that is what a reader of grid-analytic.tsv actually opens first)

    Tab-separated, LF line endings, one call per data row. Lines starting with '#' are comments;
    the first non-comment line is the column-name header, which both drivers assert against
    verbatim rather than silently reading the wrong columns if this script's schema ever changes
    out from under them. Empty string means "does not apply to this row's func":

      case_id, func, ipl, tjd, iflag, hsys, geolon, geolat, height, armc, eps, sid_mode, x2cross,
      dir, t0, ayan_t0

    x2cross and dir are appended after sid_mode, and t0/ayan_t0 after those, not interleaved among
    the original twelve columns, so every existing column keeps the same index it always had --
    both additions are additive, not a renumbering. t0/ayan_t0 carry swe_set_sid_mode's own
    SE_SIDM_USER parameters (empty means 0.0, the same default an absent sid_mode already implied
    for every non-USER row) -- see $AyanamsaUserParams and New-AyanamsaRow/New-AyanamsaExRow.

.NOTES
    Deterministic by construction: no timestamps, no randomness, no machine-dependent state.
    Running this script twice must produce a byte-identical file -- that is what lets
    grid-analytic.tsv be committed as data instead of generated fresh on every run.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# No native (non-PowerShell) commands run in this script, so this changes nothing today -- kept
# for the same future-proofing reason Tools/CReference/build-c.ps1's own copy of this line gives.
$PSNativeCommandUseErrorActionPreference = $false

$outputPath = Join-Path $PSScriptRoot 'grid-analytic.tsv'

# ---------------------------------------------------------------------------------------
# Swiss Ephemeris constants (SwissEphNet/SwissEph.swephexp.h.cs / external/swisseph/swephexp.h).
# Named here rather than inlined as bare integers so the flag-combination table below reads as
# what it means, not as opaque numbers that would need cross-referencing to check.
# ---------------------------------------------------------------------------------------

$SEFLG_MOSEPH     = 4
$SEFLG_HELCTR     = 8
$SEFLG_TRUEPOS    = 16
$SEFLG_J2000      = 32
$SEFLG_NONUT      = 64
$SEFLG_SPEED      = 256
$SEFLG_EQUATORIAL = 2 * 1024
$SEFLG_XYZ        = 4 * 1024
$SEFLG_RADIANS    = 8 * 1024
$SEFLG_BARYCTR    = 16 * 1024
$SEFLG_TOPOCTR    = 32 * 1024
$SEFLG_SIDEREAL   = 64 * 1024
$SE_SIDM_LAHIRI   = 1
$SE_SIDM_USER     = 255
# Deliberately a literal, not read off any assembly's SwissEph.SE_NSIDM_PREDEF -- this script
# runs standalone PowerShell and loads no .NET assembly at all, so there is nothing to read the
# constant off in the first place, but the value is pinned here for the same reason
# Tools/BaselineMatrix/Ayanamsa.cs's own SidModeSweepCount is a literal: the sid-mode sweep below
# is a property of what this grid deliberately covers, not of whichever local build happens to
# define SE_NSIDM_PREDEF. See that file's own comment for the 47-vs-43 (port vs. SwissEphNet
# 2.8.0.2 NuGet package) divergence this sidesteps.
$SidModeSweepCount = 47

# swe_nod_aps(_ut)'s own reject check (external/swisseph/swecl.c:5126-5129): the four named
# lunar-node/apogee bodies, ipl < 0, and SE_NPLANETS <= ipl <= SE_AST_OFFSET.
$SE_MEAN_NODE   = 10
$SE_TRUE_NODE   = 11
$SE_MEAN_APOG   = 12
$SE_OSCU_APOG   = 13
$SE_EARTH       = 14
$SE_NPLANETS    = 23
$SE_AST_OFFSET  = 10000
# swe_nod_aps(_ut)'s method bitmask (external/swisseph/swephexp.h:291-294): SE_NODBIT_FOPOINT is
# read once as a flag (do_focal_point) and then the method value itself is reduced mod it
# (swecl.c:5099,5114), so a caller may combine it with any of the other three.
$SE_NODBIT_MEAN     = 1
$SE_NODBIT_OSCU     = 2
$SE_NODBIT_OSCU_BAR = 4
$SE_NODBIT_FOPOINT  = 256

# swe_azalt's calc_flag (external/swisseph/swephexp.h:364-365): which coordinate frame xin is in.
$SE_ECL2HOR = 0
$SE_EQU2HOR = 1

# ---------------------------------------------------------------------------------------
# Formatting -- matches Tools/BaselineMatrix/Format.cs's D()/I(): invariant culture, "R"
# round-trip precision for doubles, so every machine that runs this script (and the drivers
# that later parse its output) reads the identical digits.
# ---------------------------------------------------------------------------------------

function Fmt {
    param([double] $Value)
    return $Value.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}

function FmtI {
    param([int] $Value)
    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# Count evenly spaced Julian days across [Lo, Hi], inclusive of both ends. Mirrors
# Tools/BaselineMatrix/Grids.cs's JdSpread, but this script does not call into that project --
# see the header on why the grid may not depend on BaselineMatrix's input-building code.
function Get-JdSpread {
    param([int] $Count, [double] $Lo, [double] $Hi)
    if ($Count -lt 2) {
        throw "Get-JdSpread needs at least 2 points to span a range, got $Count."
    }
    $step = ($Hi - $Lo) / ($Count - 1)
    $values = for ($i = 0; $i -lt $Count; $i++) { $Lo + $i * $step }
    return $values
}

# Cycles deterministically through the 47 predefined sidereal modes (0..46), one call per row
# that needs a sid_mode -- replaces every prior hardcoded $SE_SIDM_LAHIRI at a SIDEREAL row site,
# so the SEFLG_SIDEREAL rows this grid already had (swe_calc/swe_calc_ut and the solar/lunar
# crossing functions) sweep the same 47-mode space Tools/BaselineMatrix/Ayanamsa.cs sweeps for
# direct ayanamsa coverage, instead of exercising the sidereal machinery through one mode only.
# Deliberately NOT crossed with every existing dimension (body x date x flag x mode would multiply
# row count by 47) -- see this script's own header for why: the sidereal correction is applied
# uniformly regardless of body, so one mode per existing row is enough to prove every mode's
# arithmetic is reachable through swe_calc/the crossing functions, and the dedicated AYANAMSA
# rows below are where the full mode x date cross product actually lives. SE_SIDM_USER is
# deliberately excluded from this cycle (predefined modes only): it has its own dedicated
# coverage below via the AYANAMSA family, which is a more direct way to pin it than folding a
# 255th value into a cycle sized for 47.
$script:sidModeCycleNext = 0
function Get-NextSidMode {
    $mode = $script:sidModeCycleNext % $SidModeSweepCount
    $script:sidModeCycleNext++
    return $mode
}

# ---------------------------------------------------------------------------------------
# Row builders
# ---------------------------------------------------------------------------------------

function New-CalcRow {
    param(
        [string] $Prefix,
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
    $caseId = "$Prefix|$(FmtI $Ipl)|$(Fmt $Tjd)|$FlagName"
    $geolonField  = if ($null -eq $GeoLon)  { '' } else { Fmt ([double]$GeoLon) }
    $geolatField  = if ($null -eq $GeoLat)  { '' } else { Fmt ([double]$GeoLat) }
    $heightField  = if ($null -eq $Height)  { '' } else { Fmt ([double]$Height) }
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $t0Field      = if ($null -eq $T0)      { '' } else { Fmt ([double]$T0) }
    $ayanT0Field  = if ($null -eq $AyanT0)  { '' } else { Fmt ([double]$AyanT0) }
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        $geolonField, $geolatField, $heightField, '', '', $sidModeField, '', '',
        $t0Field, $ayanT0Field, '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-HousesRow {
    param([char] $Hsys, [double] $GeoLat, [double] $GeoLon, [double] $Tjd)
    $caseId = "HOUSES|$Hsys|$(Fmt $GeoLat)|$(Fmt $GeoLon)|$(Fmt $Tjd)"
    $fields = @(
        $caseId, 'HOUSES', '', (Fmt $Tjd), '', "$Hsys",
        (Fmt $GeoLon), (Fmt $GeoLat), '', '', '', '', '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-HousesArmcRow {
    param([char] $Hsys, [double] $GeoLat, [double] $Eps, [double] $Armc)
    $caseId = "HOUSESARMC|$Hsys|$(Fmt $GeoLat)|$(Fmt $Eps)|$(Fmt $Armc)"
    $fields = @(
        $caseId, 'HOUSES_ARMC', '', '', '', "$Hsys",
        '', (Fmt $GeoLat), '', (Fmt $Armc), (Fmt $Eps), '', '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_houses_armc_ex2 -- new in 2.10.03 (absent from external/pyswisseph-2.08/swephexp.h entirely
# -- verified: zero matches for either "swe_houses_ex2" or "swe_houses_armc_ex2" anywhere under
# that tree). swe_houses/swe_houses_ex already reach it on every HOUSES/HOUSES_EX row
# (swehouse.c:173,186 delegate to it), but always with cusp_speed/ascmc_speed/serr hardcoded NULL,
# so h.do_speed/h.do_hspeed (swehouse.c:642-647) stay FALSE and the 2.10 speed feature the _ex2
# form adds is switched off in every row that reaches it that way. This func is called directly,
# with real cusp_speed/ascmc_speed arrays, so those writes (swehouse.c:663,671,685) actually
# execute. Same armc/eps/hsys input shape as HOUSES_ARMC above; ascmc is zero-initialized by both
# drivers before the call exactly as it already is for HOUSES_ARMC, so ascmc[9] is 0.0 (not 99) on
# every row here too -- swe_houses_armc_ex2's hsys 'I' branch (swehouse.c:648-660) only ever reads
# the saved_sundec static when ascmc[9] == 99, so every row here still takes the write branch,
# never the carried-over-state read branch; see sedump.c's own "FRESH LIBRARY STATE PER ROW"
# section for the fuller reasoning, unchanged by this addition.
function New-HousesArmcEx2Row {
    param([char] $Hsys, [double] $GeoLat, [double] $Eps, [double] $Armc)
    $caseId = "HOUSESARMCEX2|$Hsys|$(Fmt $GeoLat)|$(Fmt $Eps)|$(Fmt $Armc)"
    $fields = @(
        $caseId, 'HOUSES_ARMC_EX2', '', '', '', "$Hsys",
        '', (Fmt $GeoLat), '', (Fmt $Armc), (Fmt $Eps), '', '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_solcross/_ut and swe_mooncross/_ut share one C signature shape (x2cross, tjd, iflag, serr)
# and one row shape here: neither takes a body, a house system, a geoposition or an ARMC/eps
# pair, so every column except tjd/iflag/sid_mode/x2cross stays empty.
function New-SolarLunarCrossRow {
    param(
        [string] $Prefix,
        [string] $Func,
        [double] $X2Cross,
        [double] $Tjd,
        [string] $FlagName,
        [int]    $IFlag,
        $SidMode
    )
    $caseId = "$Prefix|$(Fmt $X2Cross)|$(Fmt $Tjd)|$FlagName"
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', '', $sidModeField, (Fmt $X2Cross), '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_mooncross_node/_ut takes no target longitude at all -- it finds a zero-*latitude* node
# crossing, not a crossing over a caller-supplied longitude -- so x2cross stays empty here, unlike
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
        '', '', '', '', '', '', '', '', '', '',
        '', '', '', '', '', ''
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
        '', '', '', '', '', '', (Fmt $X2Cross), (FmtI $Dir), '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_get_ayanamsa / swe_get_ayanamsa_ex / swe_get_ayanamsa_ex_ut -- direct ayanamsa coverage (see
# this script's own .DESCRIPTION for why the oracle previously exercised this machinery only
# indirectly, through SEFLG_SIDEREAL swe_calc/crossing rows). None of the three opens an ephemeris
# data file, so all three belong here, not in gen-grid-files.ps1. IsUser controls only the case_id
# shape (SE_SIDM_USER's t0/ayan_t0 need to be in the id for uniqueness across the three UserModeParams
# pairs below; a predefined mode's id is unique on sid_mode alone) -- the row itself always carries
# whatever T0/AyanT0 the caller passes (0/0 for a predefined mode, via apply_sid_mode's own
# has_value/empty-means-zero convention on the driver side).
function New-AyanamsaRow {
    param([int] $SidMode, [double] $Tjd, [double] $T0, [double] $AyanT0, [bool] $IsUser)
    $caseId = if ($IsUser) { "AYANAMSA|USER|$(Fmt $T0)|$(Fmt $AyanT0)|$(Fmt $Tjd)" } else { "AYANAMSA|$(FmtI $SidMode)|$(Fmt $Tjd)" }
    $fields = @(
        $caseId, 'AYANAMSA', '', (Fmt $Tjd), '', '',
        '', '', '', '', '', (FmtI $SidMode), '', '',
        (Fmt $T0), (Fmt $AyanT0), '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-AyanamsaExRow {
    param([string] $Func, [int] $SidMode, [double] $Tjd, [string] $FlagName, [int] $IFlag, [double] $T0, [double] $AyanT0, [bool] $IsUser)
    $prefix = if ($Func -eq 'AYANAMSA_EX') { 'AYANAMSAEX' } else { 'AYANAMSAEXUT' }
    $caseId = if ($IsUser) { "$prefix|USER|$(Fmt $T0)|$(Fmt $AyanT0)|$(Fmt $Tjd)|$FlagName" } else { "$prefix|$(FmtI $SidMode)|$(Fmt $Tjd)|$FlagName" }
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', '', (FmtI $SidMode), '', '',
        (Fmt $T0), (Fmt $AyanT0), '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_houses_ex -- the sidereal/radians-capable sibling of swe_houses (see New-HousesRow above).
# Unlike swe_houses, it takes an iflag, so a sid_mode/t0/ayan_t0 triple may be present -- SidMode
# is $null for a non-sidereal row, matching every other *SidMode param in this script. Shared with
# HOUSES_EX2 below (Prefix/Func distinguish swe_houses_ex from swe_houses_ex2 -- identical input
# shape; see sedump.c's process_houses_ex/process_houses_ex2 for the output-side difference).
function New-HousesExRow {
    param([string] $Prefix, [string] $Func, [char] $Hsys, [double] $GeoLat, [double] $GeoLon, [double] $Tjd, [string] $FlagName, [int] $IFlag, $SidMode)
    $caseId = "$Prefix|$Hsys|$(Fmt $GeoLat)|$(Fmt $GeoLon)|$(Fmt $Tjd)|$FlagName"
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, $Func, '', (Fmt $Tjd), (FmtI $IFlag), "$Hsys",
        (Fmt $GeoLon), (Fmt $GeoLat), '', '', '', $sidModeField, '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_get_ayanamsa_ut -- the UT sibling of swe_get_ayanamsa (New-AyanamsaRow above). Same shape,
# same sid_mode/t0/ayan_t0 handling; only the func token and case_id prefix differ, since Tjd here
# is UT rather than ET.
function New-AyanamsaUtRow {
    param([int] $SidMode, [double] $Tjd, [double] $T0, [double] $AyanT0, [bool] $IsUser)
    $caseId = if ($IsUser) { "AYANAMSAUT|USER|$(Fmt $T0)|$(Fmt $AyanT0)|$(Fmt $Tjd)" } else { "AYANAMSAUT|$(FmtI $SidMode)|$(Fmt $Tjd)" }
    $fields = @(
        $caseId, 'AYANAMSA_UT', '', (Fmt $Tjd), '', '',
        '', '', '', '', '', (FmtI $SidMode), '', '',
        (Fmt $T0), (Fmt $AyanT0), '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_sidtime -- a bare double, no serr, no sid_mode of its own (sidereal *time*, not the
# ayanamsha) -- so tjd is the only input.
function New-SidtimeRow {
    param([double] $Tjd)
    $caseId = "SIDTIME|$(Fmt $Tjd)"
    $fields = @(
        $caseId, 'SIDTIME', '', (Fmt $Tjd), '', '',
        '', '', '', '', '', '', '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_azalt -- geopos reuses this grid's existing geolon/geolat/height columns (fields[6..8]);
# calc_flag/atpress/attemp/xin0/xin1 are the new trailing columns this func needs. xin[2] is
# deliberately not a column at all -- swe_azalt's own body never reads it (see
# Tools/CReference/sedump.c's process_azalt) -- so there is no Xin2 parameter here either.
function New-AzAltRow {
    param(
        [string] $CalcFlagName, [int] $CalcFlag,
        [double] $GeoLon, [double] $GeoLat, [double] $Height,
        [string] $PressName, [double] $AtPress, [double] $Attemp,
        [string] $XinName, [double] $Xin0, [double] $Xin1,
        [double] $Tjd
    )
    $caseId = "AZALT|$CalcFlagName|$(Fmt $GeoLat)|$PressName|$XinName|$(Fmt $Tjd)"
    $fields = @(
        $caseId, 'AZALT', '', (Fmt $Tjd), '', '',
        (Fmt $GeoLon), (Fmt $GeoLat), (Fmt $Height), '', '', '', '', '', '', '',
        '', (FmtI $CalcFlag), (Fmt $AtPress), (Fmt $Attemp), (Fmt $Xin0), (Fmt $Xin1)
    )
    return ($fields -join "`t")
}

# swe_house_name -- a pure lookup, so only hsys matters; reuses the shared hsys column HOUSES/
# HOUSES_ARMC/HOUSES_EX already carry at fields[5].
function New-HouseNameRow {
    param([char] $Hsys)
    $caseId = "HOUSENAME|$Hsys"
    $fields = @(
        $caseId, 'HOUSE_NAME', '', '', '', "$Hsys",
        '', '', '', '', '', '', '', '', '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# swe_nod_aps_ut -- ipl reuses the shared ipl column swe_calc/swe_helio_cross already carry at
# fields[2]; method is the new trailing column this func needs (analytic-grid position 16).
function New-NodApsUtRow {
    param([int] $Ipl, [double] $Tjd, [int] $IFlag, [int] $Method)
    $caseId = "NODAPSUT|$(FmtI $Ipl)|$(Fmt $Tjd)|$(FmtI $Method)"
    $fields = @(
        $caseId, 'NOD_APS_UT', (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        '', '', '', '', '', '', '', '', '', '',
        (FmtI $Method), '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# ---------------------------------------------------------------------------------------
# Grid values
# ---------------------------------------------------------------------------------------

# SE_SUN..SE_EARTH (0-14): Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune,
# Pluto, mean node, true node, mean apogee, oscillating apogee, Earth.
$Bodies = 0..14

# Ten points spread across the Moshier-valid window (matches
# Tools/BaselineMatrix/Grids.cs's JdSpread default range), plus two points outside it
# (500000, 3000000) to exercise the ERR return and serr message on both sides.
$CalcJds = @(Get-JdSpread -Count 10 -Lo 1000000 -Hi 2600000) + @(500000.0, 3000000.0)

$TopoGeoLon = -118.24
$TopoGeoLat = 34.05
$TopoHeight = 100.0

# The twelve combinations the task calls out explicitly: one plain baseline plus every flag
# whose effect on the numeric result is worth freezing on its own. NeedsTopo/NeedsSid mark the
# two combinations that require a swe_set_topo/swe_set_sid_mode call before the row's
# swe_calc/swe_calc_ut runs -- SEFLG_TOPOCTR and SEFLG_SIDEREAL mean nothing on a library
# instance that was never told a geoposition or ayanamsha.
$FlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';      Flag = 0;                 NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'SPEED';      Flag = $SEFLG_SPEED;      NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'EQUATORIAL'; Flag = $SEFLG_EQUATORIAL; NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'XYZ';        Flag = $SEFLG_XYZ;        NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'RADIANS';    Flag = $SEFLG_RADIANS;    NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'J2000';      Flag = $SEFLG_J2000;      NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'NONUT';      Flag = $SEFLG_NONUT;      NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'TRUEPOS';    Flag = $SEFLG_TRUEPOS;    NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'HELCTR';     Flag = $SEFLG_HELCTR;     NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'BARYCTR';    Flag = $SEFLG_BARYCTR;    NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'TOPOCTR';    Flag = $SEFLG_TOPOCTR;    NeedsTopo = $true;  NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL';   Flag = $SEFLG_SIDEREAL;   NeedsTopo = $false; NeedsSid = $true }
)

# Every house-system letter SwissEphNet/CPort/SweHouse.cs actually implements a case for
# (confirmed by grepping its switch statements). Deliberately excludes 'J' (upstream 2.10.03
# only; not yet ported) and any letter this port has no case for at all -- an untested letter
# here would silently exercise only the shared default/fallback branch under a name that implies
# real per-system coverage.
$HouseLetters = @(
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'i', 'K', 'L', 'M', 'N', 'O',
    'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y'
)

# Near-polar and polar-circle extremes alongside ordinary latitudes: Placidus and Koch degenerate
# past the polar circle, and that boundary itself moves with obliquity (see HouseEps below).
$HouseGeoLats = @(-89, -80, -70, -66, -60, 0, 60, 66, 70, 80, 89)

# Los Angeles, Greenwich, Tokyo: a negative longitude, an exact zero, and a positive longitude.
# The negative and positive values are deliberately not round numbers, so neither could hide a
# sign or wraparound bug; Greenwich's zero is exact on purpose, to cover that boundary itself.
$HouseGeoLons = @(-118.24, 0.0, 139.6917)

$HouseJds = Get-JdSpread -Count 4 -Lo 1000000 -Hi 2600000

$HouseArmcs = 0..7 | ForEach-Object { $_ * 45.0 }

# Real mean obliquity, the degenerate eps=0 edge case, and eps=40 -- eps=40 moves the
# polar-degeneracy boundary for Placidus/Koch to |geolat| = 50, putting a different subset of
# HouseGeoLats onto the degenerate branch than the real obliquity does (matches
# Tools/BaselineMatrix/Grids.cs's Eps and its own comment on why that third value is not just
# another arithmetic sample).
$HouseEps = @(23.4392911, 0.0, 40.0)

# ---------------------------------------------------------------------------------------
# Crossing-function grid values (swe_solcross/_ut, swe_mooncross/_ut, swe_mooncross_node/_ut,
# swe_helio_cross/_ut). See this script's own .DESCRIPTION for why these eight funcs are the gap
# this addition closes.
# ---------------------------------------------------------------------------------------

# Six target longitudes for swe_solcross/swe_mooncross/swe_helio_cross. 0.0 and 360.0 are both
# included on purpose even though swe_degnorm folds them to the identical normalized distance --
# a bit-identical *result* is still worth its own grid row here, since it proves the wraparound
# normalization at both of its own boundary inputs, not just one. 90/180/270 cover the other three
# quadrants; 359.9999 sits just below the wraparound edge from the other side.
$CrossX2 = @(0.0, 90.0, 180.0, 270.0, 359.9999, 360.0)

# Two start dates well inside the Moshier-valid window $CalcJds above already spans -- early and
# late. Unlike $CalcJds, this dimension does not need a wide spread: swe_solcross/swe_mooncross
# only ever search up to about one solar/lunar period ahead of the start date, so a second,
# distant start date is enough to prove the search still works when it starts from a very
# different point in the ephemeris.
$CrossTjd = @(1200000.0, 2400000.0)

# swe_solcross's own doc comment (external/swisseph/sweph.c:8312-8315) names SEFLG_HELCTR,
# SEFLG_TRUEPOS and SEFLG_NONUT, plus SEFLG_SIDEREAL, which the sibling swe_mooncross_ut doc
# comment documents explicitly (see $MoonCrossFlagCombos below) and which swe_calc itself always
# recognizes regardless of which wrapper calls it. SEFLG_HELCTR is deliberately NOT included
# below, even though the doc comment names it: swe_solcross hardcodes ipl = SE_SUN
# (external/swisseph/sweph.c:8325) and never substitutes SE_EARTH the way its own comment's "1 =
# heliocentric, EARTH" wording implies, so SEFLG_HELCTR actually asks swe_calc for the
# heliocentric position of the Sun itself -- the coordinate origin, with an always-zero speed.
# Measured: for x2cross values where the initial distance estimate does not already land within
# CROSS_PRECISION on the first pass (every value here except 0.0/360.0, which converge in one
# step because dist starts at exactly 0), the refinement loop's `jd += dist / x[3]` divides a
# nonzero dist by that zero speed, drives jd to +Infinity, and the next swe_calc(Infinity, ...)
# call inside libswe never returns -- confirmed by isolating SOLCROSS|90|1200000|HELCTR against
# the built sedump.exe and observing unbounded CPU time with no output. That is a hang, not merely
# a slow or degenerate result, so it cannot be a grid row: a case this driver can never finish is
# not a test case, it is a way to make every future run of this harness never complete either.
$SolCrossFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;               NeedsSid = $false }
    [pscustomobject]@{ Name = 'TRUEPOS';  Flag = $SEFLG_TRUEPOS;  NeedsSid = $false }
    [pscustomobject]@{ Name = 'NONUT';    Flag = $SEFLG_NONUT;    NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsSid = $true }
)

# swe_mooncross/swe_mooncross_ut's own doc comments (external/swisseph/sweph.c:8380-8383,
# 8415-8423) list SEFLG_TRUEPOS, SEFLG_NONUT and SEFLG_SIDEREAL; they do not document SEFLG_HELCTR
# the way swe_solcross does (a heliocentric Moon has no defined meaning), so this list omits it
# rather than exercising a combination outside either function's documented contract.
$MoonCrossFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;               NeedsSid = $false }
    [pscustomobject]@{ Name = 'TRUEPOS';  Flag = $SEFLG_TRUEPOS;  NeedsSid = $false }
    [pscustomobject]@{ Name = 'NONUT';    Flag = $SEFLG_NONUT;    NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsSid = $true }
)

# swe_mooncross_node/_ut find a zero-*latitude* crossing, not a longitude target -- SEFLG_SIDEREAL
# only changes the longitude reference frame, so it is dropped here: it would be a redundant
# combination, not a new code path, for a search that never reads ecliptic longitude at all.
$MoonCrossNodeFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';   Flag = 0 }
    [pscustomobject]@{ Name = 'TRUEPOS'; Flag = $SEFLG_TRUEPOS }
    [pscustomobject]@{ Name = 'NONUT';   Flag = $SEFLG_NONUT }
)
# Four start dates (no target longitude to spread across instead, unlike the two crossing
# functions above), spanning the same Moshier-valid window.
$MoonCrossNodeTjd = @(1200000.0, 1500000.0, 1800000.0, 2400000.0)

# swe_helio_cross(_ut) rejects SE_SUN, SE_MOON, both lunar nodes and both lunar apogees, and the
# two interpolated apogee/perigee bodies (external/swisseph/sweph.c:8538-8547, a three-way `||` of
# an SE_SUN check, an SE_MOON check, and two node/apogee range checks). SE_SUN, SE_MOON and
# SE_TRUE_NODE below are one representative pick from each of those three disjuncts, so the SERR
# path is proven to fire from each of them independently rather than always hitting the same one.
# The bodies the function does accept: every classical planet Mercury..Pluto that survives the
# reject check, and SE_EARTH (a legal but unusual heliocentric target -- heliocentric Earth is
# just the antipode of geocentric Sun).
#
# SE_CHIRON is deliberately NOT in this list, even though it is the one body
# swe_helio_cross(_ut) special-cases with a hardcoded mean speed instead of the speed swe_calc
# itself returns (external/swisseph/sweph.c:8551-8552) -- exactly the kind of hardcoded-constant
# boundary worth checking bit-for-bit. It belongs in gen-grid-files.ps1's $HelioCrossIplFiles
# instead, not here: Chiron has no Moshier analytic model, so swe_calc reads seas_12.se1/
# seas_18.se1 for it regardless of the SEFLG_MOSEPH bit this whole grid otherwise guarantees means
# "touches no ephemeris data file" (see this script's own .DESCRIPTION). Putting it here was a
# category error, caught because this grid never configures an ephemeris path: the call reached
# no farther than a "file not found" error before it could ever exercise the mean-speed override
# the SE_CHIRON case exists to cover, so the 16 rows this produced were testing a path string, not
# the branch they were meant to test. gen-grid-files.ps1 opens a real ephemeris directory that
# ships seas_12.se1/seas_18.se1, which is where SE_CHIRON's mean-speed override is actually
# reachable -- see that script's own $HelioCrossIplFiles comment.
$HelioCrossRejectIpl = @(0, 1, 11)   # SE_SUN, SE_MOON, SE_TRUE_NODE
$HelioCrossValidIpl = @(2, 3, 4, 5, 6, 7, 8, 9, 14)
$HelioCrossIpl = $HelioCrossRejectIpl + $HelioCrossValidIpl
$HelioCrossX2 = @(0.0, 180.0)
$HelioCrossTjd = @(1500000.0, 2200000.0)
# Both search directions the API takes: dir >= 0 searches forward from Tjd, dir < 0 searches
# backward -- see external/swisseph/sweph.c:8554-8559.
$HelioCrossDir = @(1, -1)
$HelioCrossFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN'; Flag = 0 }
    [pscustomobject]@{ Name = 'NONUT'; Flag = $SEFLG_NONUT }
)

# ---------------------------------------------------------------------------------------
# swe_get_ayanamsa / swe_get_ayanamsa_ex / swe_get_ayanamsa_ex_ut grid values -- direct coverage
# of the ayanamsa machinery itself, closing the gap the rest of this grid only ever exercised
# indirectly (a SEFLG_SIDEREAL swe_calc/crossing row proves the ayanamsa was applied to something,
# never what value it actually was). Four Jds, not $CalcJds' twelve: mirrors
# Tools/BaselineMatrix/Ayanamsa.cs's own Jds count (8, here 4 -- half, since this sweep is also
# crossed with $SidModeSweepCount modes where Ayanamsa.cs sweeps the same 47 but does not also
# carry this grid's SE_SIDM_USER sub-sweep on top), keeping the sid_mode x date cross product
# bounded rather than reusing the full twelve-point Moshier-window spread every other swe_calc row
# in this grid already uses.
# ---------------------------------------------------------------------------------------

$AyanamsaJds = Get-JdSpread -Count 4 -Lo 1000000 -Hi 2600000

# Matches Tools/BaselineMatrix/Ayanamsa.cs's own ExIflagCombos: swe_get_ayanamsa_ex(_ut) takes an
# iflag (unlike plain swe_get_ayanamsa/_ut, which do not), and SEFLG_NONUT is the one bit whose
# effect on the ayanamsa value itself is worth freezing on its own. Flag here is the caller-chosen
# bit only; SEFLG_MOSEPH is OR-ed in separately at each of this grid's two AYANAMSA_EX/
# AYANAMSA_EX_UT build-row loops below, not baked in here, matching how $FlagCombos above keeps
# SEFLG_MOSEPH out of its own Flag field too.
#
# Read directly off the C body (not assumed from the header declaration): swi_get_ayanamsa_ex's
# own guard (sweph.c:3031-3045) is not what leaks the environment into these rows.
# get_builtin_star (sweph.c:6750-6803) hardcodes the star record for exactly the twelve sid_modes
# that guard names, and swe_fixstar (sweph.c:7896-7953) -- the function swi_get_ayanamsa_ex's own
# star/galactic sid_mode branches actually call (e.g. sweph.c:3051 for SE_SIDM_TRUE_CITRA), not
# swe_fixstar2 -- consults it BEFORE falling through to swi_fixstar_load_record's sefstars.txt
# path (sweph.c:7927-7937) -- so sefstars.txt is never the file that decides the ayanamsa value
# here. What actually leaks the environment: swe_fixstar's own position calc,
# swi_fixstar_calc_from_record (sweph.c:7613), calls main_planet_bary for Earth's barycentric
# position (sweph.c:7711-7714, needed for parallax/light-deflection/aberration). Under the default
# SEFLG_SWIEPH (i.e. without SEFLG_MOSEPH forced), that reaches get_new_segment's
# swi_fopen(ifno, s, swed.ephepath, serr) (sweph.c:2192) -- the same file-open and "not found in
# PATH" serr mechanism (swi_fopen, sweph.c:2363-2404) that produces the DIR_GLUE-sensitive
# messages docs/known-issues.md's SE_EPHE_PATH section measures, not a lazy re-init. swed.ephepath
# at that point is whatever the driver's own per-row swe_set_ephe_path call already resolved it
# to, honoring SE_EPHE_PATH's priority over the
# path argument (sweph.c:1327-1330) -- the same priority rule, reached through a file-open path
# rather than a fresh call to swe_set_ephe_path itself. (swi_fixstar_calc_from_record has its own
# !(epheflag & SEFLG_MOSEPH) guard too, sweph.c:7633-7635, and its message correctly names
# "swe_fixstar() or swe_fixstar_ut()" -- the function actually running. swe_fixstar2's parallel
# guard, in fixstar_calc_from_struct at sweph.c:6407-6427, carries the identical message even
# though it is reached from swe_fixstar2, not swe_fixstar -- so citing that guard against the real
# path would have pointed at a message that misnames its own caller; the real path's guard does
# not have that problem.) A column-level diff of the analytic dump with SE_EPHE_PATH set against
# unset (value columns and retc, not just row count) found only err-column movement on these rows,
# never a value or retc change. OR-ing SEFLG_MOSEPH into these two funcs' iflag closes that gap
# directly, by routing main_planet_bary straight to its Moshier branch (sweph.c:1735-1737), which
# never reads swed.ephepath or opens a file at all -- rather than adding separate file-backed
# coverage for a file dependency that get_builtin_star already shows does not exist for these
# twelve sid_modes.
$AyanamsaExIflagCombos = @(
    [pscustomobject]@{ Name = '0';     Flag = 0 }
    [pscustomobject]@{ Name = 'NONUT'; Flag = $SEFLG_NONUT }
)

# Three t0/ayan_t0 pairs for SE_SIDM_USER -- matches Tools/BaselineMatrix/Ayanamsa.cs's own
# UserModeParams exactly (a J2000.0 epoch with a zero ayanamsa, a B1950-ish epoch with a nonzero
# positive ayanamsa, and a pre-Gregorian epoch with a negative ayanamsa), so both the baseline and
# this oracle pin the same three points in SE_SIDM_USER's (t0, ayan_t0) input space.
$AyanamsaUserParams = @(
    [pscustomobject]@{ T0 = 2451545.0; AyanT0 = 0.0 }
    [pscustomobject]@{ T0 = 2415020.0; AyanT0 = 24.0 }
    [pscustomobject]@{ T0 = 2299160.5; AyanT0 = -5.5 }
)

# ---------------------------------------------------------------------------------------
# swe_houses_ex grid values. Same 25-letter house-system list as HOUSES/HOUSES_ARMC above (see
# $HouseLetters' own comment for why 'J' is excluded -- swe_houses_ex shares the exact same
# per-system switch in swehouse.c that HOUSES/HOUSES_ARMC already avoid 'J' for). Smaller than
# HOUSES' own geolat/geolon/tjd cross product on purpose: iflag is a new dimension HOUSES does not
# have, and this grid's row budget goes toward sweeping iflag (PLAIN/SIDEREAL/RADIANS) rather than
# re-covering the geolat/geolon/tjd spread HOUSES/HOUSES_ARMC already prove.
# ---------------------------------------------------------------------------------------

# Two polar-circle extremes (one per hemisphere) plus the equator -- the degenerate-branch cases
# that earn HOUSES its own keep (see that section's own comment on eps = 0/niter_max), reused here
# rather than HOUSES' full eleven-point spread.
$HousesExGeoLats = @(-80, -66, 0, 66, 80)
$HousesExGeoLons = @(-118.24, 0.0)
$HousesExJds = @($HouseJds[0], $HouseJds[-1])
$HousesExFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;               NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; NeedsSid = $true }
    [pscustomobject]@{ Name = 'RADIANS';  Flag = $SEFLG_RADIANS;  NeedsSid = $false }
)

# ---------------------------------------------------------------------------------------
# swe_get_ayanamsa_ut grid values -- identical sweep to AYANAMSA above (New-AyanamsaRow), since
# swe_get_ayanamsa_ut takes the exact same (tjd, sid_mode) inputs plain swe_get_ayanamsa does.
# Reuses $AyanamsaJds/$AyanamsaUserParams rather than a second copy of the same numbers.
# ---------------------------------------------------------------------------------------

# ---------------------------------------------------------------------------------------
# swe_sidtime grid values. Zero coverage anywhere in this repo before this addition. Pure
# arithmetic with no conditional branch of its own (swephlib.c:3580-3592: one straight-line
# computation from swi_epsiln/swi_nutation/swe_sidtime0), so this is a modest numeric-value
# sample, not a sweep across code paths the way the sid_mode/hsys dimensions above are -- twelve
# points across the same Moshier-window spread the rest of this grid uses.
# ---------------------------------------------------------------------------------------
$SidtimeJds = Get-JdSpread -Count 12 -Lo 1000000 -Hi 2600000

# ---------------------------------------------------------------------------------------
# swe_azalt grid values. calc_flag crosses SE_ECL2HOR (which additionally calls swe_calc for
# SE_ECL_NUT and swe_cotrans before the shared horizon-coordinate arithmetic) against SE_EQU2HOR
# (which skips straight to it) -- swecl.c:2804-2808. AzAltPressScenarios crosses the atpress == 0
# pressure-estimate branch (swecl.c:2819-2822), forced by pairing it with a non-zero height, against
# an explicitly given atpress with a zero height, so both branches are reached on purpose rather
# than by accident of which row happens to carry which height. AzAltGeoLats crosses an ordinary
# latitude against a near-pole one, since geopos[1] feeds the cotrans call that turns the
# hour-angle-relative x[] into azimuth/altitude (swecl.c:2814). Attemp has no branch of its own
# (swe_refrac_extended folds it into one arithmetic expression regardless of value), so it stays
# fixed.
# ---------------------------------------------------------------------------------------
$AzAltCalcFlagCombos = @(
    [pscustomobject]@{ Name = 'ECL2HOR'; Flag = $SE_ECL2HOR }
    [pscustomobject]@{ Name = 'EQU2HOR'; Flag = $SE_EQU2HOR }
)
$AzAltPressScenarios = @(
    [pscustomobject]@{ Name = 'ESTIMATED'; AtPress = 0.0;     Height = 100.0 }
    [pscustomobject]@{ Name = 'GIVEN';     AtPress = 1013.25; Height = 0.0 }
)
$AzAltGeoLon = -118.24
$AzAltGeoLats = @(34.05, 89.0)
$AzAltAttemp = 15.0
$AzAltXinPoints = @(
    [pscustomobject]@{ Name = 'ZERO'; X0 = 0.0;   X1 = 0.0 }
    [pscustomobject]@{ Name = 'MID';  X0 = 90.0;  X1 = 45.0 }
    [pscustomobject]@{ Name = 'NEG';  X0 = 270.0; X1 = -30.0 }
)
$AzAltTjd = @(1500000.0, 2400000.0)

# ---------------------------------------------------------------------------------------
# swe_house_name grid values -- trivial, but zero coverage before this addition. Every letter
# swehouse.c's switch actually cases on (25 -- see swe_house_name's own header comment for the
# full list, which INCLUDES 'J' unlike $HouseLetters above: swe_house_name is a pure lookup that
# both the C and the port agree on for 'J', even though neither side's actual house-cusp
# *computation* implements that system yet -- see docs/known-issues.md's "What the oracle grids
# do not cover in the house code" for that distinction), plus 'P', which is deliberately NOT one
# of the switch's case labels and therefore exercises the default ("Placidus") branch.
# ---------------------------------------------------------------------------------------
$HouseNameLetters = @(
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'i', 'J', 'K', 'L', 'M', 'N',
    'O', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y',
    'P'  # not a case label -- falls through to the default ("Placidus") branch
)

# ---------------------------------------------------------------------------------------
# swe_nod_aps_ut grid values. NodApsAcceptedIpl mirrors $HelioCrossValidIpl's own reasoning below
# (Sun..Pluto, Earth -- every body with a Moshier model, so a forced-SEFLG_MOSEPH row does not
# fail on a missing ephemeris file before it can exercise nod_aps's own logic at all).
# NodApsRejectedIpl is one representative pick from each disjunct of swe_nod_aps's own reject
# check (swecl.c:5126-5129): the four named lunar-node/apogee bodies, ipl < 0, and the
# SE_NPLANETS..SE_AST_OFFSET range (its own two ends plus one midpoint). NodApsMethods sweeps
# every distinct code path method's own bitmask reaches: mean (0 and SE_NODBIT_MEAN, which take
# the same branch for Sun..Neptune/Earth), SE_NODBIT_OSCU, SE_NODBIT_OSCU_BAR (barycentric,
# reached only past Jupiter -- x[2] > 6 at swecl.c:5245), and each of those OR-ed with
# SE_NODBIT_FOPOINT (swecl.c:5099,5114).
# ---------------------------------------------------------------------------------------
$NodApsAcceptedIpl = @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, $SE_EARTH)
$NodApsRejectedIpl = @($SE_MEAN_NODE, $SE_TRUE_NODE, $SE_MEAN_APOG, $SE_OSCU_APOG, -1, $SE_NPLANETS, $SE_AST_OFFSET)
$NodApsMethods = @(
    0,
    $SE_NODBIT_MEAN,
    $SE_NODBIT_OSCU,
    $SE_NODBIT_OSCU_BAR,
    ($SE_NODBIT_FOPOINT),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_MEAN),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_OSCU),
    ($SE_NODBIT_FOPOINT -bor $SE_NODBIT_OSCU_BAR)
)
$NodApsTjd = @(1200000.0, 2400000.0)
$NodApsRejectedTjd = 1200000.0

# ---------------------------------------------------------------------------------------
# Build rows
# ---------------------------------------------------------------------------------------

$rows = [System.Collections.Generic.List[string]]::new()
$calcCount = 0
$calcUtCount = 0
$housesCount = 0
$housesArmcCount = 0
$housesArmcEx2Count = 0
$solCrossCount = 0
$solCrossUtCount = 0
$moonCrossCount = 0
$moonCrossUtCount = 0
$moonCrossNodeCount = 0
$moonCrossNodeUtCount = 0
$helioCrossCount = 0
$helioCrossUtCount = 0
$ayanamsaCount = 0
$ayanamsaExCount = 0
$ayanamsaExUtCount = 0
$housesExCount = 0
$housesEx2Count = 0
$ayanamsaUtCount = 0
$sidtimeCount = 0
$azaltCount = 0
$houseNameCount = 0
$nodApsUtCount = 0

foreach ($ipl in $Bodies) {
    foreach ($tjd in $CalcJds) {
        foreach ($combo in $FlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            $geolon  = if ($combo.NeedsTopo) { $TopoGeoLon } else { $null }
            $geolat  = if ($combo.NeedsTopo) { $TopoGeoLat } else { $null }
            $height  = if ($combo.NeedsTopo) { $TopoHeight } else { $null }
            # Cycled, not pinned to $SE_SIDM_LAHIRI -- see Get-NextSidMode's own comment.
            $sidMode = if ($combo.NeedsSid)  { Get-NextSidMode } else { $null }

            $rows.Add((New-CalcRow -Prefix 'CALC' -Func 'CALC' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcCount++

            $rows.Add((New-CalcRow -Prefix 'CALCUT' -Func 'CALC_UT' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcUtCount++
        }
    }
}

foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HouseGeoLats) {
        foreach ($geolon in $HouseGeoLons) {
            foreach ($tjd in $HouseJds) {
                $rows.Add((New-HousesRow -Hsys $hsys -GeoLat $geolat -GeoLon $geolon -Tjd $tjd))
                $housesCount++
            }
        }
    }
}

foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HouseGeoLats) {
        foreach ($eps in $HouseEps) {
            foreach ($armc in $HouseArmcs) {
                $rows.Add((New-HousesArmcRow -Hsys $hsys -GeoLat $geolat -Eps $eps -Armc $armc))
                $housesArmcCount++
            }
        }
    }
}

# HOUSES_ARMC_EX2: a smaller cross-section of the sweep above ($HousesExGeoLats' 5 latitudes, not
# $HouseGeoLats' 11) -- HOUSES_ARMC already proves the geometry in full; this only needs to prove
# the _ex2 speed outputs are wired up across every hsys letter, including 'I'/'i'.
foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HousesExGeoLats) {
        foreach ($eps in $HouseEps) {
            foreach ($armc in $HouseArmcs) {
                $rows.Add((New-HousesArmcEx2Row -Hsys $hsys -GeoLat $geolat -Eps $eps -Armc $armc))
                $housesArmcEx2Count++
            }
        }
    }
}

foreach ($x2 in $CrossX2) {
    foreach ($tjd in $CrossTjd) {
        foreach ($combo in $SolCrossFlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            # Cycled, not pinned to $SE_SIDM_LAHIRI -- see Get-NextSidMode's own comment. Computed
            # once per combo occurrence and reused for both the ET and UT row below, matching how
            # the prior fixed $combo.SidMode was shared between them.
            $sidMode = if ($combo.NeedsSid) { Get-NextSidMode } else { $null }

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSS' -Func 'SOLCROSS' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
            $solCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSSUT' -Func 'SOLCROSS_UT' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
            $solCrossUtCount++
        }
        foreach ($combo in $MoonCrossFlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            $sidMode = if ($combo.NeedsSid) { Get-NextSidMode } else { $null }

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSS' -Func 'MOONCROSS' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
            $moonCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSSUT' -Func 'MOONCROSS_UT' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
            $moonCrossUtCount++
        }
    }
}

foreach ($tjd in $MoonCrossNodeTjd) {
    foreach ($combo in $MoonCrossNodeFlagCombos) {
        $iflag = $SEFLG_MOSEPH -bor $combo.Flag

        $rows.Add((New-MoonCrossNodeRow -Prefix 'MOONCROSSNODE' -Func 'MOONCROSS_NODE' -Tjd $tjd `
            -FlagName $combo.Name -IFlag $iflag))
        $moonCrossNodeCount++

        $rows.Add((New-MoonCrossNodeRow -Prefix 'MOONCROSSNODEUT' -Func 'MOONCROSS_NODE_UT' -Tjd $tjd `
            -FlagName $combo.Name -IFlag $iflag))
        $moonCrossNodeUtCount++
    }
}

foreach ($ipl in $HelioCrossIpl) {
    foreach ($x2 in $HelioCrossX2) {
        foreach ($tjd in $HelioCrossTjd) {
            foreach ($dir in $HelioCrossDir) {
                foreach ($combo in $HelioCrossFlagCombos) {
                    $iflag = $SEFLG_MOSEPH -bor $combo.Flag

                    $rows.Add((New-HelioCrossRow -Prefix 'HELIOCROSS' -Func 'HELIO_CROSS' -Ipl $ipl -X2Cross $x2 -Tjd $tjd `
                        -FlagName $combo.Name -IFlag $iflag -Dir $dir))
                    $helioCrossCount++

                    $rows.Add((New-HelioCrossRow -Prefix 'HELIOCROSSUT' -Func 'HELIO_CROSS_UT' -Ipl $ipl -X2Cross $x2 -Tjd $tjd `
                        -FlagName $combo.Name -IFlag $iflag -Dir $dir))
                    $helioCrossUtCount++
                }
            }
        }
    }
}

foreach ($sidMode in 0..($SidModeSweepCount - 1)) {
    foreach ($tjd in $AyanamsaJds) {
        $rows.Add((New-AyanamsaRow -SidMode $sidMode -Tjd $tjd -T0 0.0 -AyanT0 0.0 -IsUser:$false))
        $ayanamsaCount++

        foreach ($combo in $AyanamsaExIflagCombos) {
            # SEFLG_MOSEPH OR-ed in explicitly -- see $AyanamsaExIflagCombos' own comment for why:
            # without it, swi_get_ayanamsa_ex's twelve star/galactic-based sid_modes
            # (sweph.c:3031-3045) call swe_fixstar (sweph.c:7896-7953), whose own position calc
            # swi_fixstar_calc_from_record (sweph.c:7613) reaches get_new_segment's
            # swi_fopen(..., swed.ephepath, serr) (sweph.c:2192) via main_planet_bary under the
            # default SEFLG_SWIEPH -- letting SE_EPHE_PATH (already resolved into swed.ephepath by
            # the driver's own per-row swe_set_ephe_path call, sweph.c:1327-1330's priority) leak
            # into this row's serr text even though grid-analytic.tsv's own premise is that no row
            # here touches a file or the environment at all. See this variable's own comment above
            # for the full call chain and why the star position itself never depends on
            # sefstars.txt.
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            $rows.Add((New-AyanamsaExRow -Func 'AYANAMSA_EX' -SidMode $sidMode -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -T0 0.0 -AyanT0 0.0 -IsUser:$false))
            $ayanamsaExCount++

            $rows.Add((New-AyanamsaExRow -Func 'AYANAMSA_EX_UT' -SidMode $sidMode -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -T0 0.0 -AyanT0 0.0 -IsUser:$false))
            $ayanamsaExUtCount++
        }
    }
}

foreach ($p in $AyanamsaUserParams) {
    foreach ($tjd in $AyanamsaJds) {
        $rows.Add((New-AyanamsaRow -SidMode $SE_SIDM_USER -Tjd $tjd -T0 $p.T0 -AyanT0 $p.AyanT0 -IsUser:$true))
        $ayanamsaCount++

        foreach ($combo in $AyanamsaExIflagCombos) {
            # SEFLG_MOSEPH OR-ed in -- see the identical predefined-mode loop above for why.
            # SE_SIDM_USER is never one of swi_get_ayanamsa_ex's twelve star/galactic sid_modes, so
            # this row was never exposed to the same env-dependence, but forcing it here too keeps
            # every AYANAMSA_EX/AYANAMSA_EX_UT row in this grid on one uniform iflag convention.
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            $rows.Add((New-AyanamsaExRow -Func 'AYANAMSA_EX' -SidMode $SE_SIDM_USER -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -T0 $p.T0 -AyanT0 $p.AyanT0 -IsUser:$true))
            $ayanamsaExCount++

            $rows.Add((New-AyanamsaExRow -Func 'AYANAMSA_EX_UT' -SidMode $SE_SIDM_USER -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -T0 $p.T0 -AyanT0 $p.AyanT0 -IsUser:$true))
            $ayanamsaExUtCount++
        }
    }
}

foreach ($hsys in $HouseLetters) {
    foreach ($geolat in $HousesExGeoLats) {
        foreach ($geolon in $HousesExGeoLons) {
            foreach ($tjd in $HousesExJds) {
                foreach ($combo in $HousesExFlagCombos) {
                    $iflag = $combo.Flag
                    $sidMode = if ($combo.NeedsSid) { Get-NextSidMode } else { $null }

                    $rows.Add((New-HousesExRow -Prefix 'HOUSESEX' -Func 'HOUSES_EX' -Hsys $hsys -GeoLat $geolat -GeoLon $geolon -Tjd $tjd `
                        -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
                    $housesExCount++

                    $rows.Add((New-HousesExRow -Prefix 'HOUSESEX2' -Func 'HOUSES_EX2' -Hsys $hsys -GeoLat $geolat -GeoLon $geolon -Tjd $tjd `
                        -FlagName $combo.Name -IFlag $iflag -SidMode $sidMode))
                    $housesEx2Count++
                }
            }
        }
    }
}

foreach ($sidMode in 0..($SidModeSweepCount - 1)) {
    foreach ($tjd in $AyanamsaJds) {
        $rows.Add((New-AyanamsaUtRow -SidMode $sidMode -Tjd $tjd -T0 0.0 -AyanT0 0.0 -IsUser:$false))
        $ayanamsaUtCount++
    }
}
foreach ($p in $AyanamsaUserParams) {
    foreach ($tjd in $AyanamsaJds) {
        $rows.Add((New-AyanamsaUtRow -SidMode $SE_SIDM_USER -Tjd $tjd -T0 $p.T0 -AyanT0 $p.AyanT0 -IsUser:$true))
        $ayanamsaUtCount++
    }
}

foreach ($tjd in $SidtimeJds) {
    $rows.Add((New-SidtimeRow -Tjd $tjd))
    $sidtimeCount++
}

foreach ($calcFlagCombo in $AzAltCalcFlagCombos) {
    foreach ($press in $AzAltPressScenarios) {
        foreach ($geolat in $AzAltGeoLats) {
            foreach ($xin in $AzAltXinPoints) {
                foreach ($tjd in $AzAltTjd) {
                    $rows.Add((New-AzAltRow -CalcFlagName $calcFlagCombo.Name -CalcFlag $calcFlagCombo.Flag `
                        -GeoLon $AzAltGeoLon -GeoLat $geolat -Height $press.Height `
                        -PressName $press.Name -AtPress $press.AtPress -Attemp $AzAltAttemp `
                        -XinName $xin.Name -Xin0 $xin.X0 -Xin1 $xin.X1 -Tjd $tjd))
                    $azaltCount++
                }
            }
        }
    }
}

foreach ($hsys in $HouseNameLetters) {
    $rows.Add((New-HouseNameRow -Hsys $hsys))
    $houseNameCount++
}

foreach ($ipl in $NodApsAcceptedIpl) {
    foreach ($method in $NodApsMethods) {
        foreach ($tjd in $NodApsTjd) {
            $rows.Add((New-NodApsUtRow -Ipl $ipl -Tjd $tjd -IFlag $SEFLG_MOSEPH -Method $method))
            $nodApsUtCount++
        }
    }
}
foreach ($ipl in $NodApsRejectedIpl) {
    $rows.Add((New-NodApsUtRow -Ipl $ipl -Tjd $NodApsRejectedTjd -IFlag $SEFLG_MOSEPH -Method 0))
    $nodApsUtCount++
}

$totalRows = $rows.Count
$expectedTotal = $calcCount + $calcUtCount + $housesCount + $housesArmcCount + $housesArmcEx2Count +
    $solCrossCount + $solCrossUtCount + $moonCrossCount + $moonCrossUtCount +
    $moonCrossNodeCount + $moonCrossNodeUtCount + $helioCrossCount + $helioCrossUtCount +
    $ayanamsaCount + $ayanamsaExCount + $ayanamsaExUtCount +
    $housesExCount + $housesEx2Count + $ayanamsaUtCount + $sidtimeCount + $azaltCount + $houseNameCount + $nodApsUtCount
if ($totalRows -ne $expectedTotal) {
    throw 'Row count bookkeeping is inconsistent -- this is a bug in this script, not a data problem.'
}

# ---------------------------------------------------------------------------------------
# Header block -- the substance of the format lives here, in the file itself, not only in
# this script's own comment-based help above. See this script's .DESCRIPTION for the "why
# is the grid separate from the two drivers" rationale in full.
# ---------------------------------------------------------------------------------------

$headerLines = @(
    '# grid-analytic.tsv -- committed input vectors for the bit-exact C-vs-C# comparison harness'
    '# (stage 1). Regenerated by Tools/OracleGrid/gen-grid-analytic.ps1 -- never hand-edit this'
    '# file; a change here has to come from that script, committed together with its regenerated'
    '# output.'
    '#'
    '# WHY THE GRID IS A SEPARATE FILE FROM THE TWO DRIVERS THAT REPLAY IT'
    '#'
    '# Tools/BaselineMatrix already sweeps a wide set of Swiss Ephemeris calls, but each of its'
    '# area generators builds its own inputs and calls the .NET API in the same method. A C driver'
    '# written against that shape would have to reimplement the same input-building logic in C,'
    '# and the two implementations would drift apart from each other silently -- exactly the'
    '# failure this harness exists to catch. Neither Tools/CReference/sedump.c nor'
    '# Tools/OracleDump/Program.cs builds a grid of its own; both are interpreters over these rows.'
    '# Extending coverage later means adding rows here, not editing two programs in step.'
    '#'
    '# COVERAGE'
    '#'
    '# swe_calc / swe_calc_ut: bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating apogee,'
    '# Earth), crossed with a spread of Julian days and twelve iflag combinations, every one'
    '# carrying SEFLG_MOSEPH explicitly so the result depends on no ephemeris data file and is'
    '# reproducible on any machine (a file-backed grid is a later, separate stage). The TOPOCTR'
    '# combination adds a fixed geoposition and the SIDEREAL combination a fixed sid mode, each'
    '# applied via its own swe_set_* call before that row runs.'
    '#'
    "# swe_houses / swe_houses_armc: every house-system letter this port implements"
    '# (SwissEphNet/CPort/SweHouse.cs; upstream 2.10.03 adds ''J'', which this port does not'
    '# implement yet, so ''J'' is deliberately absent here), crossed with geographic latitudes --'
    '# including polar and near-polar extremes, where Placidus and Koch degenerate -- longitudes,'
    '# Julian days and ARMC values.'
    '#'
    '# swe_solcross / swe_solcross_ut / swe_mooncross / swe_mooncross_ut / swe_mooncross_node /'
    '# swe_mooncross_node_ut / swe_helio_cross / swe_helio_cross_ut: the eight crossing functions,'
    '# all carrying SEFLG_MOSEPH like the rest of this grid. swe_solcross and swe_mooncross are'
    '# crossed with a spread of target longitudes (including both 0.0 and 360.0, the two ends of'
    '# the wraparound swe_degnorm folds together), start dates and (most of) the flag combinations'
    '# each function''s own doc comment names -- SEFLG_HELCTR is deliberately left out of'
    '# swe_solcross''s own list, because it drives swe_solcross into an unbounded loop inside libswe'
    '# itself (see Tools/OracleGrid/gen-grid-analytic.ps1''s $SolCrossFlagCombos for the mechanism).'
    '# swe_mooncross_node takes no target longitude (it finds a'
    '# zero-latitude node crossing, not a longitude crossing), so it is crossed with start dates'
    '# and flags only. swe_helio_cross is crossed with target longitudes, start dates, both search'
    '# directions the API takes, and a body list that deliberately includes SE_SUN/SE_MOON/'
    '# SE_TRUE_NODE (one representative body the function rejects from each disjunct of its own'
    '# reject check) alongside the bodies it accepts under SEFLG_MOSEPH. SE_CHIRON, the one body'
    '# the function overrides with a hardcoded mean speed instead of swe_calc''s own, is NOT in'
    '# this grid: it has no Moshier model, so it needs a real data file regardless of SEFLG_MOSEPH'
    '# -- covered instead in grid-files.tsv, where that file is actually present.'
    '#'
    '# A FRESH LIBRARY INSTANCE PER ROW, IN BOTH DRIVERS'
    '#'
    '# swe_houses_armc carries a hidden saved_sundec field (see Tools/BaselineGen/Program.cs and'
    '# SwissEphNet/CPort/SweHouse.cs) that changes hsys ''I''/''i'' results depending on what a PRIOR'
    '# call on the same instance computed. Reusing one instance across rows would make the two'
    '# drivers disagree for a reason that has nothing to do with the port -- both reset all'
    '# library state before every row (see each driver''s own header comment for how).'
    '#'
    '# COLUMNS (tab-separated, one call per line, LF line endings, empty string where a column'
    '# does not apply to that row''s func)'
    '#'
    '#   case_id    stable, unique, pipe-delimited id; ordinal comparison sorts it deterministically'
    '#   func       CALC | CALC_UT | HOUSES | HOUSES_ARMC | HOUSES_ARMC_EX2 | SOLCROSS | SOLCROSS_UT |'
    '#              MOONCROSS | MOONCROSS_UT | MOONCROSS_NODE | MOONCROSS_NODE_UT | HELIO_CROSS |'
    '#              HELIO_CROSS_UT | HOUSES_EX | HOUSES_EX2'
    '#   ipl        body number                                        [CALC, CALC_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   tjd        Julian day (ET for CALC/SOLCROSS/MOONCROSS/MOONCROSS_NODE/HELIO_CROSS; UT for'
    '#              the corresponding _UT funcs and HOUSES)'
    '#              [CALC, CALC_UT, HOUSES, SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT,'
    '#              MOONCROSS_NODE, MOONCROSS_NODE_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   iflag      swe_calc/crossing-func iflag, with SEFLG_MOSEPH already OR-ed in'
    '#              [CALC, CALC_UT, SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT, MOONCROSS_NODE,'
    '#              MOONCROSS_NODE_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   hsys       house-system letter          [HOUSES, HOUSES_ARMC, HOUSES_ARMC_EX2, HOUSES_EX, HOUSES_EX2]'
    '#   geolon     geographic longitude, degrees east    [HOUSES, HOUSES_EX, HOUSES_EX2; CALC/CALC_UT topo rows]'
    '#   geolat     geographic latitude, degrees north    [HOUSES, HOUSES_ARMC, HOUSES_ARMC_EX2, HOUSES_EX,'
    '#              HOUSES_EX2; CALC/CALC_UT topo rows]'
    '#   height     observer height above sea level, metres            [CALC/CALC_UT topo rows only]'
    '#   armc       ARMC, degrees                                      [HOUSES_ARMC, HOUSES_ARMC_EX2]'
    '#   eps        obliquity of the ecliptic, degrees                 [HOUSES_ARMC, HOUSES_ARMC_EX2]'
    '#   sid_mode   swe_set_sid_mode mode, applied before the row runs [CALC/CALC_UT rows whose iflag'
    '#              carries SEFLG_SIDEREAL; SOLCROSS/MOONCROSS rows whose iflag carries it too]'
    '#   x2cross    target ecliptic longitude to cross, degrees        [SOLCROSS, SOLCROSS_UT, MOONCROSS,'
    '#              MOONCROSS_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   dir        swe_helio_cross(_ut) search direction: >= 0 forward, < 0 backward'
    '#              [HELIO_CROSS, HELIO_CROSS_UT]'
    '#   t0         SE_SIDM_USER reference epoch, TT (swe_set_sid_mode''s own t0 parameter);'
    '#              empty means 0.0, which is also what an absent sid_mode implies (no'
    '#              swe_set_sid_mode call at all) [CALC/CALC_UT/SOLCROSS/SOLCROSS_UT/MOONCROSS/'
    '#              MOONCROSS_UT/AYANAMSA/AYANAMSA_EX/AYANAMSA_EX_UT rows whose sid_mode is'
    '#              SE_SIDM_USER]'
    '#   ayan_t0    SE_SIDM_USER ayanamsa at t0, degrees (swe_set_sid_mode''s own ayan_t0'
    '#              parameter); same emptiness convention as t0 [same rows as t0]'
    '#'
    '# x2cross and dir are appended after sid_mode, and t0/ayan_t0 after those, rather than'
    '# interleaved among the original twelve columns, so every column HOUSES/HOUSES_ARMC/CALC/'
    '# CALC_UT rows already used keeps the same index it always had -- additive, not a renumbering,'
    '# the same choice x2cross/dir themselves made when they were added.'
    '#'
    '# AYANAMSA / AYANAMSA_EX / AYANAMSA_EX_UT: swe_get_ayanamsa/_ex/_ex_ut direct coverage. tjd is'
    '# ET for AYANAMSA/AYANAMSA_EX, UT for AYANAMSA_EX_UT; iflag applies to the _EX variants only'
    '# (plain swe_get_ayanamsa takes no iflag); sid_mode is always present on these rows (every'
    '# predefined mode 0..46, plus SE_SIDM_USER with t0/ayan_t0 set). AYANAMSA has no serr output'
    '# parameter -- its err column stays empty, the same convention HOUSES/HOUSES_ARMC already use.'
    '#'
    '# A row with a non-empty geolon/geolat/height needs swe_set_topo called first; a row with a'
    '# non-empty sid_mode needs swe_set_sid_mode called first -- both are per-row setup on that'
    '# row''s fresh library instance.'
    '#'
    '# HOUSES_EX / AYANAMSA_UT / SIDTIME / AZALT / HOUSE_NAME / NOD_APS_UT: six entry points an'
    '# astrology-program consumer of this library actually calls that no grid measured before this'
    '# addition (Celestium is one example such consumer, named only as that -- nothing about its'
    '# own source is referenced here). HOUSES_EX is swe_houses with an iflag (SEFLG_SIDEREAL,'
    '# SEFLG_RADIANS), the sidereal house path -- same 25-letter hsys set as HOUSES/HOUSES_ARMC'
    '# ('' J'' excluded for the same reason). AYANAMSA_UT is the UT sibling of AYANAMSA. SIDTIME'
    '# (swe_sidtime) and AZALT (swe_azalt) had zero coverage anywhere in this repository before'
    '# this addition; AZALT reuses the geolon/geolat/height columns for its geopos parameter and'
    '# gets five new trailing columns (method/calc_flag/atpress/attemp/xin0/xin1 -- method is'
    '# NOD_APS_UT''s own, the other four are AZALT''s; xin[2] has no column at all, since'
    '# swe_azalt''s own body never reads it). HOUSE_NAME (swe_house_name) is a pure lookup, swept'
    '# across a wider hsys set than HOUSES/HOUSES_ARMC on purpose: it INCLUDES ''J'' (both the C and'
    '# the port agree on its name even though neither implements that house system''s cusp'
    '# computation yet) and ''P'' (not a case label, so it exercises the default/Placidus branch).'
    '# NOD_APS_UT (swe_nod_aps_ut) reuses the ipl column and adds method (its own bitmask'
    '# parameter); both accepted and rejected ipl values are covered, to exercise the serr path as'
    '# well as the real one.'
    '#'
    '# HOUSES_EX2 / HOUSES_ARMC_EX2: new in 2.10.03 (absent from'
    '# external/pyswisseph-2.08/swephexp.h entirely). swe_houses/swe_houses_ex already reach both'
    '# on every HOUSES/HOUSES_EX row (swehouse.c:173,186 delegate to them), but always with'
    '# cusp_speed/ascmc_speed/serr hardcoded NULL, switching the 2.10 speed feature off'
    '# (swehouse.c:642-647). These two funcs are called directly, with real cusp_speed/ascmc_speed'
    '# arrays, emitting cusp[0..36]+ascmc[0..9]+cusp_speed[0..36]+ascmc_speed[0..9] (94 doubles) plus'
    '# a real serr. Same input columns as HOUSES_EX/HOUSES_ARMC respectively. Guarded behind'
    '# SWISSEPH_HAS_HOUSES_EX2 in both drivers, the same pattern SWISSEPH_HAS_CROSSING already uses.'
    '#'
    '# AYANAMSA_EX/AYANAMSA_EX_UT now OR SEFLG_MOSEPH into every row''s iflag, closing a gap where'
    '# twelve sid_modes (swi_get_ayanamsa_ex''s own guard, sweph.c:3031-3045) call swe_fixstar'
    '# (sweph.c:7896-7953), whose swi_fixstar_calc_from_record (sweph.c:7613) opens a planet file'
    '# via swed.ephepath (sweph.c:2192) with no ephemeris bit forced, letting SE_EPHE_PATH leak'
    '# into the row''s err column even though this grid''s whole premise is that no row touches a'
    '# file or the environment. Measured (see $AyanamsaExIflagCombos'' own comment for the exact'
    '# row-level result and full call chain): only the err column moved; the star position itself'
    '# comes from a hardcoded built-in table (get_builtin_star, sweph.c:6750-6803), not'
    '# sefstars.txt, so the fix removes an environment dependency without changing any value.'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- both drivers assert it matches verbatim before reading any'
    '# data, so a schema change here that a driver was not updated for fails loudly instead of'
    '# silently misreading columns.'
)
$columnHeader = 'case_id' + "`t" + 'func' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag' + "`t" +
    'hsys' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'armc' + "`t" + 'eps' + "`t" + 'sid_mode' + "`t" +
    'x2cross' + "`t" + 'dir' + "`t" + 't0' + "`t" + 'ayan_t0' + "`t" +
    'method' + "`t" + 'calc_flag' + "`t" + 'atpress' + "`t" + 'attemp' + "`t" + 'xin0' + "`t" + 'xin1'

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
Write-Host "  HOUSES             $housesCount"
Write-Host "  HOUSES_ARMC        $housesArmcCount"
Write-Host "  HOUSES_ARMC_EX2    $housesArmcEx2Count"
Write-Host "  SOLCROSS           $solCrossCount"
Write-Host "  SOLCROSS_UT        $solCrossUtCount"
Write-Host "  MOONCROSS          $moonCrossCount"
Write-Host "  MOONCROSS_UT       $moonCrossUtCount"
Write-Host "  MOONCROSS_NODE     $moonCrossNodeCount"
Write-Host "  MOONCROSS_NODE_UT  $moonCrossNodeUtCount"
Write-Host "  HELIO_CROSS        $helioCrossCount"
Write-Host "  HELIO_CROSS_UT     $helioCrossUtCount"
Write-Host "  AYANAMSA           $ayanamsaCount"
Write-Host "  AYANAMSA_EX        $ayanamsaExCount"
Write-Host "  AYANAMSA_EX_UT     $ayanamsaExUtCount"
Write-Host "  HOUSES_EX          $housesExCount"
Write-Host "  HOUSES_EX2         $housesEx2Count"
Write-Host "  AYANAMSA_UT        $ayanamsaUtCount"
Write-Host "  SIDTIME            $sidtimeCount"
Write-Host "  AZALT              $azaltCount"
Write-Host "  HOUSE_NAME         $houseNameCount"
Write-Host "  NOD_APS_UT         $nodApsUtCount"
