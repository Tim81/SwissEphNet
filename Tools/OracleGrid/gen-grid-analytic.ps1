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

      case_id, func, ipl, tjd, iflag, hsys, geolon, geolat, height, armc, eps, sid_mode

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
        $geolonField, $geolatField, $heightField, '', '', $sidModeField
    )
    return ($fields -join "`t")
}

function New-HousesRow {
    param([char] $Hsys, [double] $GeoLat, [double] $GeoLon, [double] $Tjd)
    $caseId = "HOUSES|$Hsys|$(Fmt $GeoLat)|$(Fmt $GeoLon)|$(Fmt $Tjd)"
    $fields = @(
        $caseId, 'HOUSES', '', (Fmt $Tjd), '', "$Hsys",
        (Fmt $GeoLon), (Fmt $GeoLat), '', '', '', ''
    )
    return ($fields -join "`t")
}

function New-HousesArmcRow {
    param([char] $Hsys, [double] $GeoLat, [double] $Eps, [double] $Armc)
    $caseId = "HOUSESARMC|$Hsys|$(Fmt $GeoLat)|$(Fmt $Eps)|$(Fmt $Armc)"
    $fields = @(
        $caseId, 'HOUSES_ARMC', '', '', '', "$Hsys",
        '', (Fmt $GeoLat), '', (Fmt $Armc), (Fmt $Eps), ''
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
# Build rows
# ---------------------------------------------------------------------------------------

$rows = [System.Collections.Generic.List[string]]::new()
$calcCount = 0
$calcUtCount = 0
$housesCount = 0
$housesArmcCount = 0

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

$totalRows = $rows.Count
if ($totalRows -ne ($calcCount + $calcUtCount + $housesCount + $housesArmcCount)) {
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
    '#   func       CALC | CALC_UT | HOUSES | HOUSES_ARMC'
    '#   ipl        body number                                        [CALC, CALC_UT]'
    '#   tjd        Julian day (ET for CALC; UT for CALC_UT and HOUSES) [CALC, CALC_UT, HOUSES]'
    '#   iflag      swe_calc iflag, with SEFLG_MOSEPH already OR-ed in  [CALC, CALC_UT]'
    '#   hsys       house-system letter                                [HOUSES, HOUSES_ARMC]'
    '#   geolon     geographic longitude, degrees east                 [HOUSES; CALC/CALC_UT topo rows]'
    '#   geolat     geographic latitude, degrees north                 [HOUSES, HOUSES_ARMC; CALC/CALC_UT topo rows]'
    '#   height     observer height above sea level, metres            [CALC/CALC_UT topo rows only]'
    '#   armc       ARMC, degrees                                      [HOUSES_ARMC]'
    '#   eps        obliquity of the ecliptic, degrees                 [HOUSES_ARMC]'
    '#   sid_mode   swe_set_sid_mode mode, applied before the row runs [CALC/CALC_UT rows whose iflag carries SEFLG_SIDEREAL]'
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
    'hsys' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'armc' + "`t" + 'eps' + "`t" + 'sid_mode'

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
Write-Host "  HOUSES        $housesCount"
Write-Host "  HOUSES_ARMC   $housesArmcCount"
