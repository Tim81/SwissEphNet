/*
 * sedump.c -- the C side of the bit-exact comparison harness.
 *
 * Replays two committed grids against Astrodienst's own C, linked in as libswe -- see
 * scripts/run-oracle-dump.ps1, which builds this file and picks which .lib it links against
 * (2.10.03 by default, 2.08 for isolating transliteration defects from porting differences).
 * Tools/OracleDump/Program.cs is this file's .NET counterpart; the two must produce output in
 * the same shape for a later, separate pass to diff.
 *
 *   Tools/OracleGrid/grid-analytic.tsv  -- swe_calc/swe_calc_ut (SEFLG_MOSEPH) and
 *                                          swe_houses/swe_houses_armc. Touches no ephemeris
 *                                          data file. See gen-grid-analytic.ps1's header.
 *   Tools/OracleGrid/grid-files.tsv     -- swe_calc/swe_calc_ut (SEFLG_SWIEPH), the
 *                                          swe_fixstar family, and swe_get_planet_name. Opens
 *                                          the shipped .se1/sefstars.txt files. See
 *                                          gen-grid-files.ps1's header.
 *
 * Both grids share one output shape (see OUTPUT COLUMN LAYOUT below) and one row-processing loop
 * in main(); which grid a given input file is dispatches on its header line, checked against
 * EXPECTED_HEADER_ANALYTIC and EXPECTED_HEADER_FILES below -- the two grids have different
 * column counts (12 vs 10), so a header mismatch is caught before any row is parsed.
 *
 * INVOCATION
 *
 *   sedump.exe <grid.tsv> <output.tsv> [ephe-dir]
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
 * FRESH LIBRARY STATE PER ROW
 *
 * swe_houses_armc keeps a hidden C static (saved_sundec) that changes hsys 'I'/'i' results
 * depending on what a PRIOR call computed (see Tools/BaselineGen/Program.cs's header and
 * SwissEphNet/CPort/SweHouse.cs). Every grid-files.tsv row additionally depends on which segment
 * of which file is currently cached (free_planets, the fidat table) -- both are exactly the kind
 * of state a "fresh instance per row" harness exists to neutralize. swe_close() resets all of it,
 * so it runs before every row here, not just once at the end -- getting this wrong would make
 * this driver disagree with Tools/OracleDump/Program.cs (which constructs a fresh SwissEph
 * instance, and for grid-files.tsv rows, a fresh OnLoadFile attachment, per row) for a reason
 * that has nothing to do with the port being compared.
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
 *
 * GET_PLANET_NAME has no value column at all: swe_get_planet_name returns a string, not a
 * double, so there is nothing to hex-encode. Its returned name is written into the err column
 * instead of a value column -- see gen-grid-files.ps1's header for why that column, specifically,
 * is the right one for it. HOUSES/HOUSES_ARMC's cusp[0..36], not just cusp[1..12], is because
 * hsys 'G' (Gauquelin sectors) populates cusp[1..36] and a fixed column count keeps every func's
 * row mechanically the same width regardless of house system -- cusp[13..36] simply stay at
 * their zero-initialized default for every other system (matches Tools/BaselineMatrix/Houses.cs's
 * own reasoning for the same choice). retc/err come right after case_id, not after the doubles,
 * purely so a reader can see whether a row errored before scanning past however many value
 * columns that func has.
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
#define ANALYTIC_COLUMNS 12
#define FILES_COLUMNS 10
#define CUSP_COUNT 37   /* cusp[0..36] */
#define ASCMC_COUNT 10  /* ascmc[0..9] */
#define STAR_BUF_LEN AS_MAXCH

/* Mode dispatches on which of these two headers the grid's first non-comment line matches --
 * see this file's own top-of-file comment. */
static const char *EXPECTED_HEADER_ANALYTIC =
    "case_id\tfunc\tipl\ttjd\tiflag\thsys\tgeolon\tgeolat\theight\tarmc\teps\tsid_mode";
static const char *EXPECTED_HEADER_FILES =
    "case_id\tfunc\tipl\ttjd\tiflag\tstar\tgeolon\tgeolat\theight\tsid_mode";

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
    if (has_value(fields[sid_mode_idx])) {
        int32 sid_mode = (int32)parse_int(fields[sid_mode_idx], case_id, "sid_mode");
        swe_set_sid_mode(sid_mode, 0, 0);
    }

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
static void process_fixstar(FILE *out, const char *case_id, const char *func, char *fields[])
{
    char star[STAR_BUF_LEN];
    double tjd = parse_double(fields[3], case_id, "tjd");
    int32 iflag = (int32)parse_int(fields[4], case_id, "iflag");
    double xx[6] = { 0 };
    char serr[AS_MAXCH];
    int retc, i;

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

static void process_houses(FILE *out, const char *case_id, char *fields[])
{
    double tjd = parse_double(fields[3], case_id, "tjd");
    int hsys = parse_hsys(fields[5], case_id);
    double geolon = parse_double(fields[6], case_id, "geolon");
    double geolat = parse_double(fields[7], case_id, "geolat");
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };
    int retc, i;

    retc = swe_houses(tjd, geolat, geolon, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

static void process_houses_armc(FILE *out, const char *case_id, char *fields[])
{
    double armc = parse_double(fields[9], case_id, "armc");
    double eps = parse_double(fields[10], case_id, "eps");
    int hsys = parse_hsys(fields[5], case_id);
    double geolat = parse_double(fields[7], case_id, "geolat");
    double cusp[40] = { 0 };
    double ascmc[10] = { 0 };
    int retc, i;

    retc = swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

    fprintf(out, "%s\t%d\t", case_id, retc); /* no serr param on swe_houses_armc */
    for (i = 0; i < CUSP_COUNT; i++) emit_value(out, cusp[i]);
    for (i = 0; i < ASCMC_COUNT; i++) emit_value(out, ascmc[i]);
    fputc('\n', out);
}

int main(int argc, char **argv)
{
    FILE *in, *out;
    const char *ephe_dir = NULL;
    char line[MAX_LINE];
    char buf[MAX_LINE];
    int header_seen = 0;
    enum grid_mode mode = MODE_ANALYTIC;
    int expected_columns = ANALYTIC_COLUMNS;
    long row_count = 0;

    if (argc != 3 && argc != 4) {
        fprintf(stderr, "Usage: sedump <grid.tsv> <output.tsv> [ephe-dir]\n");
        return 1;
    }
    if (argc == 4) ephe_dir = argv[3];

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

        if (mode == MODE_ANALYTIC) {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 11);
            } else if (strcmp(func, "HOUSES") == 0) {
                process_houses(out, case_id, fields);
            } else if (strcmp(func, "HOUSES_ARMC") == 0) {
                process_houses_armc(out, case_id, fields);
            } else {
                die("unknown func '%s' at case %s", func, case_id);
            }
        } else {
            if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
                process_calc(out, case_id, func, fields, 9);
            } else if (strcmp(func, "FIXSTAR") == 0 || strcmp(func, "FIXSTAR_UT") == 0
                       || strcmp(func, "FIXSTAR2") == 0 || strcmp(func, "FIXSTAR2_UT") == 0) {
                process_fixstar(out, case_id, func, fields);
            } else if (strcmp(func, "FIXSTAR_MAG") == 0) {
                process_fixstar_mag(out, case_id, fields);
            } else if (strcmp(func, "GET_PLANET_NAME") == 0) {
                process_name(out, case_id, fields);
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
