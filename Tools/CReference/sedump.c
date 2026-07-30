/*
 * sedump.c -- the C side of the bit-exact comparison harness's first stage.
 *
 * Reads Tools/OracleGrid/grid-analytic.tsv (built by gen-grid-analytic.ps1; see that file's own
 * header for the column layout, the case for a shared grid, and why 'J' is absent from the
 * house-system letters) and replays every row against Astrodienst's own C, linked in as
 * libswe -- see scripts/run-oracle-dump.ps1, which builds this file and picks which .lib it
 * links against (2.10.03 by default, 2.08 for isolating transliteration defects from porting
 * differences). Tools/OracleDump/Program.cs is this file's .NET counterpart; the two must
 * produce output in the same shape for a later, separate pass to diff.
 *
 * INVOCATION
 *
 *   sedump.exe <grid.tsv> <output.tsv>
 *
 * FRESH LIBRARY STATE PER ROW
 *
 * swe_houses_armc keeps a hidden C static (saved_sundec) that changes hsys 'I'/'i' results
 * depending on what a PRIOR call computed (see Tools/BaselineGen/Program.cs's header and
 * SwissEphNet/CPort/SweHouse.cs). swe_close() resets that and every other piece of global state
 * libswe carries between calls, so it runs before every row here, not just once at the end --
 * getting this wrong would make this driver disagree with Tools/OracleDump/Program.cs (which
 * constructs a fresh SwissEph instance per row) for a reason that has nothing to do with the
 * port being compared.
 *
 * OUTPUT COLUMN LAYOUT
 *
 * One line per data row, tab separated:
 *
 *   case_id, retc, err, then every double the row's func returns as a (decimal, hex) pair
 *
 * "every double the row's func returns" is fixed per func, not per row, so the column count for
 * a given func never depends on which house system or iflag a particular row happens to use:
 *
 *   CALC, CALC_UT        xx[0..5]                 (6 doubles  -> 12 value columns)
 *   HOUSES, HOUSES_ARMC  cusp[0..36], ascmc[0..9]  (47 doubles -> 94 value columns)
 *
 * cusp[0..36], not just cusp[1..12], because hsys 'G' (Gauquelin sectors) populates
 * cusp[1..36] and a fixed column count keeps every func's row mechanically the same width
 * regardless of house system -- cusp[13..36] simply stay at their zero-initialized default for
 * every other system (matches Tools/BaselineMatrix/Houses.cs's own reasoning for the same
 * choice). retc/err come right after case_id, not after the doubles, purely so a reader can see
 * whether a row errored before scanning past however many value columns that func has.
 *
 * Decimal columns (%.17g) are for a human reading the file; the hex columns are what a
 * comparison pass should actually diff; two decimal strings from two different printf/ToString
 * implementations are not guaranteed to render identically even when they represent the exact
 * same bits. swe_houses and swe_houses_armc have no error-string output parameter at all, so
 * their err column is always empty -- that is not a driver defect, the C API genuinely has
 * nothing to report there.
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
#define EXPECTED_COLUMNS 12
#define CUSP_COUNT 37   /* cusp[0..36] */
#define ASCMC_COUNT 10  /* ascmc[0..9] */

static const char *EXPECTED_HEADER =
    "case_id\tfunc\tipl\ttjd\tiflag\thsys\tgeolon\tgeolat\theight\tarmc\teps\tsid_mode";

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

static void process_calc(FILE *out, const char *case_id, const char *func, char *fields[])
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
    if (has_value(fields[11])) {
        int32 sid_mode = (int32)parse_int(fields[11], case_id, "sid_mode");
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
    char line[MAX_LINE];
    char buf[MAX_LINE];
    int header_seen = 0;
    long row_count = 0;

    if (argc != 3) {
        fprintf(stderr, "Usage: sedump <grid.tsv> <output.tsv>\n");
        return 1;
    }

    in = fopen(argv[1], "rb");
    if (!in) die("cannot open grid file %s", argv[1]);
    out = fopen(argv[2], "wb");
    if (!out) die("cannot open output file %s", argv[2]);

    while (fgets(line, sizeof line, in)) {
        char *fields[EXPECTED_COLUMNS];
        int n;
        const char *case_id, *func;

        rtrim(line);
        if (line[0] == '\0') continue;
        if (line[0] == '#') continue;

        if (!header_seen) {
            if (strcmp(line, EXPECTED_HEADER) != 0) {
                die("grid header does not match what this driver expects.\nexpected: %s\ngot:      %s",
                    EXPECTED_HEADER, line);
            }
            header_seen = 1;
            continue;
        }

        strcpy(buf, line);
        n = split_fields(buf, fields, EXPECTED_COLUMNS);
        if (n != EXPECTED_COLUMNS) {
            die("row has %d column(s), expected %d: %s", n, EXPECTED_COLUMNS, line);
        }

        case_id = fields[0];
        func = fields[1];

        swe_close(); /* fresh library state before every row -- see header comment */

        if (strcmp(func, "CALC") == 0 || strcmp(func, "CALC_UT") == 0) {
            process_calc(out, case_id, func, fields);
        } else if (strcmp(func, "HOUSES") == 0) {
            process_houses(out, case_id, fields);
        } else if (strcmp(func, "HOUSES_ARMC") == 0) {
            process_houses_armc(out, case_id, fields);
        } else {
            die("unknown func '%s' at case %s", func, case_id);
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
