#Requires -Version 7.3
<#
.SYNOPSIS
    Regenerates args-grid.tsv, the committed swetest command-line grid for the text-level
    comparison between Astrodienst's swetest and this port's SweTest.

.DESCRIPTION
    scripts/verify-swetest-diff.ps1 runs every row of args-grid.tsv through both
    external/.c-reference/swetest.exe and Programs/SweTest, byte-diffs their stdout, and gates on
    Tests/swetest/known-diff.tsv. This script is the ONLY place that decides what those rows are --
    the grid is data, checked in like Tools/OracleGrid/grid-analytic.tsv, so a case is added by
    editing this script and regenerating, not by hand-editing the TSV.

    Every row's args column is a complete swetest argument string a user could paste after the exe
    name, apart from -edir<path>: scripts/verify-swetest-diff.ps1 appends that one itself, since the
    checkout path is machine-specific and does not belong in committed data. -head, unlike -edir, IS
    part of the committed args column -- Add-Row below bakes it in for every row except HEADER_BLOCK
    -- because leaving it to the verify script to add uniformly at run time would have made the one
    category that exists to omit it (HEADER_BLOCK, see below) indistinguishable from every other
    row. That was tried and measured wrong before this comment was written: with the verify script
    prepending -head unconditionally, HEADER_BLOCK's three rows came back 3/3 byte-identical, the
    version-banner difference it exists to record silently suppressed along with everything else.

    WHY -head IS NOT OPTIONAL FOR EVERY OTHER CATEGORY

    Every swetest run prints a "date (dmy) ... version 2.08" (or 2.10.03) line before anything
    else, and swe_version() bakes the running library's own version string into it. Since the
    entire point of this harness is comparing a 2.08 port against a 2.10.03 reference, that line
    would differ on every single row for a reason that has nothing to do with output formatting --
    it would drown out every real formatting difference this grid exists to find. -head (confirmed
    empirically: it suppresses the argv echo AND the whole date/UT/TT/Epsilon/Nutation block, not
    just the echo its name suggests) removes that block entirely, so the diff is scoped to the
    per-body/per-house/per-event lines the -f format letters actually control. HEADER_BLOCK
    deliberately omits -head, so the version-banner difference is still on record instead of
    silently unreachable.

    THREE PORT DEFECTS THIS GRID DELIBERATELY EXERCISES, NOT WORKS AROUND

    Building this grid meant running real invocations against both binaries first (never guess
    swetest's argument grammar from reading C alone), and that surfaced three genuine divergences
    in Programs/SweTest/Program.cs, confirmed against external/.c-reference/swetest.exe (built from
    the real v2.10.3final swetest.c) before being trusted as port bugs rather than harness mistakes:

      1. -p<selection> BODY-LIST TRUNCATION (Program.cs ~line 1221-1234). The C -p option takes the
         whole remainder of the argument as a multi-letter body-selection string. The C# only reads
         a single character (`spno = argv[i][2]`) and falls to `plsel = spno.ToString()` for any
         selection that is not exactly "d"/"p"/"h"/"a" -- so "-p0123456789" silently becomes "-p0"
         (Sun only) instead of ten bodies. -pd/-pp/-ph/-pa still work, because those four single
         letters hit the named switch cases before the truncating default -- this grid uses them
         for the main coverage and reserves a tiny PLSEL_TRUNCATION category to document the bug on
         a custom digit string, rather than letting it silently blank out the rest of the grid.

      2. C POINTER-ARITHMETIC MISTRANSLATED AS STRING CONCATENATION. Five call sites read
         `int.Parse(argv[i] + N)` where the C original is `atoi(argv[i] + N)` -- C pointer
         arithmetic that skips N characters, ported as C# `string + int`, which concatenates N's
         decimal text onto the end of the string instead. "-sid1" becomes the literal string
         "-sid14" (4 is the skip count) before int.Parse ever sees it, and throws. Confirmed at:
         -ay<N> (~878), -sidt0<N> (~884), -sidsp<N> (~893), -sid<N> (~919), -helflag<N> (~964).
         -j<jd> has the same shape one line differently (`begindate = argv[i] + 1`, ~933) and
         throws downstream instead, once the corrupted string reaches date parsing. Every one of
         these throws an unhandled exception in Programs/SweTest and runs cleanly on
         external/.c-reference/swetest.exe -- verified by hand before this comment was written, not
         inferred from reading the diff analysis this task started from. CRASH_MISC carries one row
         per site so the gate has to keep seeing (and re-confirming) each one rather than silently
         losing coverage of a bug that is trivial to "fix" by deleting the failing row.

      3. -house SCSCANF %c CRASH (Program.cs ~line 981). `C.sscanf(sp, "%lf,%lf,%c", ref top_long,
         ref top_lat, ref sout)` passes a `string` where the %c conversion expects a `char`, and
         SwissEphNet/Tools/C.scanf.cs throws InvalidCastException on every call, bracketed or not.
         This is swetest's primary house-cusp entry point, so HOUSES_CRASH documents the crash
         directly rather than pretending house-system coverage exists through some detour -- there
         isn't a working one; -hsy alone sets the system letter but never triggers a houses
         calculation without -house.

    A FOURTH CRASH, FOUND BY THE HARNESS ITSELF RATHER THAN BY READING THE SOURCE FIRST: MISC_FLAGS
    was built expecting -utc12:30:00 (Program.cs ~line 836, `stimein = argv[i].Substring(4, 30);`)
    to just work, the same way -ut<time>/-t<time> do a few lines away with a length-checked
    Substring. It does not -- C's `strncpy(stimein, argv[i] + 4, 30)` copies up to 30 bytes or stops
    at the null terminator, whichever comes first, but C#'s two-argument Substring(start, length)
    demands the string actually contain start+length characters and throws
    ArgumentOutOfRangeException otherwise. "-utc12:30:00" is 12 characters; Substring(4, 30) asks
    for 34. This row was not added to document a known bug -- it was added as an ordinary flag test,
    and scripts/verify-swetest-diff.ps1's first real run is what surfaced the crash. Left in
    MISC_FLAGS rather than moved to CRASH_MISC, as a record of the harness catching something this
    comment did not already know to look for.

    None of this is fixed here -- Programs/SweTest/Program.cs is a frozen transliteration (see
    CONTRIBUTING.md, "Transliterated files must never be reformatted") and this script's whole job
    is measuring the port as it stands, not repairing it.

    -m/-z/-f/-F FORMAT LETTERS ARE DELIBERATELY ABSENT from FMT_LETTERS. The task that produced this
    grid was told, in terms taken from a review of the swetest.c 2.08->2.10.03 delta, that those
    four are 2.10.03-only additions this port does not have. That is only true for 'm' and 'z' as
    -f format letters -- 'f'/'F' already exist in Program.cs's print_line switch (~2822-2823) for
    apsides/focus output, apparently carried over from 2.08. Rather than second-guess the review
    this task was handed, 'f'/'F' are excluded from FMT_LETTERS along with 'm'/'z', on the
    assumption that 2.10.03 reassigns or extends their meaning in a way the current implementation
    does not match; 'm' and 'z' remain valid as -p PLSEL letters (mean node, fictitious body) since
    that is a different namespace from -f format letters and unaffected by this exclusion.

    COLUMN LAYOUT (documented again, verbatim, at the top of the generated file, since that is what
    a reader of args-grid.tsv opens first)

    Tab-separated, LF line endings, one swetest invocation per data row. Lines starting with '#' are
    comments; the first non-comment line is the column header, which scripts/verify-swetest-diff.ps1
    asserts against verbatim:

      case_id   stable, unique, pipe-delimited id
      category  groups rows for reporting (PLANETS, FMT_LETTERS, HOUSES_CRASH, ...)
      args      the swetest argument string, everything except -edir<path>

.NOTES
    Deterministic by construction -- no timestamps, no randomness, no machine state. Running this
    script twice must produce a byte-identical file, the same invariant Tools/OracleGrid's grid
    generators carry.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$outputPath = Join-Path $PSScriptRoot 'args-grid.tsv'

$rows = [System.Collections.Generic.List[pscustomobject]]::new()
$categoryCounts = [System.Collections.Generic.Dictionary[string, int]]::new()

function Add-Row {
    param([string] $CaseId, [string] $Category, [string] $ArgsText)
    if (-not $categoryCounts.ContainsKey($Category)) { $categoryCounts[$Category] = 0 }
    $categoryCounts[$Category]++
    # -head is baked in here, for every category except HEADER_BLOCK, rather than added by
    # scripts/verify-swetest-diff.ps1 at run time -- see the HEADER_BLOCK section below and this
    # script's own .DESCRIPTION ("WHY -head IS NOT OPTIONAL") for why HEADER_BLOCK has to be the
    # one category where it is genuinely absent, not just a row with an empty-looking effect.
    $finalArgs = if ($Category -eq 'HEADER_BLOCK') { $ArgsText } else { "-head $ArgsText" }
    $rows.Add([pscustomobject]@{ CaseId = $CaseId; Category = $Category; Args = $finalArgs })
}

# A safe token for a case_id: swetest format letters include characters ('+', '-', '*', '/', '=')
# that are legal in a TSV field but awkward in an id a human has to read aloud or grep for.
$LetterName = @{
    '+' = 'PLUS'; '-' = 'MINUS'; '*' = 'STAR'; '/' = 'SLASH'; '=' = 'EQUALS'
}
function Get-LetterName {
    param([char] $Letter)
    if ($LetterName.ContainsKey([string]$Letter)) { return $LetterName[[string]$Letter] }
    return [string]$Letter
}

# ---------------------------------------------------------------------------------------
# PLANETS -- the bulk numeric coverage. -pd/-pp/-ph/-pa hit the four named switch cases in
# Program.cs's -p handling (PLSEL_D/P/H/A), so each expands to its full multi-body list instead of
# the single-character-truncation default described in the header comment above.
# ---------------------------------------------------------------------------------------

# Twelve dates: modern-era spread, a leap day, both sides of the Julian/Gregorian cutover (the day
# swetest itself switches calendars for a "-b" date), and two arbitrary real dates that are not
# round numbers.
$Dates12 = @(
    '1.1.1900', '1.1.1950', '1.1.2000', '1.1.2024', '1.1.2100', '1.1.2200',
    '29.2.2000', '4.10.1582', '15.10.1582', '21.6.2012', '25.12.1969', '11.9.2001'
)
$PlselMain = @('d', 'p', 'h', 'a')
foreach ($date in $Dates12) {
    foreach ($plsel in $PlselMain) {
        Add-Row -CaseId "PLANETS|$date|$plsel|ESWE" -Category 'PLANETS' `
            -ArgsText "-b$date -p$plsel -fPLBRS -eswe"
    }
}

# Moshier path: no ephemeris file dependency at all, so this isolates the analytic engine from
# file-decoding correctness -- a subset of the dates above, -pd/-pp only (the fictitious-body and
# hypothetical-body letters -ph/-pa exercise seorbel.txt regardless of -eswe/-emos, so repeating
# them here would test the same file-reading path twice under a different label).
$DatesMoseph6 = @('1.1.1900', '1.1.2000', '1.1.2024', '1.1.2100', '29.2.2000', '4.10.1582')
foreach ($date in $DatesMoseph6) {
    foreach ($plsel in @('d', 'p')) {
        Add-Row -CaseId "PLANETS_MOSEPH|$date|$plsel|EMOS" -Category 'PLANETS_MOSEPH' `
            -ArgsText "-b$date -p$plsel -fPLBRS -emos"
    }
}

# ---------------------------------------------------------------------------------------
# PLANETS_SINGLE -- one row per individual body letter, one fixed date, isolating any one body's
# formatting from the rest of the -pa list.
# ---------------------------------------------------------------------------------------

$SingleBodyLetters = @(
    '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'm', 't',
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I'
)
foreach ($letter in $SingleBodyLetters) {
    Add-Row -CaseId "PLANETS_SINGLE|$(Get-LetterName $letter)" -Category 'PLANETS_SINGLE' `
        -ArgsText "-b1.1.2000 -p$letter -fPLBRS -eswe"
}

# ---------------------------------------------------------------------------------------
# PLSEL_TRUNCATION -- documents defect 1 from the header comment: a custom multi-letter -p
# selection collapses to its first character.
# ---------------------------------------------------------------------------------------

$TruncationSelections = @('0123456789', '01mt', 'ABCDEFGHI')
$truncIdx = 0
foreach ($sel in $TruncationSelections) {
    $truncIdx++
    Add-Row -CaseId "PLSEL_TRUNCATION|$truncIdx" -Category 'PLSEL_TRUNCATION' `
        -ArgsText "-b1.1.2000 -p$sel -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# FMT_LETTERS -- one row per -f format letter Program.cs's print_line switch implements, Sun,
# one fixed date. 'm'/'z'/'f'/'F' excluded -- see the header comment. Each row pairs the letter
# under test with 'P' (object name) so the output line has a readable label; 'P' itself is tested
# alone.
# ---------------------------------------------------------------------------------------

$FmtLetters = @(
    'y', 'Y', 'P', 'p', 'J', 'T', 't', 'L', 'l', 'G', 'g', 'j', 'Z', 'S', 's',
    'B', 'b', 'A', 'a', 'D', 'd', 'I', 'i', 'H', 'h', 'K', 'k', 'R', 'W', 'w',
    'r', 'q', 'U', 'X', 'u', 'x', 'Q', 'N', 'n', '+', '-', '*', '/', '=', 'V', 'v'
)
foreach ($letter in $FmtLetters) {
    $fmt = if ($letter -eq 'P') { '-fP' } else { "-fP$letter" }
    Add-Row -CaseId "FMT_LETTERS|$(Get-LetterName $letter)" -Category 'FMT_LETTERS' `
        -ArgsText "-b1.1.2000 -p0 $fmt -eswe"
}

# ---------------------------------------------------------------------------------------
# FMT_MULTI -- combined format strings resembling realistic swetest usage, not one-letter-at-a-time
# isolation.
# ---------------------------------------------------------------------------------------

$MultiFormats = @(
    '-fPLBRS', '-fPYyTt', '-fPAaDdIiHhKk', '-fPUuXx', '-fPQ', '-fPNn'
)
$multiIdx = 0
foreach ($fmt in $MultiFormats) {
    $multiIdx++
    Add-Row -CaseId "FMT_MULTI|$multiIdx" -Category 'FMT_MULTI' `
        -ArgsText "-b1.1.2000 -pa $fmt -eswe"
}

# ---------------------------------------------------------------------------------------
# HOUSES_CRASH -- documents defect 3 from the header comment: -house crashes Programs/SweTest on
# every syntactically valid invocation, bracketed or not, confirmed against
# external/.c-reference/swetest.exe running the same arguments cleanly.
# ---------------------------------------------------------------------------------------

$HouseCrashCases = @(
    @{ Id = '1'; Args = '-house10,51.5,P' }
    @{ Id = '2'; Args = '-house[10,51.5,K]' }
    @{ Id = '3'; Args = '-house0,66,O' }
    @{ Id = '4'; Args = '-house139.6917,35.6895,W' }
)
foreach ($c in $HouseCrashCases) {
    Add-Row -CaseId "HOUSES_CRASH|$($c.Id)" -Category 'HOUSES_CRASH' `
        -ArgsText "-b1.1.2000 -p0 $($c.Args) -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# ECLIPSE -- solar/lunar eclipse search, rise/set, meridian transit, occultation. All confirmed
# working (no crash) against a fixed Greenwich-ish geoposition.
# ---------------------------------------------------------------------------------------

$EclipseTypes = @(
    @{ Name = 'SOLECL'; Flag = '-solecl' }
    @{ Name = 'LUNECL'; Flag = '-lunecl' }
    @{ Name = 'RISE'; Flag = '-rise' }
    @{ Name = 'OCCULT'; Flag = '-occult' }
    @{ Name = 'METR'; Flag = '-metr' }
)
$EclipseDates = @('1.1.2000', '1.1.2020', '21.6.2001', '25.12.2010')
foreach ($type in $EclipseTypes) {
    foreach ($date in $EclipseDates) {
        Add-Row -CaseId "ECLIPSE|$($type.Name)|$date" -Category 'ECLIPSE' `
            -ArgsText "-b$date -p0 -geopos10,51.5,0 $($type.Flag) -fPl -eswe"
    }
}

# ---------------------------------------------------------------------------------------
# STAR -- named fixed stars via -xf<name> -pf (the plsel letter that actually routes to
# SE_FIXSTAR; -x<name> alone without -pf exercises no star code path at all, confirmed by hand),
# plus two -x<name> direct-form rows and two asteroids with no local data file (DATA-MISSING,
# se00433s.se1/se00004s.se1 are not in Tests/conformance/required-ephemeris-files.tsv).
# ---------------------------------------------------------------------------------------

$StarNames = @('regulus', 'aldebaran', 'antares', 'spica', 'sirius', 'arcturus', 'pollux', 'betelgeuse')
foreach ($name in $StarNames) {
    Add-Row -CaseId "STAR|$name" -Category 'STAR' `
        -ArgsText "-b1.1.2000 -xf$name -pf -fPLBRS -eswe"
}
foreach ($name in @('regulus', 'sirius')) {
    Add-Row -CaseId "STAR_X|$name" -Category 'STAR' `
        -ArgsText "-b1.1.2000 -x$name -pf -fPLBRS -eswe"
}
foreach ($num in @('433', '4')) {
    Add-Row -CaseId "STAR_ASTEROID_MISSING|$num" -Category 'STAR' `
        -ArgsText "-b1.1.2000 -xs$num -ps -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# ORBEL -- osculating orbital elements. Object 0 (Sun) is deliberately included: swe_get_orbital_
# elements() rejects it (heliocentric elements of the Sun make no sense), which is a real,
# reproducible error line, not a harness mistake.
# ---------------------------------------------------------------------------------------

foreach ($letter in @('0', '1', '4', 'A')) {
    Add-Row -CaseId "ORBEL|$(Get-LetterName $letter)" -Category 'ORBEL' `
        -ArgsText "-b1.1.2000 -p$letter -orbel -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# DIFF -- -d (difference) / -D (midpoint) mode.
# ---------------------------------------------------------------------------------------

$DiffCases = @(
    @{ Id = 'd1'; Args = '-d1' }
    @{ Id = 'd3'; Args = '-d3' }
    @{ Id = 'D1'; Args = '-D1' }
    @{ Id = 'D4'; Args = '-D4' }
)
foreach ($c in $DiffCases) {
    Add-Row -CaseId "DIFF|$($c.Id)" -Category 'DIFF' `
        -ArgsText "-b1.1.2000 -p0 $($c.Args) -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# TIDACC -- -tidacc<N> silently sets the tidal-acceleration value to 0 instead of N on the port
# (Program.cs ~line 1168: `C.atof(argv[i] + 7)`, the same string-concatenation shape as defect 2 in
# the header comment, but C.atof tolerates the garbage prefix by returning 0.0 instead of throwing
# -- so this is a silent wrong value, not a crash, and needs its own category from CRASH_MISC).
# ---------------------------------------------------------------------------------------

foreach ($val in @('1.5', '-25.8', '0')) {
    Add-Row -CaseId "TIDACC|$val" -Category 'TIDACC' `
        -ArgsText "-b1.1.2000 -p1 -tidacc$val -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# CRASH_MISC -- defect 2 from the header comment: one row per broken call site.
# ---------------------------------------------------------------------------------------

$CrashCases = @(
    @{ Id = 'SID'; Args = '-sid1' }
    @{ Id = 'AY'; Args = '-ay1' }
    @{ Id = 'SIDT0'; Args = '-sidt01' }
    @{ Id = 'SIDSP'; Args = '-sidsp1' }
    @{ Id = 'HELFLAG'; Args = '-helflag1' }
    @{ Id = 'J'; Args = '-j2451545.0' }
)
foreach ($c in $CrashCases) {
    $dateArg = if ($c.Id -eq 'J') { '' } else { '-b1.1.2000 ' }
    Add-Row -CaseId "CRASH_MISC|$($c.Id)" -Category 'CRASH_MISC' `
        -ArgsText "$dateArg-p0 $($c.Args) -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# MISC_FLAGS -- one row per boolean/simple-value flag not already covered above.
# ---------------------------------------------------------------------------------------

$MiscFlags = @(
    @{ Id = 'TRUE'; Args = '-true' }
    @{ Id = 'NOABERR'; Args = '-noaberr' }
    @{ Id = 'NODEFL'; Args = '-nodefl' }
    @{ Id = 'NONUT'; Args = '-nonut' }
    @{ Id = 'SPEED'; Args = '-speed' }
    @{ Id = 'SPEED3'; Args = '-speed3' }
    @{ Id = 'NOSPEED'; Args = '-nospeed' }
    @{ Id = 'J2000'; Args = '-j2000' }
    @{ Id = 'ICRS'; Args = '-icrs' }
    @{ Id = 'BARY'; Args = '-bary' }
    @{ Id = 'HEL'; Args = '-hel' }
    @{ Id = 'TOPO'; Args = '-topo10,51.5,0' }
    @{ Id = 'GEOPOS'; Args = '-geopos10,51.5,0' }
    @{ Id = 'LMT'; Args = '-lmt' }
    @{ Id = 'LAT'; Args = '-lat' }
    @{ Id = 'ROUNDSEC'; Args = '-roundsec -dms' }
    @{ Id = 'ROUNDMIN'; Args = '-roundmin -dms' }
    @{ Id = 'DMS'; Args = '-dms' }
    @{ Id = 'SHORT'; Args = '-short' }
    @{ Id = 'HOR'; Args = '-n3 -s1 -hor' }
    @{ Id = 'GAP'; Args = "-g;" }
    @{ Id = 'BWD'; Args = '-bwd -n3 -s1' }
    @{ Id = 'TIME_T'; Args = '-t12:30:00' }
    @{ Id = 'TIME_UT'; Args = '-ut12:30:00' }
    @{ Id = 'TIME_UTC'; Args = '-utc12:30:00' }
    @{ Id = 'SIDUDEF'; Args = '-sidudef10,0' }
)
foreach ($f in $MiscFlags) {
    Add-Row -CaseId "MISC_FLAGS|$($f.Id)" -Category 'MISC_FLAGS' `
        -ArgsText "-b1.1.2000 -p0 $($f.Args) -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# DATE_FORMATS -- date-string shapes -b itself understands (see Program.cs's date parser, not the
# broken -j CLI flag, which CRASH_MISC covers). "-bj<jd>" is Program.cs's own smoke-test idiom
# (Tools/CReference/build-c.ps1's Invoke-SwetestSmoke) for a Julian-day date without going through
# the broken -j flag: "-b" is Substring-parsed correctly, and the date string that follows is then
# recognized as starting with 'j' by the (separate, working) date-string parser.
# ---------------------------------------------------------------------------------------

$DateFormatCases = @(
    @{ Id = 'LEAP_DAY'; Args = '-b29.2.2000' }
    @{ Id = 'NEGATIVE_YEAR'; Args = '-b1.1.-100' }
    @{ Id = 'JULIAN_CUTOVER_LAST'; Args = '-b4.10.1582' }
    @{ Id = 'GREGORIAN_CUTOVER_FIRST'; Args = '-b15.10.1582' }
    @{ Id = 'FAR_FUTURE'; Args = '-b1.1.2400' }
    @{ Id = 'OUT_OF_RANGE_PAST'; Args = '-b1.1.-4000' }
    @{ Id = 'FRACTIONAL_UT'; Args = '-b1.1.2000 -ut12:30:00' }
    @{ Id = 'BJ_JULIAN_DAY'; Args = '-bj2451545.0' }
    @{ Id = 'BJ_FORCE_JUL'; Args = '-bj2299160.5jul' }
    @{ Id = 'BJ_FORCE_GREG'; Args = '-bj2299160.5greg' }
    @{ Id = 'PLUS_ONE_DAY'; Args = '-b1.1.2000' }
    @{ Id = 'BWD_STEP'; Args = '-b1.1.2000 -bwd -n2 -s1' }
)
foreach ($c in $DateFormatCases) {
    $extra = if ($c.Id -eq 'PLUS_ONE_DAY') { ' +1' } else { '' }
    Add-Row -CaseId "DATE_FORMATS|$($c.Id)" -Category 'DATE_FORMATS' `
        -ArgsText "$($c.Args)$extra -p0 -fPl -eswe"
}

# ---------------------------------------------------------------------------------------
# HEADER_BLOCK -- deliberately omits -head, so the version-banner difference (2.08 vs 2.10.03,
# baked into the same line as delta-t) is on record as an expected PORT-VERSION difference instead
# of silently unreachable because every other category suppresses it.
# ---------------------------------------------------------------------------------------

$HeaderBlockDates = @('1.1.2000', '1.1.2100', '4.10.1582')
foreach ($date in $HeaderBlockDates) {
    Add-Row -CaseId "HEADER_BLOCK|$date" -Category 'HEADER_BLOCK' `
        -ArgsText "-b$date -p0 -fPl -eswe"
}

$totalRows = $rows.Count

# ---------------------------------------------------------------------------------------
# Write the file
# ---------------------------------------------------------------------------------------

$headerLines = @(
    '# args-grid.tsv -- committed swetest command-line grid for the text-level comparison between'
    "# Astrodienst's swetest and this port's SweTest. Regenerated by"
    '# Tools/SwetestDiff/gen-args-grid.ps1 -- never hand-edit this file; a change here has to come'
    '# from that script, committed together with its regenerated output. See that script''s own'
    '# .DESCRIPTION for why each category looks the way it does, including the three genuine port'
    '# defects (a -p body-list truncation, five "argv[i] + N" pointer-arithmetic-as-string-'
    '# concatenation crashes, and a -house sscanf %c crash) this grid deliberately exercises rather'
    '# than routes around, and why -m/-z/-f/-F are absent from FMT_LETTERS.'
    '#'
    '# scripts/verify-swetest-diff.ps1 runs every row through both'
    '# external/.c-reference/swetest.exe and Programs/SweTest, appending -edir"<path>" itself -- the'
    '# ephemeris directory is a checkout-local path and does not belong in committed data. -head is'
    '# already part of the args column below (every category except HEADER_BLOCK carries it) -- see'
    '# the generator''s .DESCRIPTION for why that has to be baked in per row rather than added'
    '# uniformly by the verify script.'
    '#'
    '# COLUMNS (tab-separated, one invocation per line, LF line endings)'
    '#'
    '#   case_id    stable, unique, pipe-delimited id'
    '#   category   groups rows for reporting'
    '#   args       the swetest argument string, everything except -edir<path>'
    '#'
    '# Lines starting with ''#'' are comments. The first non-comment line is the column-name header'
    '# below and is not a data row -- scripts/verify-swetest-diff.ps1 asserts it matches verbatim.'
)
$columnHeader = 'case_id' + "`t" + 'category' + "`t" + 'args'

$writer = [System.IO.StreamWriter]::new($outputPath, $false, [System.Text.UTF8Encoding]::new($false))
try {
    $writer.NewLine = "`n"
    foreach ($headerLine in $headerLines) { $writer.WriteLine($headerLine) }
    $writer.WriteLine($columnHeader)
    foreach ($row in $rows) {
        $writer.WriteLine(($row.CaseId + "`t" + $row.Category + "`t" + $row.Args))
    }
}
finally {
    $writer.Dispose()
}

Write-Host "PASS: wrote $totalRows data row(s) to $outputPath" -ForegroundColor Green
foreach ($key in $categoryCounts.Keys) {
    Write-Host ("  {0,-20} {1}" -f $key, $categoryCounts[$key])
}
