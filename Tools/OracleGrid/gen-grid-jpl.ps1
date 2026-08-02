#Requires -Version 7.3
<#
.SYNOPSIS
    Regenerates grid-jpl.tsv, the committed input set for the bit-exact oracle harness's third
    stage: the SEFLG_JPLEPH code paths neither grid-analytic.tsv nor grid-files.tsv can reach.

.DESCRIPTION
    grid-analytic.tsv OR-s SEFLG_MOSEPH into every swe_calc/swe_calc_ut row and grid-files.tsv
    OR-s SEFLG_SWIEPH into every one of its own, so between them the JPL backend --
    SwissEphNet/CPort/SweJPL.cs in its entirety (swi_open_jpl_file, read_const, state, interp,
    swi_pleph, swi_close_jpl_file), plus sweph.c's jplplan/jpl_denum handling and
    swe_set_jpl_file itself -- has no bit-level coverage of any kind. This grid is the first one
    that asks for it.

    Same shape as gen-grid-analytic.ps1 and gen-grid-files.ps1, and the same reason for existing
    as a separate, committed file: Tools/CReference/sedump.c and Tools/OracleDump/Program.cs both
    replay it rather than building their own inputs, so the two drivers cannot silently drift
    apart on what gets tested.

    IT REUSES grid-files.tsv's COLUMN LAYOUT VERBATIM

    The header line below is byte-for-byte grid-files.tsv's own fourteen-column header, and that is
    deliberate: both drivers dispatch their column layout on which header they read (see
    sedump.c's EXPECTED_HEADER_ANALYTIC/EXPECTED_HEADER_FILES and OracleDump's
    ExpectedHeaderAnalytic/ExpectedHeaderFiles), and this grid needs exactly the columns
    grid-files.tsv already defines -- ipl, tjd, iflag, the three topocentric columns, sid_mode,
    and the t0/ayan_t0 pair -- with nothing added and nothing dropped. Introducing a third,
    identical-but-differently-named header would have forced a third parsing mode into both
    drivers to describe the same fourteen columns. What makes this a distinct grid is the ephemeris
    flag every row carries and the JPL file the drivers are pointed at, not its schema.

    THE DE FILE IS SUPPLIED BY THE RUNNER, NOT BY THIS REPO

    A JPL DE file is 190 MB (DE406) to 2.6 GB (DE431); this repo ships none and never will. Both
    drivers therefore take the file to open as an argument -- see scripts/run-oracle-dump.ps1's
    -JplFile and its SWISSEPH_ORACLE_JPL_FILE environment variable, which is what makes this grid
    opt-in and keeps it out of CI, exactly as SWISSEPH_CONFORMANCE_INCLUDE_JPL/
    SWISSEPH_CONFORMANCE_JPL_FILE already do for the correctness oracle's own JPL iterations
    (Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs).

    Which DE file is used does not have to be pinned for this grid to mean something, and that is
    the whole reason the oracle -- rather than the conformance corpus -- is the instrument here:
    both sides open the SAME file on the SAME machine over the SAME inputs and their raw IEEE-754
    bit patterns are compared against each other, so any difference is a port defect by
    construction. (The conformance corpus cannot do this: setest hardcodes
    swe_set_jpl_file("de431.eph") at suite scope, so t.exp's expected values are DE431's and
    running it against another DE file produces real differences indistinguishable from port
    defects.)

    THE DATE RANGE IS THE ONE THING THE DE FILE DOES CONSTRAIN

    A tjd outside the opened file's own span makes swi_pleph refuse the row, which is correct
    behaviour on both sides but makes for a useless comparison -- both sides would agree on an
    error message and compute nothing. The dates below are chosen to sit inside DE406's span (JD
    625360.5 to 2816848.5, read out of the file's own header record; calendar years -2999 to
    +3000), with roughly a century of margin at each end. DE431 (-13000 to +17000) is a superset,
    so the same dates are valid there too; a narrower file than DE406 would need this list
    revisited.

    COVERAGE

      swe_calc / swe_calc_ut -- bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating apogee,
                                 Earth), the same body set both sibling grids use, crossed with ten
                                 Julian days spread across DE406's span and eight iflag
                                 combinations, every one carrying SEFLG_JPLEPH. Both the ET
                                 (swe_calc) and UT (swe_calc_ut) entry points, since the UT one
                                 routes through swe_deltat_ex, whose own ephemeris-flag branch
                                 (swephlib.c:3318-3353) reads swed.jpldenum -- a value nothing but
                                 an actually-opened JPL file ever sets.

      SEFLG_JPLHOR and SEFLG_JPLHOR_APPROX are two of those eight combinations, and they are the
      reason this grid can say anything at all about load_dpsi_deps -- see below.

    SEFLG_JPLHOR IS HOW load_dpsi_deps BECOMES OBSERVABLE

    load_dpsi_deps (sweph.c:1380, SwissEphNet/CPort/Sweph.cs:1637) has exactly one caller in the
    whole library: swe_set_jpl_file, and only on the branch where the file it just opened reports
    jpldenum >= 403 (sweph.c:1503-1504). Nothing else reaches it, from any API, ever. Neither
    sibling grid calls swe_set_jpl_file at all, so before this grid the function had no coverage of
    any kind -- not from the oracle, not from the characterization baseline (which never opens a
    file), and not from the conformance corpus.

    DE406 reports jpldenum 406, so every row of this grid enters it. What makes that VISIBLE, and
    not merely asserted, is the SEFLG_JPLHOR rows: plaus_iflag (sweph.c:6121-6141) reads
    swed.eop_dpsi_loaded -- a variable load_dpsi_deps is the only writer of -- and emits a
    different serr string for each of its values, into the same err column both drivers already
    dump and Tools/OracleVerify already compares byte for byte:

        0  "you did not call swe_set_jpl_file(); default to SEFLG_JPLHOR_APPROX"
       -1  "file eop_1962_today.txt not found; default to SEFLG_JPLHOR_APPROX"
       -2  "file eop_1962_today.txt corrupt; default to SEFLG_JPLHOR_APPROX"
       -3  "file eop_finals.txt corrupt; default to SEFLG_JPLHOR_APPROX"

    A JPLHOR row whose err column reads "you did not call swe_set_jpl_file()" proves the driver
    never got as far as swe_set_jpl_file; one that reads any of the other three proves
    swe_set_jpl_file opened the file, saw denum >= 403, and called load_dpsi_deps, on that side, on
    that run. The two sides agreeing on which string appeared is the measurement.

    Which of the three the run actually produces depends on whether eop_1962_today.txt and
    eop_finals.txt sit in the directory the drivers are pointed at. Astrodienst distribute both;
    this repo ships neither and does not require them. Without them load_dpsi_deps runs its
    file-not-found early return (swed.eop_dpsi_loaded = ERR) and its parsing loop stays uncovered;
    with them present in the -JplEpheDir the parsing loop runs too and the err string changes
    accordingly on both sides at once. Either way the grid itself is unchanged -- this is a
    property of the runner's directory, not of these rows.

    COLUMN LAYOUT

    Identical to grid-files.tsv's; see gen-grid-files.ps1's own header for the full description of
    all eighteen columns. star, x2cross and dir are always empty here (this grid has no fixed-star
    or crossing rows at all), and so are t0/ayan_t0 (its SIDEREAL rows use predefined modes only,
    the same as grid-files.tsv's), method and hsys (no NOD_APS_UT/HOUSES_EX rows here), and armc/
    eps (no HOUSES_ARMC_EX2 rows here either).

.NOTES
    Deterministic by construction: no timestamps, no randomness, no machine-dependent state (the
    Julian day values below come from a fixed Gregorian-calendar-to-Julian-day-number conversion
    run inside this script, not from any live clock or ephemeris lookup). Running this script twice
    must produce a byte-identical file.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# No native (non-PowerShell) commands run in this script -- see gen-grid-analytic.ps1's own copy
# of this line for why it is set anyway.
$PSNativeCommandUseErrorActionPreference = $false

$outputPath = Join-Path $PSScriptRoot 'grid-jpl.tsv'

# ---------------------------------------------------------------------------------------
# Swiss Ephemeris constants (SwissEphNet/SwissEph.swephexp.h.cs / external/swisseph/swephexp.h).
# ---------------------------------------------------------------------------------------

$SEFLG_JPLEPH        = 1
$SEFLG_HELCTR        = 8
$SEFLG_SPEED         = 256
$SEFLG_BARYCTR       = 16 * 1024
$SEFLG_TOPOCTR       = 32 * 1024
$SEFLG_SIDEREAL      = 64 * 1024
$SEFLG_JPLHOR        = 256 * 1024
$SEFLG_JPLHOR_APPROX = 512 * 1024

# Deliberately a literal, not read off any assembly's SwissEph.SE_NSIDM_PREDEF -- see
# gen-grid-analytic.ps1's and gen-grid-files.ps1's own copies of this constant and comment for why.
$SidModeSweepCount = 47

# ---------------------------------------------------------------------------------------
# Formatting -- matches gen-grid-analytic.ps1's and gen-grid-files.ps1's Fmt/FmtI: invariant
# culture, "R" round-trip precision for doubles, so every machine that runs this script (and the
# drivers that later parse its output) reads the identical digits.
# ---------------------------------------------------------------------------------------

function Fmt {
    param([double] $Value)
    return $Value.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}

function FmtI {
    param([int] $Value)
    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# Fliegel & Van Flandern's proleptic-Gregorian-to-Julian-day-number conversion -- an exact copy of
# gen-grid-files.ps1's Get-Jdn, self-contained for the same reason: this script may not depend on
# the input-building code the drivers exist to check. Returns the Julian day number at 12:00 (noon)
# of the given calendar date, the convention swe_calc/swe_calc_ut's tjd parameter uses. Handles
# negative (astronomical, proleptic Gregorian) years unchanged -- the formula is arithmetic on the
# year number and has no calendar-era branch, which is what lets the BC dates below go through it.
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
# gen-grid-analytic.ps1's own copy of this function and its comment for the full reasoning. A
# separate counter from either sibling script's: the three scripts run as separate processes over
# separate grids, so there is no shared state to keep in sync between them.
$script:sidModeCycleNext = 0
function Get-NextSidMode {
    $mode = $script:sidModeCycleNext % $SidModeSweepCount
    $script:sidModeCycleNext++
    return $mode
}

# ---------------------------------------------------------------------------------------
# Row builder -- one func family only (CALC/CALC_UT), so unlike its sibling scripts this one needs
# a single builder. Column order matches grid-files.tsv's header exactly.
# ---------------------------------------------------------------------------------------

function New-CalcJplRow {
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
    # The four trailing empties are method, hsys, armc and eps -- appended to grid-files.tsv's
    # layout when NOD_APS_UT/HOUSES_EX and, later, HOUSES_ARMC_EX2 were added to that grid. None of
    # those funcs appears here -- this grid is swe_calc/swe_calc_ut only -- but the column count is
    # what both drivers assert against, so every row carries them.
    $fields = @(
        $caseId, $Func, (FmtI $Ipl), (Fmt $Tjd), (FmtI $IFlag), '',
        $geolonField, $geolatField, $heightField, $sidModeField, '', '',
        '', '', '', '', '', ''
    )
    return ($fields -join "`t")
}

# ---------------------------------------------------------------------------------------
# Grid values
# ---------------------------------------------------------------------------------------

# SE_SUN..SE_EARTH (0-14): the same body set both sibling grids use, for the same reason -- see
# gen-grid-analytic.ps1's own header. Bodies 10-13 (mean/true node, mean/osculating apogee) carry
# no ephemeris data of their own but are derived from the Moon's computed position, so under
# SEFLG_JPLEPH they still route through jplplan for the Moon; and plaus_iflag strips
# SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX from exactly those four (sweph.c:6114-6117), which is itself a
# branch worth having both sides walk.
$Bodies = 0..14

# Ten Julian days spread across DE406's own span (JD 625360.5 to 2816848.5, calendar years -2999
# to +3000, read out of the file's header record), with about a century of margin at each end so
# no interpolation window can reach past either edge. Written as calendar dates rather than raw
# Julian days so the margin is checkable by eye; Get-Jdn above turns them into the doubles that
# actually land in the file. Deliberately not the same date list either sibling grid uses -- those
# are pinned to the two era .se1 files' 1200-2399 span, which is a tiny sliver of what a DE file
# covers, and spending all ten rows of a JPL grid inside it would leave the majority of the file's
# range untouched.
$CalcJdsJpl = @(
    (Get-Jdn -2900 3 1),
    (Get-Jdn -2000 1 1),
    (Get-Jdn -1000 6 15),
    (Get-Jdn 1 1 1),
    (Get-Jdn 500 3 20),
    (Get-Jdn 1000 11 5),
    (Get-Jdn 1500 7 1),
    (Get-Jdn 1900 1 1),
    (Get-Jdn 2400 1 1),
    (Get-Jdn 2900 1 1)
)

$TopoGeoLon = -118.24
$TopoGeoLat = 34.05
$TopoHeight = 100.0

# Eight combinations. The first six mirror gen-grid-files.ps1's own six one for one (PLAIN, SPEED,
# TOPOCTR, SIDEREAL, HELCTR, BARYCTR), so the JPL backend is asked for the same shapes of result
# the SWIEPH backend is already asked for and a difference between the two grids' outcomes points
# at the backend rather than at the surrounding machinery. The last two are JPL-only:
# SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX are stripped outright unless the ephemeris flag is SEFLG_JPLEPH
# (sweph.c:6110-6112), so no other grid can reach them at all, and SEFLG_JPLHOR is what makes
# load_dpsi_deps observable -- see this script's own header.
$FlagCombos = @(
    [pscustomobject]@{ Name = 'PLAIN';       Flag = 0;                    NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'SPEED';       Flag = $SEFLG_SPEED;         NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'TOPOCTR';     Flag = $SEFLG_TOPOCTR;       NeedsTopo = $true;  NeedsSid = $false }
    [pscustomobject]@{ Name = 'SIDEREAL';    Flag = $SEFLG_SIDEREAL;      NeedsTopo = $false; NeedsSid = $true }
    [pscustomobject]@{ Name = 'HELCTR';      Flag = $SEFLG_HELCTR;        NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'BARYCTR';     Flag = $SEFLG_BARYCTR;       NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'JPLHOR';      Flag = $SEFLG_JPLHOR;        NeedsTopo = $false; NeedsSid = $false }
    [pscustomobject]@{ Name = 'JPLHORAPPROX'; Flag = $SEFLG_JPLHOR_APPROX; NeedsTopo = $false; NeedsSid = $false }
)

# ---------------------------------------------------------------------------------------
# Build rows
# ---------------------------------------------------------------------------------------

$rows = [System.Collections.Generic.List[string]]::new()
$calcCount = 0
$calcUtCount = 0

foreach ($ipl in $Bodies) {
    foreach ($tjd in $CalcJdsJpl) {
        foreach ($combo in $FlagCombos) {
            $iflag = $SEFLG_JPLEPH -bor $combo.Flag
            $geolon  = if ($combo.NeedsTopo) { $TopoGeoLon } else { $null }
            $geolat  = if ($combo.NeedsTopo) { $TopoGeoLat } else { $null }
            $height  = if ($combo.NeedsTopo) { $TopoHeight } else { $null }
            # Cycled, not pinned -- see Get-NextSidMode's own comment.
            $sidMode = if ($combo.NeedsSid)  { Get-NextSidMode } else { $null }

            $rows.Add((New-CalcJplRow -Func 'CALC' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcCount++

            $rows.Add((New-CalcJplRow -Func 'CALC_UT' -Ipl $ipl -Tjd $tjd `
                -FlagName $combo.Name -IFlag $iflag `
                -GeoLon $geolon -GeoLat $geolat -Height $height -SidMode $sidMode))
            $calcUtCount++
        }
    }
}

$totalRows = $rows.Count
$expectedTotal = $calcCount + $calcUtCount
if ($totalRows -ne $expectedTotal) {
    throw 'Row count bookkeeping is inconsistent -- this is a bug in this script, not a data problem.'
}

# Every case_id must be unique: Tools/OracleVerify keys both dumps by it, so a duplicate would
# silently make one row shadow another and shrink what is actually compared. Cheap to assert here,
# and this grid's case_id is built from four fields that the nested loops above are responsible for
# keeping distinct.
$caseIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($row in $rows) {
    if (-not $caseIds.Add(($row -split "`t")[0])) {
        throw "Duplicate case_id '$(($row -split "`t")[0])' -- this is a bug in this script, not a data problem."
    }
}

# ---------------------------------------------------------------------------------------
# Header block
# ---------------------------------------------------------------------------------------

$headerLines = @(
    '# grid-jpl.tsv -- committed input vectors for the bit-exact C-vs-C# comparison harness'
    '# (stage 3, the JPL ephemeris backend). Regenerated by Tools/OracleGrid/gen-grid-jpl.ps1'
    '# -- never hand-edit this file; a change here has to come from that script, committed'
    '# together with its regenerated output.'
    '#'
    '# WHY THIS GRID EXISTS ALONGSIDE grid-analytic.tsv AND grid-files.tsv'
    '#'
    '# grid-analytic.tsv OR-s SEFLG_MOSEPH into every swe_calc/swe_calc_ut row and grid-files.tsv'
    '# OR-s SEFLG_SWIEPH into every one of its own, so between them SwissEphNet/CPort/SweJPL.cs in'
    '# its entirety -- swi_open_jpl_file, read_const, state, interp, swi_pleph,'
    '# swi_close_jpl_file -- plus sweph.c''s jplplan and swe_set_jpl_file itself have no bit-level'
    '# coverage of any kind. Every row here requests SEFLG_JPLEPH instead.'
    '#'
    '# THE DE FILE IS SUPPLIED BY THE RUNNER, NOT BY THIS REPO'
    '#'
    '# A JPL DE file is 190 MB (DE406) to 2.6 GB (DE431); this repo ships none. Both drivers take'
    '# the file to open as an argument -- see scripts/run-oracle-dump.ps1''s -JplFile and its'
    '# SWISSEPH_ORACLE_JPL_FILE environment variable, which is what makes this grid opt-in and'
    '# keeps it out of CI. Which DE file is used does not have to be pinned: both sides open the'
    '# SAME file on the SAME machine over the SAME inputs and their raw IEEE-754 bit patterns are'
    '# compared against each other, so any difference is a port defect by construction.'
    '#'
    '# COVERAGE'
    '#'
    '# swe_calc / swe_calc_ut: bodies 0-14 (Sun..Pluto, mean/true node, mean/oscillating apogee,'
    '# Earth), crossed with ten Julian days spread across DE406''s span (JD 625360.5 to 2816848.5,'
    '# calendar years -2999 to +3000, with about a century of margin at each end) and eight iflag'
    '# combinations, every one carrying SEFLG_JPLEPH. A tjd outside the opened file''s own span'
    '# makes swi_pleph refuse the row -- correct behaviour on both sides, but a useless'
    '# comparison, which is what the margins are for. DE431 (-13000 to +17000) is a superset, so'
    '# the same dates are valid there too.'
    '#'
    '# Six of the eight iflag combinations mirror grid-files.tsv''s own six (PLAIN, SPEED, TOPOCTR,'
    '# SIDEREAL, HELCTR, BARYCTR). The other two are JPL-only: SEFLG_JPLHOR and'
    '# SEFLG_JPLHOR_APPROX are stripped outright unless the ephemeris flag is SEFLG_JPLEPH'
    '# (sweph.c:6110-6112), so no other grid can reach them at all.'
    '#'
    '# SEFLG_JPLHOR IS HOW load_dpsi_deps BECOMES OBSERVABLE'
    '#'
    '# load_dpsi_deps (sweph.c:1380) has exactly one caller in the whole library: swe_set_jpl_file,'
    '# and only where the file it just opened reports jpldenum >= 403 (sweph.c:1503-1504). Neither'
    '# sibling grid calls swe_set_jpl_file at all. DE406 reports denum 406, so every row here'
    '# enters it -- and plaus_iflag (sweph.c:6121-6141) makes that visible rather than merely'
    '# asserted: it reads swed.eop_dpsi_loaded, which load_dpsi_deps is the only writer of, and'
    '# emits a different serr string per value into the same err column both drivers dump and'
    '# Tools/OracleVerify compares byte for byte. A JPLHOR row reading "you did not call'
    '# swe_set_jpl_file()" proves the driver never got there; any of the other three proves it did.'
    '#'
    '# COLUMN LAYOUT'
    '#'
    '# Byte-for-byte grid-files.tsv''s own sixteen-column header, deliberately: both drivers'
    '# dispatch their column layout on which header they read, and this grid needs exactly the'
    '# columns that one already defines. What makes this a distinct grid is the ephemeris flag'
    '# every row carries and the JPL file the drivers are pointed at, not its schema. See'
    '# Tools/OracleGrid/gen-grid-files.ps1''s header for the full description of all sixteen'
    '# columns. star, x2cross, dir, t0, ayan_t0, method, hsys, armc and eps are always empty here.'
    '#'
    '# That coupling is the point and also the cost: this grid has to be regenerated whenever'
    '# grid-files.tsv''s layout changes, and it was not when method/hsys were appended for'
    '# NOD_APS_UT and HOUSES_EX. Nothing caught it, because this grid is opt-in -- it needs a'
    '# multi-hundred-MB JPL DE file no runner has, so no gate replays it. The header assertion in'
    '# both drivers did fail loudly the first time it was run by hand, which is the design working;'
    '# what is missing is anything that runs it. Treat a files-grid schema change as a two-grid'
    '# change. This regeneration is itself the second half of one such change: armc/eps were'
    '# appended to grid-files.tsv for HOUSES_ARMC_EX2 (swe_houses_ex2/swe_houses_armc_ex2 are new'
    '# in 2.10.03), and this grid picked up the same two trailing empty columns in the same commit,'
    '# not a later one.'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- both drivers assert it matches verbatim before reading any'
    '# data.'
)
$columnHeader = 'case_id' + "`t" + 'func' + "`t" + 'ipl' + "`t" + 'tjd' + "`t" + 'iflag' + "`t" +
    'star' + "`t" + 'geolon' + "`t" + 'geolat' + "`t" + 'height' + "`t" + 'sid_mode' + "`t" +
    'x2cross' + "`t" + 'dir' + "`t" + 't0' + "`t" + 'ayan_t0' + "`t" + 'method' + "`t" + 'hsys' + "`t" +
    'armc' + "`t" + 'eps'

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
Write-Host "  CALC     $calcCount"
Write-Host "  CALC_UT  $calcUtCount"
