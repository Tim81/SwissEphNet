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

    Covers two function families, chosen because between them they are roughly 90% of the
    conformance corpus (Tests/SwissEphNet.Conformance.Tests) and the bulk of the numeric porting
    work:

      swe_calc / swe_calc_ut  -- bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating
                                  apogee, Earth), crossed with a spread of Julian days (including
                                  two outside the Moshier-valid window, to exercise the ERR/serr
                                  path) and twelve iflag combinations. SEFLG_MOSEPH is OR-ed into
                                  every one of them, so every result depends on no ephemeris data
                                  file and is reproducible on any machine -- a file-backed grid is
                                  a later, separate stage. The TOPOCTR combination additionally
                                  carries a fixed geoposition and the SIDEREAL combination a fixed
                                  sid mode, each applied via its own swe_set_* call before that
                                  row's swe_calc/swe_calc_ut runs.

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
                                  alongside the bodies it accepts, including SE_CHIRON (the one
                                  body the function overrides with a hardcoded mean speed instead
                                  of swe_calc's own).

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
      dir

    x2cross and dir are appended after sid_mode, not interleaved among the original twelve
    columns, so every existing column keeps the same index it always had -- the crossing rows are
    additive, not a renumbering.

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
        $SidMode
    )
    $caseId = "$Prefix|$(FmtI $Ipl)|$(Fmt $Tjd)|$FlagName"
    $geolonField  = if ($null -eq $GeoLon)  { '' } else { Fmt ([double]$GeoLon) }
    $geolatField  = if ($null -eq $GeoLat)  { '' } else { Fmt ([double]$GeoLat) }
    $heightField  = if ($null -eq $Height)  { '' } else { Fmt ([double]$Height) }
    $sidModeField = if ($null -eq $SidMode) { '' } else { FmtI ([int]$SidMode) }
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        $geolonField, $geolatField, $heightField, '', '', $sidModeField, '', ''
    )
    return ($fields -join "`t")
}

function New-HousesRow {
    param([char] $Hsys, [double] $GeoLat, [double] $GeoLon, [double] $Tjd)
    $caseId = "HOUSES|$Hsys|$(Fmt $GeoLat)|$(Fmt $GeoLon)|$(Fmt $Tjd)"
    $fields = @(
        $caseId, 'HOUSES', '', (Fmt $Tjd), '', "$Hsys",
        (Fmt $GeoLon), (Fmt $GeoLat), '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-HousesArmcRow {
    param([char] $Hsys, [double] $GeoLat, [double] $Eps, [double] $Armc)
    $caseId = "HOUSESARMC|$Hsys|$(Fmt $GeoLat)|$(Fmt $Eps)|$(Fmt $Armc)"
    $fields = @(
        $caseId, 'HOUSES_ARMC', '', '', '', "$Hsys",
        '', (Fmt $GeoLat), '', (Fmt $Armc), (Fmt $Eps), '', '', ''
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
        '', '', '', '', '', $sidModeField, (Fmt $X2Cross), ''
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
        '', '', '', '', '', '', '', ''
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
        '', '', '', '', '', '', (Fmt $X2Cross), (FmtI $Dir)
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
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;              SidMode = $null }
    [pscustomobject]@{ Name = 'TRUEPOS';  Flag = $SEFLG_TRUEPOS; SidMode = $null }
    [pscustomobject]@{ Name = 'NONUT';    Flag = $SEFLG_NONUT;   SidMode = $null }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; SidMode = $SE_SIDM_LAHIRI }
)

# swe_mooncross/swe_mooncross_ut's own doc comments (external/swisseph/sweph.c:8380-8383,
# 8415-8423) list SEFLG_TRUEPOS, SEFLG_NONUT and SEFLG_SIDEREAL; they do not document SEFLG_HELCTR
# the way swe_solcross does (a heliocentric Moon has no defined meaning), so this list omits it
# rather than exercising a combination outside either function's documented contract.
$MoonCrossFlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';    Flag = 0;              SidMode = $null }
    [pscustomobject]@{ Name = 'TRUEPOS';  Flag = $SEFLG_TRUEPOS; SidMode = $null }
    [pscustomobject]@{ Name = 'NONUT';    Flag = $SEFLG_NONUT;   SidMode = $null }
    [pscustomobject]@{ Name = 'SIDEREAL'; Flag = $SEFLG_SIDEREAL; SidMode = $SE_SIDM_LAHIRI }
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
$HelioCrossRejectIpl = @(0, 1, 11)   # SE_SUN, SE_MOON, SE_TRUE_NODE
# The bodies the function does accept: every classical planet Mercury..Pluto that survives the
# reject check, SE_EARTH (a legal but unusual heliocentric target -- heliocentric Earth is just
# the antipode of geocentric Sun), and SE_CHIRON, which the function special-cases with a
# hardcoded mean speed instead of the speed swe_calc itself returns
# (external/swisseph/sweph.c:8551-8552) -- exactly the kind of hardcoded-constant boundary this
# task exists to check bit-for-bit.
$HelioCrossValidIpl = @(2, 3, 4, 5, 6, 7, 8, 9, 14, 15)
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
# Build rows
# ---------------------------------------------------------------------------------------

$rows = [System.Collections.Generic.List[string]]::new()
$calcCount = 0
$calcUtCount = 0
$housesCount = 0
$housesArmcCount = 0
$solCrossCount = 0
$solCrossUtCount = 0
$moonCrossCount = 0
$moonCrossUtCount = 0
$moonCrossNodeCount = 0
$moonCrossNodeUtCount = 0
$helioCrossCount = 0
$helioCrossUtCount = 0

foreach ($ipl in $Bodies) {
    foreach ($tjd in $CalcJds) {
        foreach ($combo in $FlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag
            $geolon  = if ($combo.NeedsTopo) { $TopoGeoLon } else { $null }
            $geolat  = if ($combo.NeedsTopo) { $TopoGeoLat } else { $null }
            $height  = if ($combo.NeedsTopo) { $TopoHeight } else { $null }
            $sidMode = if ($combo.NeedsSid)  { $SE_SIDM_LAHIRI } else { $null }

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

foreach ($x2 in $CrossX2) {
    foreach ($tjd in $CrossTjd) {
        foreach ($combo in $SolCrossFlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSS' -Func 'SOLCROSS' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $combo.SidMode))
            $solCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'SOLCROSSUT' -Func 'SOLCROSS_UT' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $combo.SidMode))
            $solCrossUtCount++
        }
        foreach ($combo in $MoonCrossFlagCombos) {
            $iflag = $SEFLG_MOSEPH -bor $combo.Flag

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSS' -Func 'MOONCROSS' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $combo.SidMode))
            $moonCrossCount++

            $rows.Add((New-SolarLunarCrossRow -Prefix 'MOONCROSSUT' -Func 'MOONCROSS_UT' -X2Cross $x2 -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag -SidMode $combo.SidMode))
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

$totalRows = $rows.Count
$expectedTotal = $calcCount + $calcUtCount + $housesCount + $housesArmcCount +
    $solCrossCount + $solCrossUtCount + $moonCrossCount + $moonCrossUtCount +
    $moonCrossNodeCount + $moonCrossNodeUtCount + $helioCrossCount + $helioCrossUtCount
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
    '# reject check) alongside the bodies it accepts, including SE_CHIRON (the one body the'
    '# function overrides with a hardcoded mean speed instead of swe_calc''s own).'
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
    '#   func       CALC | CALC_UT | HOUSES | HOUSES_ARMC | SOLCROSS | SOLCROSS_UT | MOONCROSS |'
    '#              MOONCROSS_UT | MOONCROSS_NODE | MOONCROSS_NODE_UT | HELIO_CROSS | HELIO_CROSS_UT'
    '#   ipl        body number                                        [CALC, CALC_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   tjd        Julian day (ET for CALC/SOLCROSS/MOONCROSS/MOONCROSS_NODE/HELIO_CROSS; UT for'
    '#              the corresponding _UT funcs and HOUSES)'
    '#              [CALC, CALC_UT, HOUSES, SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT,'
    '#              MOONCROSS_NODE, MOONCROSS_NODE_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   iflag      swe_calc/crossing-func iflag, with SEFLG_MOSEPH already OR-ed in'
    '#              [CALC, CALC_UT, SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT, MOONCROSS_NODE,'
    '#              MOONCROSS_NODE_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   hsys       house-system letter                                [HOUSES, HOUSES_ARMC]'
    '#   geolon     geographic longitude, degrees east                 [HOUSES; CALC/CALC_UT topo rows]'
    '#   geolat     geographic latitude, degrees north                 [HOUSES, HOUSES_ARMC; CALC/CALC_UT topo rows]'
    '#   height     observer height above sea level, metres            [CALC/CALC_UT topo rows only]'
    '#   armc       ARMC, degrees                                      [HOUSES_ARMC]'
    '#   eps        obliquity of the ecliptic, degrees                 [HOUSES_ARMC]'
    '#   sid_mode   swe_set_sid_mode mode, applied before the row runs [CALC/CALC_UT rows whose iflag'
    '#              carries SEFLG_SIDEREAL; SOLCROSS/MOONCROSS rows whose iflag carries it too]'
    '#   x2cross    target ecliptic longitude to cross, degrees        [SOLCROSS, SOLCROSS_UT, MOONCROSS,'
    '#              MOONCROSS_UT, HELIO_CROSS, HELIO_CROSS_UT]'
    '#   dir        swe_helio_cross(_ut) search direction: >= 0 forward, < 0 backward'
    '#              [HELIO_CROSS, HELIO_CROSS_UT]'
    '#'
    '# x2cross and dir are appended after sid_mode rather than interleaved among the original'
    '# twelve columns, so every column HOUSES/HOUSES_ARMC/CALC/CALC_UT rows already used keeps the'
    '# same index it always had.'
    '#'
    '# A row with a non-empty geolon/geolat/height needs swe_set_topo called first; a row with a'
    '# non-empty sid_mode needs swe_set_sid_mode called first -- both are per-row setup on that'
    '# row''s fresh library instance.'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- both drivers assert it matches verbatim before reading any'
    '# data, so a schema change here that a driver was not updated for fails loudly instead of'
    '# silently misreading columns.'
)
$columnHeader = 'case_id' + "`t" + 'func' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag' + "`t" +
    'hsys' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'armc' + "`t" + 'eps' + "`t" + 'sid_mode' + "`t" +
    'x2cross' + "`t" + 'dir'

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
Write-Host "  SOLCROSS           $solCrossCount"
Write-Host "  SOLCROSS_UT        $solCrossUtCount"
Write-Host "  MOONCROSS          $moonCrossCount"
Write-Host "  MOONCROSS_UT       $moonCrossUtCount"
Write-Host "  MOONCROSS_NODE     $moonCrossNodeCount"
Write-Host "  MOONCROSS_NODE_UT  $moonCrossNodeUtCount"
Write-Host "  HELIO_CROSS        $helioCrossCount"
Write-Host "  HELIO_CROSS_UT     $helioCrossUtCount"
