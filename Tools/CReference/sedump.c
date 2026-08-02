/*
 * sedump.c -- the C side of the bit-exact comparison harness.
 *
 * Replays the committed grids against Astrodienst's own C, linked in as libswe -- see
 * scripts/run-oracle-dump.ps1, which builds this file and picks which .lib it links against
 * (2.10.03 by default, 2.08 for isolating transliteration defects from porting differences).
 * Tools/OracleDump/Program.cs is this file's .NET counterpart; the two must produce output in
 * the same shape for a later, separate pass to diff.
 *
 *   Tools/OracleGrid/grid-analytic.tsv  -- swe_calc/swe_calc_ut (SEFLG_MOSEPH),
 *                                          swe_houses/swe_houses_armc, the eight crossing
 *                                          functions (swe_solcross/_ut, swe_mooncross/_ut,
 *                                          swe_mooncross_node/_ut, swe_helio_cross/_ut), also
 *                                          under SEFLG_MOSEPH, and swe_get_ayanamsa/_ex/_ex_ut
 *                                          (direct ayanamsa coverage -- every predefined sid_mode
 *                                          plus SE_SIDM_USER). Touches no ephemeris data file.
 *                                          See gen-grid-analytic.ps1's header.
 *   Tools/OracleGrid/grid-files.tsv     -- swe_calc/swe_calc_ut (SEFLG_SWIEPH), the
 *                                          swe_fixstar family, swe_get_planet_name, and the same
 *                                          eight crossing functions under SEFLG_SWIEPH. Opens
 *                                          the shipped .se1/sefstars.txt files. See
 *                                          gen-grid-files.ps1's header.
 *   Tools/OracleGrid/grid-jpl.tsv       -- swe_calc/swe_calc_ut (SEFLG_JPLEPH), including the
 *                                          SEFLG_JPLHOR/SEFLG_JPLHOR_APPROX combinations no other
 *                                          grid can reach (sweph.c:6110-6112 strips both unless
 *                                          the ephemeris flag is SEFLG_JPLEPH). Opens a JPL DE
 *                                          file this repo does not ship, named by the optional
 *                                          fourth argument below. See gen-grid-jpl.ps1's header.
 *
 * Every grid shares one output shape (see OUTPUT COLUMN LAYOUT below) and one row-processing loop
 * in main(); which column layout a given input file uses dispatches on its header line, checked
 * against EXPECTED_HEADER_ANALYTIC and EXPECTED_HEADER_FILES below -- those two layouts have
 * different column counts (16 vs 14), so a header mismatch is caught before any row is parsed.
 * grid-jpl.tsv carries grid-files.tsv's header verbatim and is therefore read in MODE_FILES: it
 * needs exactly the columns that layout already defines, and what makes it a distinct grid is the
 * ephemeris flag its rows carry and the JPL file this driver is pointed at, not its schema -- see
 * gen-grid-jpl.ps1's own header for why a third, identical-but-differently-named header would
 * have bought nothing but a third parsing mode.
 *
 * SWISSEPH_HAS_CROSSING: THE EIGHT CROSSING FUNCTIONS DO NOT EXIST IN 2.08
 *
 * This same source file is compiled twice -- once here against external/swisseph (2.10.03),
 * once by Tools/CReference/build-c.ps1 against external/pyswisseph-2.08 -- and swe_solcross,
 * swe_mooncross, swe_mooncross_node, swe_helio_cross and their _ut variants are absent from
 * pyswisseph-2.08 entirely (verified: zero matches for "solcross", "mooncross" or "helio_cross"
 * anywhere under external/pyswisseph-2.08/), so a build against the 2.08 headers has no
 * declaration to call. scripts/run-oracle-dump.ps1 defines SWISSEPH_HAS_CROSSING=1 on the command
 * line when it compiles this file against 2.10.03; Tools/CReference/build-c.ps1's 2.08 build does
 * not define it, so the #else branch below applies there by default, with no change needed to
 * that script. The #else branch cannot call the real API, but it still emits exactly one row
 * per crossing case, with the same column count the real branch would use for that func, and a
 * clearly out-of-band retc (NOT_IN_208_RETC) plus an explanatory serr -- so a 2.08 build's row
 * count for a crossing-bearing grid still matches the grid's own row count (see
 * scripts/run-oracle-dump.ps1's own row-count guards, which fail loudly on any mismatch) and the
 * row still parses cleanly for any future three-way classification that reads the 2.08 dump.
 *
 * INVOCATION
 *
 *   sedump.exe <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]
 *
 * ephe-dir is optional. grid-analytic.tsv needs it never (every row forces SEFLG_MOSEPH, so no
 * row ever opens a file) and the existing two-argument invocation is untouched -- passing it is
 * new, additive behavior, not a change to how the analytic grid has always been run. grid-files.tsv
 * needs it always: when given, swe_set_ephe_path(ephe-dir) runs at the top of every row's
 * processing, right after swe_close() (see FRESH LIBRARY STATE PER ROW below) -- this is the "The
 * C side just needs swe_set_ephe_path to the same directory" half of the fix
 * Tests/SwissEphNet.Conformance.Tests/Dispatch/EphemerisFileResolver.cs's Attach describes for
 * the .NET side (sweph.c:1315-1350: swe_set_ephe_path is not a setter, it closes every open file
 * and eagerly opens the Moon file to pin tidal acceleration, so the path has to be set before any
 * row-specific call runs, not after).
 *
 * jpl-file is optional too, and only grid-jpl.tsv needs it. When given, swe_set_jpl_file(jpl-file)
 * runs immediately AFTER swe_set_ephe_path, once per row. That order is not incidental and cannot
 * be swapped: swe_set_jpl_file opens the file eagerly, right there in the call, resolving the name
 * against swed.ephepath as it stands at that moment (sweph.c:1499-1505). Called before
 * swe_set_ephe_path it would resolve against whatever path was left over -- SE_EPHE_PATH's
 * compiled-in default on the first row -- almost certainly fail to find the file, and so never
 * reach the jpldenum >= 403 branch below; swe_set_ephe_path would then close the JPL file it did
 * not manage to open anyway. Every SEFLG_JPLEPH row would fall back through SEFLG_SWIEPH to
 * Moshier (sweph.c:894-913) and compare bit-identical between the two sides while measuring
 * nothing about the JPL backend at all. For the same reason, passing jpl-file with an empty
 * ephe-dir is rejected outright in main() instead of being left to resolve against SE_EPHE_PATH.
 *
 * Passing jpl-file also has one effect no other argument does: swe_set_jpl_file is the only caller
 * of load_dpsi_deps in the whole library (sweph.c:1503-1504, on the branch where the file it just
 * opened reports jpldenum >= 403), so this argument is the only way either driver reaches that
 * function at all. gen-grid-jpl.ps1's header describes how the SEFLG_JPLHOR rows make that
 * reachability observable in the err column instead of merely asserted.
 *
 * ONE PIECE OF STATE swe_close() DOES NOT RESET: swed.eop_dpsi_loaded
 *
 * swe_close() frees swed.dpsi and swed.deps but leaves swed.eop_dpsi_loaded at whatever
 * load_dpsi_deps last wrote (sweph.c's swe_close: the two free() calls have no accompanying
 * assignment, and the port mirrors that faithfully in SwissEphNet/CPort/Sweph.cs). This driver's
 * per-row swe_close() therefore does NOT give a row a fresh eop state, while
 * Tools/OracleDump/Program.cs's fresh SwissEph instance does -- the same shape of difference this
 * file's FRESH LIBRARY STATE PER ROW section already documents for swe_houses_armc_ex2's
 * saved_sundec, and like that one it does not currently bite, for a reason worth writing down:
 *
 *   load_dpsi_deps returns early only when eop_dpsi_loaded > 0, i.e. only after a SUCCESSFUL
 *   load. With neither eop_1962_today.txt nor eop_finals.txt in ephe-dir -- which is the case for
 *   every directory this repo declares -- the very first row's call fails at swi_fopen and writes
 *   ERR (-1). -1 is not > 0, so every later row runs the same code and writes the same -1, and the
 *   C side's carried-over value is indistinguishable from the .NET side's freshly-computed one.
 *
 * Put those two files in ephe-dir and that stops being true: row 1 would write 1 or 2 and
 * allocate dpsi/deps, row 2's swe_close() would free both arrays while leaving the > 0 marker in
 * place, and load_dpsi_deps would then return early without reallocating -- leaving the C side
 * claiming loaded EOP data it no longer has, against a .NET side that reloaded it. That is a real
 * asymmetry in this harness (arguably a latent defect in the C's own swe_close), so if this driver
 * is ever pointed at a directory carrying the EOP files, it needs a way to reset that field
 * between rows before the resulting diff can be read as a statement about the port.
 *
 * FRESH LIBRARY STATE PER ROW
 *
 * swe_houses_armc_ex2 keeps a hidden C static, saved_sundec (external/swisseph/swehouse.c:636),
 * that changes hsys 'I'/'i' results depending on what a PRIOR call computed (see
 * Tools/BaselineGen/Program.cs's header and SwissEphNet/CPort/SweHouse.cs). swe_close() does not
 * touch it and cannot: saved_sundec is a function-local static; swe_close() only resets fields of
 * swed. That does not currently bite: both drivers zero-initialize
 * ascmc, so ascmc[9] == 0 on every row, and swe_houses_armc_ex2's hsys 'I' branch only ever reads
 * saved_sundec when ascmc[9] == 99 (Astrodienst's documented "no Sun declination supplied"
 * signal) -- with ascmc[9] == 0, the function always takes the branch that WRITES saved_sundec,
 * never the one that reads a value carried over from a prior row. Every hsys 'I'/'i' row grid-
 * files.tsv contains (792 of them) takes the write branch, so this driver's C state and the
 * .NET side's per-instance state (saved_sundec is an instance field there, never shared across
 * calls) stay observably equivalent despite the difference in how each implements "fresh".
 * A future grid row that sets ascmc[9] = 99 on purpose would change that: it would make the C
 * side read whatever a prior row last wrote to saved_sundec, carrying state this driver has no
 * way to reset between rows, while the .NET side started that row with a clean instance -- the
 * two sides would disagree for a reason that has nothing to do with the port being compared.
 * Every grid-files.tsv row additionally depends on which segment of which file is currently
 * cached (free_planets, the fidat table); swe_close() does reset that (it is swed state), so it
 * runs before every row here, not just once at the end -- getting this wrong would make this
 * driver disagree with Tools/OracleDump/Program.cs (which constructs a fresh SwissEph instance,
 * and for grid-files.tsv rows, a fresh swe_set_ephe_path() call, per row) for a reason that has
 * nothing to do with the port being compared.
 *
 * OUTPUT COLUMN LAYOUT
 *
 * One line per data row, tab separated:
 *
 *   case_id, retc, err, then every double the row's func returns as a (decimal, hex) pair
 *
 * "every double the row's func returns" is fixed per func, not per row, so the column count for
 * a given func never depends on which house system, iflag or star name a particular row happens
 * to use:
 *
 *   CALC, CALC_UT                            xx[0..5]                 (6 doubles  -> 12 value columns)
 *   HOUSES, HOUSES_ARMC                      cusp[0..36], ascmc[0..9] (47 doubles -> 94 value columns)
 *   FIXSTAR, FIXSTAR_UT, FIXSTAR2, FIXSTAR2_UT  xx[0..5]              (6 doubles  -> 12 value columns)
 *   FIXSTAR_MAG                              mag                      (1 double   -> 2 value columns)
 *   GET_PLANET_NAME                          (none)                   (0 value columns)
 *   SOLCROSS, SOLCROSS_UT, MOONCROSS,
 *     MOONCROSS_UT, HELIO_CROSS,
 *     HELIO_CROSS_UT                         jd_cross                 (1 double   -> 2 value columns)
 *   MOONCROSS_NODE, MOONCROSS_NODE_UT        jd_cross, xlon, xla      (3 doubles  -> 6 value columns)
 *   AYANAMSA, AYANAMSA_EX, AYANAMSA_EX_UT    daya                     (1 double   -> 2 value columns)
 *
 * GET_PLANET_NAME has no value column at all: swe_get_planet_name returns a string, not a
 * double, so there is nothing to hex-encode. Its returned name is written into the err column
 * instead of a value column -- see gen-grid-files.ps1's header for why that column, specifically,
 * is the right one for it. AYANAMSA (plain swe_get_ayanamsa) has an err column too, but it is
 * always empty rather than repurposed: swe_get_ayanamsa has no serr output parameter and no error
 * signal of any kind, so there is nothing to write there -- see process_ayanamsa. HOUSES/
 * HOUSES_ARMC's cusp[0..36], not just cusp[1..12], is because
 * hsys 'G' (Gauquelin sectors) populates cusp[1..36] and a fixed column count keeps every func's
 * row mechanically the same width regardless of house system -- cusp[13..36] simply stay at
 * their zero-initialized default for every other system (matches Tools/BaselineMatrix/Houses.cs's
 * own reasoning for the same choice). retc/err come right after case_id, not after the doubles,
 * purely so a reader can see whether a row errored before scanning past however many value
 * columns that func has.
 *
 * THE CROSSING FUNCTIONS' retc COLUMN: ONE REAL, SIX SYNTHETIC
 *
 * swe_helio_cross(_ut) is the only one of the eight with a real int32 return code (OK/ERR); its
 * jd_cross output parameter is written only on the OK path (external/swisseph/sweph.c:8567,8613),
 * left untouched on every ERR return, so this driver zero-initializes it before the call -- an
 * ERR row's jd_cross column is then a deterministic 0.0 on both sides, not whatever happened to
 * be on each side's stack. The other six (swe_solcross/_ut, swe_mooncross/_ut,
 * swe_mooncross_node/_ut) return the crossing time itself as a double, with no int32 at all;
 * Astrodienst's own doc comment on each says errors are "indicated by returning a jd < jd_et [or
 * jd_ut]!" (external/swisseph/sweph.c:8319, 8353, 8387, 8421, 8454, 8491). This driver computes a
 * retc for those six itself -- ERR (-1) when the returned jd is less than the input jd, OK (0)
 * otherwise -- purely so the row still fits the shared "case_id, retc, err, values..." shape
 * every other func in this file already uses. Tools/OracleDump/Program.cs computes the identical
 * value from the identical returned bits, so this synthetic column can never disagree between the
 * two sides on its own. swe_mooncross_node(_ut)'s xlon/xla output parameters follow the same
 * zero-initialize-before-the-call rule as swe_helio_cross's jd_cross, for the same reason: they
 * are written only on the convergence path (external/swisseph/sweph.c:8480-8481, 8517-8518).
 *
 * Decimal columns (%.17g) are for a human reading the file; the hex columns are what a
 * comparison pass should actually diff; two decimal strings from two different printf/ToString
 * implementations are not guaranteed to render identically even when they represent the exact
 * same bits. swe_houses, swe_houses_armc and swe_get_planet_name have no error-string output
 * parameter at all, so their err column is either always empty (houses) or repurposed to carry
 * the return value itself (GET_PLANET_NAME) -- that is not a driver defect, the C API genuinely
 * has nothing else to report there.
 *
 * A malformed row (wrong column count, unparseable number, unknown func) is a hard failure: this
 * driver must not silently skip a row and emit fewer lines than the grid contains, which would
 * let a later comparison pass quietly run over a truncated set of cases.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdarg.h>
#include <errno.h>
#include "swephexp.h"

#define MAX_LINE 4096
#define ANALYTIC_COLUMNS 16
#define FILES_COLUMNS 14
#define CUSP_COUNT 37   /* cusp[0..36] */
#define ASCMC_COUNT 10  /* ascmc[0..9] */
#define STAR_BUF_LEN AS_MAXCH
/* Out-of-band retc for a crossing-func row emitted by a build with SWISSEPH_HAS_CROSSING
 * undefined (2.08) -- see this file's own top-of-file comment. Never a value swe_solcross and
 * friends (or this driver's own synthetic OK/ERR for them) could produce, so it cannot be
 * mistaken for a real result. */
#define NOT_IN_208_RETC (-9999)

/* Mode dispatches on which of these two headers the grid's first non-comment line matches --
 * see this file's own top-of-file comment. x2cross, dir, t0 and ayan_t0 are appended after
 * sid_mode in both headers, not interleaved among the original columns, so every column this
 * file's other process_* functions already index by a fixed offset keeps that same offset. t0/
 * ayan_t0 carry swe_set_sid_mode's own SE_SIDM_USER parameters -- see apply_sid_mode. */
static const char *EXPECTED_HEADER_ANALYTIC =
    "case_id\tfunc\tipl\ttjd\tiflag\thsys\tgeolon\tgeolat\theight\tarmc\teps\tsid_mode\tx2cross\tdir\tt0\tayan_t0";
static const char *EXPECTED_HEADER_FILES =
    "case_id\tfunc\tipl\ttjd\tiflag\tstar\tgeolon\tgeolat\theight\tsid_mode\tx2cross\tdir\tt0\tayan_t0";

enum grid_mode { MODE_ANALYTIC, MODE_FILES };

static void die(const char *fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    fprintf(stderr, "\n");
    exit(1);
}

static uint64_t bits_of(double x)
{
    uint64_t bits;
    memcpy(&bits, &x, sizeof bits);
    return bits;
}

static void rtrim(char *s)
{
    size_t len = strlen(s);
    while (len > 0 && (s[len - 1] == '\n' || s[len - 1] == '\r')) {
        s[--len] = '\0';
    }
}

/*
 * Splits line in place on tabs. Unlike strtok, this preserves empty fields between consecutive
 * tabs -- the grid relies on that to mean "this column does not apply to this row's func", and
 * silently collapsing "a\t\tb" into two fields instead of three would misalign every column
 * after the first empty one.
 *
 * Returns the total field count, which may exceed max_fields; fields beyond max_fields are not
 * written into the fields[] array (to avoid writing past its end), so the caller must check the
 * returned count against what it expects before indexing into fields[].
 */
static int split_fields(char *line, char *fields[], int max_fields)
{
    int count = 0;
    char *p = line;
    if (count < max_fields) fields[count] = p;
    count++;
    while (*p) {
        if (*p == '\t') {
            *p = '\0';
            if (count < max_fields) fields[count] = p + 1;
            count++;
        }
        p++;
    }
    return count;
}

static int has_value(const char *s)
{
    return s[0] != '\0';
}

static double parse_double(const char *s, const char *case_id, const char *col)
{
    char *end;
    double v;
    if (s[0] == '\0') die("missing required field '%s' at case %s", col, case_id);
    errno = 0;
    v = strtod(s, &end);
    if (end == s || *end != '\0') die("cannot parse '%s' as a double at case %s: '%s'", col, case_id, s);
    return v;
}

static long parse_int(const char *s, const char *case_id, const char *col)
{
    char *end;
    long v;
    if (s[0] == '\0') die("missing required field '%s' at case %s", col, case_id);
    errno = 0;
    v = strtol(s, &end, 10);
    if (end == s || *end != '\0') die("cannot parse '%s' as an int at case %s: '%s'", col, case_id, s);
    return v;
}

static int parse_hsys(const char *s, const char *case_id)
{
    if (s[0] == '\0' || s[1] != '\0') {
        die("hsys must be exactly one character at case %s: '%s'", case_id, s);
    }
    return (unsigned char)s[0];
}

static void emit_value(FILE *out, double v)
{
    fprintf(out, "\t%.17g\t%016llx", v, (unsigned long long)bits_of(v));
}

/* Mirrors Tools/OracleDump/Program.cs's EscapeErr and Tools/BaselineMatrix/Format.cs's S(): a
 * raw serr string could in principle contain a tab or newline and corrupt the TSV shape if
 * printed as-is. */
static void emit_escaped(FILE *out, const char *s)
{
    for (; *s; s++) {
        switch (*s) {
            case '\\': fputs("\\\\", out); break;
            case '\t': fputs("\\t", out); break;
            case '\r': fputs("\\r", out); break;
            case '\n': fputs("\\n", out); break;
            default:   fputc(*s, out);
        }
    }
}

/*
 * Applies swe_set_sid_mode when the row's sid_mode column is non-empty. t0/ayan_t0 (swe_set_sid_mode's
 * own SE_SIDM_USER parameters) always sit exactly 3 and 4 columns after sid_mode in both grids --
 * sid_mode, x2cross, dir, t0, ayan_t0, in that fixed relative order, for both the 16-column
 * analytic layout (sid_mode_idx 11) and the 14-column files layout (sid_mode_idx 9) -- see
 * gen-grid-analytic.ps1's and gen-grid-files.ps1's own header comments on why x2cross/dir/t0/
 * ayan_t0 are appended in that order rather than interleaved among the original columns. A row
 * with no sid_mode never reads t0/ayan_t0 at all: an empty sid_mode column means "this row's func
 * does not touch the sidereal frame" and t0/ayan_t0 mean nothing without it. An empty t0/ayan_t0
 * on a row that DOES set sid_mode means 0.0 -- the same default swe_set_sid_mode(sid_mode, 0, 0)
 * always passed before this driver could express SE_SIDM_USER at all.
 */
static void apply_sid_mode(char *fields[], const char *case_id, int sid_mode_idx)
{
    int32 sid_mode;
    double t0, ayan_t0;

    if (!has_value(fields[sid_mode_idx])) return;

    sid_mode = (int32)parse_int(fields[sid_mode_idx], case_id, "sid_mode");
    t0 = has_value(fields[sid_mode_idx + 3]) ? parse_double(fields[sid_mode_idx + 3], case_id, "t0") : 0.0;
    ayan_t0 = has_value(fields[sid_mode_idx + 4]) ? parse_double(fields[sid_mode_idx + 4], case_id, "ayan_t0") : 0.0;
    swe_set_sid_mode(sid_mode, t0, ayan_t0);
}

/*
 * MOONCROSS_NODE(_UT), HELIO_CROSS(_UT), the FIXSTAR family (FIXSTAR/FIXSTAR_UT/FIXSTAR2/
 * FIXSTAR2_UT) and HOUSES/HOUSES_ARMC never call apply_sid_mode: none of the C functions behind
 * them (swe_mooncross_node(_ut), swe_helio_cross(_ut), swe_fixstar(2)(_ut), swe_houses(_armc))
 * takes a sidereal-frame parameter at all in Astrodienst's own API, so there is nothing for this
 * driver to apply. Every grid row for these funcs is therefore expected to carry an empty
 * sid_mode column -- and today, every one of them does (verified: this guard has never fired
 * against Tools/OracleGrid/grid-analytic.tsv or grid-files.tsv).
 *
 * This hard-fails instead of silently ignoring a non-empty sid_mode, because "silently ignore
 * it" is exactly the failure mode that made this a blind spot in the first place: a future
 * sidereal MOONCROSS_NODE row would have both drivers ignore the column the same way, the row
 * would compare bit-identical between them, and the comparison would prove nothing about either
 * driver's (non-existent) sidereal handling for that func -- see this file's sibling check in
 * Tools/OracleDump/Program.cs's RefuseIfSidModeSet for the .NET side of the same guard.
 */
static void refuse_if_sid_mode_set(const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    if (has_value(fields[sid_mode_idx])) {
        die("%s: func '%s' has a non-empty sid_mode ('%s'), but this driver never calls "
            "apply_sid_mode for it -- %s has no sidereal-frame parameter in Astrodienst's C API. "
            "Either this row's sid_mode should be empty (a grid-generation defect), or "
            "apply_sid_mode needs to be wired up for this func (an API change this driver has not "
            "caught up with).",
            case_id, func, fields[sid_mode_idx], func);
    }
}

/*
 * Shared by both grids: the two column layouts agree on ipl/tjd/iflag/geolon/geolat/height at
 * fields[2..8], and only disagree on where sid_mode lives (analytic's 12-column layout carries
 * hsys/armc/eps between height and sid_mode; the 10-column files layout does not) -- sid_mode_idx
 * is the one difference the two callers below pass in.
 */
static void process_calc(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xx[6] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

    serr[0] = '\0';

    if (has_value(fields[6]) || has_value(fields[7]) || has_value(fields[8])) {
        double geolon = parse_double(fields[6], case_id, "geolon");
        double geolat = parse_double(fields[7], case_id, "geolat");
        double height = parse_double(fields[8], case_id, "height");
        swe_set_topo(geolon, geolat, height);
    }
    apply_sid_mode(fields, case_id, sid_mode_idx);

    if (strcmp(func, "CALC") == 0)
        retc = swe_calc(tjd, ipl, iflag, xx, serr);
    else
        retc = swe_calc_ut(tjd, ipl, iflag, xx, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xx[i]);
    fputc('\n', out);
}

/* grid-files.tsv only: star is fields[5], iflag always carries SEFLG_SWIEPH already OR-ed in by
 * gen-grid-files.ps1. swe_fixstar/swe_fixstar2 and their _ut variants can rewrite the star buffer
 * in place with the star's canonical name -- STAR_BUF_LEN gives that write plenty of room, and
 * this driver does not read the buffer back afterward, matching Tools/OracleDump/Program.cs (see
 * its own comment on the same point). */
static void process_fixstar(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
    char star[STAR_BUF_LEN];
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xx[6] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);

    strncpy(star, fields[5], sizeof star - 1);
    star[sizeof star - 1] = '\0';
    serr[0] = '\0';

    if (strcmp(func, "FIXSTAR") == 0)
        retc = swe_fixstar(star, tjd, iflag, xx, serr);
    else if (strcmp(func, "FIXSTAR_UT") == 0)
        retc = swe_fixstar_ut(star, tjd, iflag, xx, serr);
    else if (strcmp(func, "FIXSTAR2") == 0)
        retc = swe_fixstar2(star, tjd, iflag, xx, serr);
    else
        retc = swe_fixstar2_ut(star, tjd, iflag, xx, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    for (i = 0; i < 6; i++) emit_value(out, xx[i]);
    fputc('\n', out);
}

/* grid-files.tsv only: swe_fixstar_mag takes no date or flag, only the star search string. */
static void process_fixstar_mag(FILE *out, const char *case_id, char *fields[])
{
    char star[STAR_BUF_LEN];
    double mag = 0;
    char serr[AS_MAXCH];
    int retc;

    strncpy(star, fields[5], sizeof star - 1);
    star[sizeof star - 1] = '\0';
    serr[0] = '\0';

    retc = swe_fixstar_mag(star, &mag, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, mag);
    fputc('\n', out);
}

/* grid-files.tsv only: swe_get_planet_name returns a string, not a double -- see this file's own
 * top-of-file comment for why that string is written into the err column instead of a value
 * column, and gen-grid-files.ps1's header for the fuller rationale. retc is a fixed 0; the C API
 * has no error code to report here (swe_get_planet_name returns char *, never NULL). */
static void process_name(FILE *out, const char *case_id, char *fields[])
{
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    char name[STAR_BUF_LEN];

    name[0] = '\0';
    swe_get_planet_name(ipl, name);

    fprintf(out, "%s\t%d\t", case_id, 0);
    emit_escaped(out, name);
    fputc('\n', out);
}

static void process_houses(FILE *out, const char *case_id, char *fields[], int sid_mode_idx)
{
    double tjd, geolon, geolat;
    int hsys, retc, i;
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };

    refuse_if_sid_mode_set(case_id, "HOUSES", fields, sid_mode_idx);

    tjd = parse_double(fields[3], case_id, "tjd");
    hsys = parse_hsys(fields[5], case_id);
    geolon = parse_double(fields[6], case_id, "geolon");
    geolat = parse_double(fields[7], case_id, "geolat");

    retc = swe_houses(tjd, geolat, geolon, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

static void process_houses_armc(FILE *out, const char *case_id, char *fields[], int sid_mode_idx)
{
    double armc, eps, geolat;
    int hsys, retc, i;
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };

    refuse_if_sid_mode_set(case_id, "HOUSES_ARMC", fields, sid_mode_idx);

    armc = parse_double(fields[9], case_id, "armc");
    eps = parse_double(fields[10], case_id, "eps");
    hsys = parse_hsys(fields[5], case_id);
    geolat = parse_double(fields[7], case_id, "geolat");

    retc = swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses_armc */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

/*
 * SOLCROSS, SOLCROSS_UT, MOONCROSS, MOONCROSS_UT: all four share one C signature shape --
 * double f(double x2cross, double jd, int32 flag, char *serr) -- and one error convention, per
 * Astrodienst's own doc comment on each (external/swisseph/sweph.c:8319, 8353, 8387, 8421):
 * "Errors are indicated by returning a jd < jd_et [or jd_ut]!", not by a separate int return code
 * the way swe_calc/swe_helio_cross use. There is no int32 retc to report at all, so this driver
 * computes one itself -- see this file's own top-of-file comment, "THE CROSSING FUNCTIONS' retc
 * COLUMN". x2cross_idx is the one difference between the two grids (analytic carries armc/eps
 * before sid_mode; files does not), matching process_calc's own sid_mode_idx parameter for the
 * same reason.
 */
static void process_crossing_deg(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int x2cross_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    double x2cross = parse_double(fields[x2cross_idx], case_id, "x2cross");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double result;
    int retc;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, sid_mode_idx);

    if (strcmp(func, "SOLCROSS") == 0)
        result = swe_solcross(x2cross, tjd, iflag, serr);
    else if (strcmp(func, "SOLCROSS_UT") == 0)
        result = swe_solcross_ut(x2cross, tjd, iflag, serr);
    else if (strcmp(func, "MOONCROSS") == 0)
        result = swe_mooncross(x2cross, tjd, iflag, serr);
    else
        result = swe_mooncross_ut(x2cross, tjd, iflag, serr);

    retc = (result < tjd) ? ERR : OK;

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, result);
    fputc('\n', out);
#else
    /* swe_solcross/swe_mooncross(_ut) do not exist in 2.08 -- see this file's own top-of-file
     * comment on SWISSEPH_HAS_CROSSING. */
    char not_in_208_msg[AS_MAXCH];
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields; (void)sid_mode_idx; (void)x2cross_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * MOONCROSS_NODE, MOONCROSS_NODE_UT: same double-return, jd-less-than-input error convention as
 * process_crossing_deg above (external/swisseph/sweph.c:8454, 8491), plus two output parameters
 * (xlon, xla) this driver zero-initializes before the call -- see this file's own top-of-file
 * comment.
 */
static void process_mooncross_node(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double result, xlon = 0.0, xla = 0.0;
    int retc;

    /* Runs regardless of SWISSEPH_HAS_CROSSING (see the #else branch below for the other half of
     * this same call): a row's sid_mode column is a property of the grid row, not of which C
     * version this translation unit is linked against, so the guard applies the same way whether
     * this branch actually calls swe_mooncross_node(_ut) or the #else branch below takes the "not
     * in 2.08" sentinel path instead. Placed after this branch's own declarations, not before
     * them, to keep every declaration in this function preceding the first statement in its own
     * block -- this file targets a C89-safe subset throughout. */
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    serr[0] = '\0';

    if (strcmp(func, "MOONCROSS_NODE") == 0)
        result = swe_mooncross_node(tjd, iflag, &xlon, &xla, serr);
    else
        result = swe_mooncross_node_ut(tjd, iflag, &xlon, &xla, serr);

    retc = (result < tjd) ? ERR : OK;

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, result);
    emit_value(out, xlon);
    emit_value(out, xla);
    fputc('\n', out);
#else
    /* swe_mooncross_node(_ut) does not exist in 2.08 -- see this file's own top-of-file comment
     * on SWISSEPH_HAS_CROSSING. The sid_mode guard still runs here (see the #ifdef branch above
     * for why): a 2.08 build takes this sentinel path for every row regardless of sid_mode, but
     * the grid row itself is still expected to carry an empty sid_mode column, the same as a
     * 2.10.03 build would require. */
    char not_in_208_msg[AS_MAXCH];
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * HELIO_CROSS, HELIO_CROSS_UT: the one pair among these eight with a real int32 return code
 * (OK/ERR) and an output parameter (jd_cross) written only on the OK path -- see this file's own
 * top-of-file comment.
 */
static void process_helio_cross(FILE *out, const char *case_id, const char *func, char *fields[], int sid_mode_idx, int x2cross_idx, int dir_idx)
{
#ifdef SWISSEPH_HAS_CROSSING
    int ipl = (int)parse_int(fields[2], case_id, "ipl");
    double x2cross = parse_double(fields[x2cross_idx], case_id, "x2cross");
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    int dir = (int)parse_int(fields[dir_idx], case_id, "dir");
    char serr[AS_MAXCH];
    double jd_cross = 0.0;
    int32 retc;

    /* Runs regardless of SWISSEPH_HAS_CROSSING (see the #else branch below for the other half of
     * this same call) -- see process_mooncross_node's identical comment above for why. Placed
     * after this branch's own declarations to keep every declaration in this function preceding
     * the first statement in its own block. */
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    serr[0] = '\0';

    if (strcmp(func, "HELIO_CROSS") == 0)
        retc = swe_helio_cross(ipl, x2cross, tjd, iflag, dir, &jd_cross, serr);
    else
        retc = swe_helio_cross_ut(ipl, x2cross, tjd, iflag, dir, &jd_cross, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, jd_cross);
    fputc('\n', out);
#else
    /* swe_helio_cross(_ut) does not exist in 2.08 -- see this file's own top-of-file comment on
     * SWISSEPH_HAS_CROSSING. The sid_mode guard still runs here -- see process_mooncross_node's
     * identical #else comment above for why. */
    char not_in_208_msg[AS_MAXCH];
    refuse_if_sid_mode_set(case_id, func, fields, sid_mode_idx);
    sprintf(not_in_208_msg, "%s does not exist in Swiss Ephemeris 2.08", func);
    (void)fields; (void)x2cross_idx; (void)dir_idx;
    fprintf(out, "%s\t%d\t", case_id, NOT_IN_208_RETC);
    emit_escaped(out, not_in_208_msg);
    emit_value(out, 0.0);
    fputc('\n', out);
#endif
}

/*
 * AYANAMSA, AYANAMSA_EX, AYANAMSA_EX_UT: direct coverage of swe_get_ayanamsa/_ex/_ex_ut -- see
 * this file's own top-of-file comment. Analytic-grid only (sid_mode_idx is always 11, the
 * analytic grid's own fixed sid_mode column position): none of the three opens an ephemeris data
 * file, so these func tokens never appear in a grid-files.tsv row and this driver never needs to
 * handle them at any other sid_mode_idx.
 *
 * AYANAMSA has no serr output parameter -- swe_get_ayanamsa returns a bare double, with no error
 * signal at all -- so its retc is a fixed OK and its err column stays empty, the same convention
 * process_houses/process_houses_armc already use for a C API with nothing to report there.
 */
static void process_ayanamsa(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    double daya;

    apply_sid_mode(fields, case_id, 11);
    daya = swe_get_ayanamsa(tjd);

    fprintf(out, "%s\t%d\t", case_id, OK);
    emit_value(out, daya);
    fputc('\n', out);
}

static void process_ayanamsa_ex(FILE *out, const char *case_id, const char *func, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    char serr[AS_MAXCH];
    double daya = 0.0;
    int32 retc;

    serr[0] = '\0';
    apply_sid_mode(fields, case_id, 11);

    if (strcmp(func, "AYANAMSA_EX") == 0)
        retc = swe_get_ayanamsa_ex(tjd, iflag, &daya, serr);
    else
        retc = swe_get_ayanamsa_ex_ut(tjd, iflag, &daya, serr);

    fprintf(out, "%s\t%d\t", case_id, retc);
    emit_escaped(out, serr);
    emit_value(out, daya);
    fputc('\n', out);
}

int main(int argc, char **argv)
{
    FILE *in, *out;
    const char *ephe_dir = NULL;
    const char *jpl_file = NULL;
    char line[MAX_LINE];
    char buf[MAX_LINE];
    int header_seen = 0;
    enum grid_mode mode = MODE_ANALYTIC;
    int expected_columns = ANALYTIC_COLUMNS;
    long row_count = 0;

    if (argc < 3 || argc > 5) {
        fprintf(stderr, "Usage: sedump <grid.tsv> <output.tsv> [ephe-dir [jpl-file]]\n");
        return 1;
    }
    if (argc >= 4) ephe_dir = argv[3];
    if (argc == 5) jpl_file = argv[4];
    /* swe_set_jpl_file resolves its argument against swed.ephepath (sweph.c:1500), so a jpl-file
     * with no ephe-dir would resolve against whatever SE_EPHE_PATH or the compiled-in default
     * happens to be -- almost certainly not finding the file, and then silently falling back
     * through SEFLG_SWIEPH to Moshier on every row. Rejected here rather than left to produce a
     * run that looks fine and measures nothing. The argc parsing above cannot express it anyway
     * (argv[4] implies argv[3]); this guard is for an explicitly empty ephe-dir. */
    if (jpl_file != NULL && (ephe_dir == NULL || ephe_dir[0] == '\0')) {
        fprintf(stderr, "sedump: jpl-file was given but ephe-dir is empty; swe_set_jpl_file resolves against the ephemeris path, so both are required together.\n");
        return 1;
    }

    in = fopen(argv[1], "rb");
    if (!in) die("cannot open grid file %s", argv[1]);
    out = fopen(argv[2], "wb");
    if (!out) die("cannot open output file %s", argv[2]);

    while (fgets(line, sizeof line, in)) {
        char *fields[FILES_COLUMNS > ANALYTIC_COLUMNS ? FILES_COLUMNS : ANALYTIC_COLUMNS];
        int n;
        const char *case_id, *func;

        rtrim(line);
        if (line[0] == '\0') continue;
        if (line[0] == '#') continue;

        if (!header_seen) {
            if (strcmp(line, EXPECTED_HEADER_ANALYTIC) == 0) {
                mode = MODE_ANALYTIC;
                expected_columns = ANALYTIC_COLUMNS;
            } else if (strcmp(line, EXPECTED_HEADER_FILES) == 0) {
                mode = MODE_FILES;
                expected_columns = FILES_COLUMNS;
            } else {
                die("grid header does not match either header this driver expects.\n"
                    "analytic: %s\nfiles:    %s\ngot:      %s",
                    EXPECTED_HEADER_ANALYTIC, EXPECTED_HEADER_FILES, line);
            }
            header_seen = 1;
            continue;
        }

        strcpy(buf, line);
        n = split_fields(buf, fields, expected_columns);
        if (n != expected_columns) {
            die("row has %d column(s), expected %d: %s", n, expected_columns, line);
        }

        case_id = fields[0];
        func = fields[1];

        swe_close(); /* fresh library state before every row -- see header comment */
        if (ephe_dir != NULL) swe_set_ephe_path(ephe_dir); /* see INVOCATION in header comment */
        /* Strictly after swe_set_ephe_path, never before it -- see INVOCATION in the header
         * comment for what swapping the two would silently turn every SEFLG_JPLEPH row into. */
        if (jpl_file != NULL) swe_set_jpl_file(jpl_file);

        if (mode == MODE_ANALYTIC) {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 11);
            } else if (strcmp(func, "HOUSES") == 0) {
                process_houses(out, case_id, fields, 11);
            } else if (strcmp(func, "HOUSES_ARMC") == 0) {
                process_houses_armc(out, case_id, fields, 11);
            } else if (strcmp(func, "SOLCROSS") == 0 || strcmp(func, "SOLCROSS_UT") == 0
                       || strcmp(func, "MOONCROSS") == 0 || strcmp(func, "MOONCROSS_UT") == 0) {
                process_crossing_deg(out, case_id, func, fields, 11, 12);
            } else if (strcmp(func, "MOONCROSS_NODE") == 0 || strcmp(func, "MOONCROSS_NODE_UT") == 0) {
                process_mooncross_node(out, case_id, func, fields, 11);
            } else if (strcmp(func, "HELIO_CROSS") == 0 || strcmp(func, "HELIO_CROSS_UT") == 0) {
                process_helio_cross(out, case_id, func, fields, 11, 12, 13);
            } else if (strcmp(func, "AYANAMSA") == 0) {
                process_ayanamsa(out, case_id, fields);
            } else if (strcmp(func, "AYANAMSA_EX") == 0 || strcmp(func, "AYANAMSA_EX_UT") == 0) {
                process_ayanamsa_ex(out, case_id, func, fields);
            } else {
                die("unknown func '%s' at case %s", func, case_id);
            }
        } else {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 9);
            } else if (strcmp(func, "FIXSTAR") == 0 || strcmp(func, "FIXSTAR_UT") == 0
                       || strcmp(func, "FIXSTAR2") == 0 || strcmp(func, "FIXSTAR2_UT") == 0) {
                process_fixstar(out, case_id, func, fields, 9);
            } else if (strcmp(func, "FIXSTAR_MAG") == 0) {
                process_fixstar_mag(out, case_id, fields);
            } else if (strcmp(func, "GET_PLANET_NAME") == 0) {
                process_name(out, case_id, fields);
            } else if (strcmp(func, "SOLCROSS") == 0 || strcmp(func, "SOLCROSS_UT") == 0
                       || strcmp(func, "MOONCROSS") == 0 || strcmp(func, "MOONCROSS_UT") == 0) {
                process_crossing_deg(out, case_id, func, fields, 9, 10);
            } else if (strcmp(func, "MOONCROSS_NODE") == 0 || strcmp(func, "MOONCROSS_NODE_UT") == 0) {
                process_mooncross_node(out, case_id, func, fields, 9);
            } else if (strcmp(func, "HELIO_CROSS") == 0 || strcmp(func, "HELIO_CROSS_UT") == 0) {
                process_helio_cross(out, case_id, func, fields, 9, 10, 11);
            } else {
                die("unknown func '%s' at case %s", func, case_id);
            }
        }

        row_count++;
    }

    if (!header_seen) die("grid file %s had no header row", argv[1]);
    if (row_count == 0) die("grid file %s produced zero rows -- a run that processed nothing is not a pass", argv[1]);

    swe_close();
    fclose(in);
    fclose(out);
    fprintf(stderr, "sedump: wrote %ld row(s) to %s\n", row_count, argv[2]);
    return 0;
}
