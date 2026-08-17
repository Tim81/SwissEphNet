/* Compare two dumps written by `house-speed-harness dump`.
 *
 * The first file is the baseline, the second the patched build. Positions,
 * return codes, error strings, ascmc and ascmc_speed must be identical; every
 * cusp speed that differs is counted and classified, per house system, so a
 * change can be attributed rather than merely noticed.
 *
 *   compare BASELINE PATCHED [maxprint] [--expect-apc-equator]
 *
 * Exit status: 0 the patched build changed nothing it must not change, 1 it
 * did, 2 the comparison could not be made at all (missing, empty, truncated,
 * mismatched, or the same file twice).
 *
 * WHY THERE IS AN ALLOWLIST RATHER THAN A TOLERANCE. The shipping stack
 * changes positions: swehouse-apc-equator-fix.patch rewrites apc_sector() and
 * moves 'Y' cusps, by 7e-12 degrees over most of the sample and by tens of
 * degrees within a nanodegree of the equator, which is the whole point of it.
 * A tolerance wide enough to absorb that would absorb an Ascendant five
 * degrees out as well. Instead every permitted difference has to name the
 * house letter it happens in, the region of the sample it happens in, and a
 * bound; anything that does not fit one of those descriptions fails, however
 * small it is. The classes are printed with their counts and caps every run,
 * so the allowlist cannot quietly grow.
 *
 * Without --expect-apc-equator nothing is allowed at all, which is the mode to
 * use when checking that a change was supposed to be inert.
 */
#include "harness.h"
#include <stdlib.h>

#define SENTINEL HARNESS_SENTINEL

/* -------------------------------------------------------------- allowlist --
 *
 * Caps measured on the shipping stack against pristine v2.10.3bfinal at
 * stride 1 and set with headroom; ../harness-README.md carries the measurement.
 * A cap is not a tolerance: a difference must ALSO match its class's shape.  */
#define CL_APC_ROUND    0
#define CL_APC_DEGEN    1
#define CL_DEGNORM_360  2
#define CL_GAUQ_FILL    3
#define CL_GAUQ_CLEAR   4
#define CL_POLE_NAN     5
#define CL_POLE_ROUND   6
#define CL_RAD_ASCSPD   7
#define CL_SID_ASCSPD   8
#define CL_N            9

/* WHY FIVE OF THE CAPS ARE PER DATE AND FOUR ARE ABSOLUTE. An absolute cap on
 * a class whose population scales with the sample is not a cap: 400,000 for
 * gauquelin-fill is 1.2x the stride-1 count and 24x the stride-2000 count, so
 * past about stride 700 the number could not be reached however broken the
 * build was. The five classes that scale are therefore capped per date used,
 * `CAP_x * harness_ndates_used(stride)`. The four that do not scale --
 * apc-degenerate, degnorm-360, pole-nan-both, pole-rounding -- live entirely
 * in the 396 degenerate rows, which are never subsampled, so their counts are
 * identical at every stride and an absolute cap is the right shape for them.
 *
 * Three of the five per-date caps are the class's STRUCTURAL ceiling rather
 * than a measured number with headroom, and this is worth being plain about:
 * radians-ascmc-speed reaches exactly 208 per date (26 letters x 8 slots) and
 * sidereal-ascmc-speed exactly 182 (26 x 7) on the clean stack, because every
 * one of those slots does change; gauquelin-fill reaches 39.9 of a possible 48
 * (24 sectors x 2 arrays) at stride 2000. For those the count cap can only
 * say "no more than every eligible slot" -- what actually gates them is the
 * SHAPE test each one carries, which is why the shapes below were tightened
 * at the same time. apc-rounding is the one with a real count margin. */

/* 'Y' positions move by about 4e-11 degrees wherever apc_sector()'s
 * denominator was merely rearranged. Measured 5,958 at stride 1, and a
 * worst-case 1.144 per date at stride 2000 with SEFLG_RADIANS. The magnitude
 * bound was 1e-9, three bins above the measured 4.007e-11 worst; at 1e-10 a
 * 'Y' position moving half a nanodegree is unexplained instead of absorbed. */
#define CAP_APC_ROUND_PER_DATE 4.0
#define BOUND_APC_ROUND_DEG 1e-10
/* At the equator and at the horizon-tangency latitude the old apc_sector() was
 * 0/0 and the new one is not, so the cusps genuinely move, by up to 180
 * degrees. That is the whole content of swehouse-apc-equator-fix.patch. Only
 * the 396 degenerate rows get close enough for it to show, and each row has 37
 * position slots, so the class cannot exceed 396*37 whatever happens. Any 'Y'
 * position moving more than 1e-9 degrees on a RANDOM row is still unexplained. */
#define CAP_APC_DEGEN_N      2000L
/* 360.0 and 0.0 are the same longitude. The patched tree normalises where the
 * baseline did not; the wrapped separation is exactly zero. */
#define CAP_DEGNORM_360_N     500L
/* 'G' 13..36 on a fallback row: the baseline never wrote them at all through
 * the armc entry point, and wrote ayanamsa-shifted stack through the sidereal
 * ones. Both are what swehouse-gauquelin-porphyry-fallback.patch is for.
 * 48 per date is every one of the 24 sectors in both arrays; 24 is every
 * sector in the position array, which is the only one gauquelin-clear counts.
 * Measured worst: 39.90 and 17.13 per date, both at stride 2000. */
#define CAP_GAUQ_FILL_PER_DATE  48.0
#define CAP_GAUQ_CLEAR_PER_DATE 24.0
/* Sunshine at a pole: NaN in both builds, upstream's, not the patches'. */
#define CAP_POLE_NAN_N       2000L
/* Exactly at a pole, cusps that are zero to within a rounding step come back
 * as a denormal-scale positive number in one build and as a literal zero in
 * the other. Measured worst 1.53e-14 degrees. Confined to the degenerate rows
 * AT the pole; the same shape anywhere else is still unexplained. */
#define CAP_POLE_ROUND_N      500L
#define BOUND_POLE_ROUND_DEG 1e-12
/* SEFLG_RADIANS: the baseline left both speed arrays in degrees. The patched
 * ascmc_speed must be EXACTLY the baseline's times DEGTORAD -- not merely
 * close to it, and not merely different. 208 per date is 26 letters x 8 slots,
 * which is what the clean stack reaches exactly. */
#define CAP_RAD_ASCSPD_PER_DATE 208.0
#define BOUND_RAD_REL       1e-15
/* Sidereal: the ayanamsa's rate, about 1e-4 degrees a day, is now subtracted
 * from ascmc_speed as well. armc (index 2) is not shifted and must not move.
 * 182 per date is 26 letters x 7 slots, again reached exactly.
 *
 * The bound used to be a flat 1e-3 deg/day, which is ten times the largest
 * rate the ayanamsa ever has on this sample: any offset up to a thousandth of
 * a degree a day was absorbed whether or not it had anything to do with an
 * ayanamsa. What is required now is the radians class's kind of statement --
 * that the difference IS the rate -- with two allowances. The relative one is
 * for the rate's own evaluation; the absolute one is for cancellation, since
 * the difference of two ascmc speeds that can reach 1e12 deg/day cannot
 * resolve 1e-4 better than the operands' own ULP. */
#define CAP_SID_ASCSPD_PER_DATE 182.0
#define BOUND_SID_RATE_REL  1e-6
#define BOUND_SID_RATE_ULP  8.0

typedef struct {
  long rows;
  long pos_diff, rc_diff, serr_diff, speed_diff;
  long ascmc_diff, ascmc_speed_diff;
  long pos_filled, speed_filled, pos_lost, speed_lost;
  /* Failure shapes the old max could not see. `d = fabs(a-b); if (d > 180)
   * d = 360 - d;` turns a 12,345 degree error into -11,985, which never
   * exceeds the running max, and fabs(NaN - x) is NaN, which never exceeds
   * anything either. Both reported as "max 0.000e+00". They are now counted
   * and carried in their own columns instead of being folded into a maximum
   * that cannot represent them. */
  long pos_nonfinite, speed_nonfinite;   /* one side non-finite, the other not */
  long pos_nan_both, speed_nan_both;     /* non-finite in BOTH builds          */
  long pos_wild, speed_wild;             /* difference beyond 360 deg          */
  /* Index 0 kept apart from 1..n. cusp_speed[0] is outside the documented
   * range, so folding a change there into speed_diff would leave it
   * indistinguishable from the millions of intended speed changes -- which is
   * only marginally better than the `c >= 1` guard that excluded it outright. */
  long pos0_diff, speed0_diff;
  double pos_max, speed_max_abs;
  int    pos_worst_cusp;
  long   pos_worst_row;
  /* Anything the allowlist did not explain. This, not pos_diff, is what the
   * exit status is built from once an allowlist is in force. */
  long unexplained;
} tally;

typedef struct {
  const char *name;
  const char *what;
  long count;
  long cap;
  double worst;
  /* The magnitude the class's shape implies, gated in its own right. A count
   * cap says how OFTEN a difference may be excused; it says nothing about how
   * far. Without this a class could stay under its cap while its worst walked
   * off the end of the table, which is exactly what happened to the sidereal
   * class: 1e-3 deg/day of slack, never once compared against anything.
   * BOUND_SHAPE_ONLY marks the classes whose statement is structural -- an
   * exact zero, an exact 360 -- and therefore has no magnitude to gate. */
  double bound;
} diffclass;

#define BOUND_SHAPE_ONLY (-1.0)

/* Angular separation of two cusp longitudes, in degrees, with the two shapes
 * the old expression swallowed made visible:
 *   kind 0  ordinary, wrapped into [0,180]
 *   kind 1  the raw gap exceeds 360 degrees, so at least one value is not a
 *           longitude at all; the RAW gap is returned, unwrapped, because
 *           wrapping it is what hid the defect
 *   kind 2  one side is not finite; no meaningful magnitude exists
 */
/* What swehouse-gauquelin-porphyry-fallback.patch actually promises for a
 * sector the baseline never wrote. `hi` is the top of the position range --
 * 360, or 2*pi under SEFLG_RADIANS -- and 0.0 for the speed array, where no
 * range statement is available and finiteness is all there is off an ERR row.
 *
 * On an ERR row the promise is not "something plausible" but "cleared", so
 * the test is a literal zero and nothing else passes it. */
static int gauq_fill_ok(double v, int32_t rc, double hi)
{
  if (rc < 0) return v == 0.0;
  /* Belt and braces only: reaching here needs the baseline to have left 13..36
   * unwritten on a row the patched build reports OK for, which is a return-code
   * difference, which already fails the row through rc_diff. The arm carries no
   * weight -- do not read the range test as the thing holding 'G' in bounds on
   * a successful row; there, both builds write the sectors and any difference
   * goes through the ordinary position path below. */
  if (!harness_finite(v)) return 0;
  if (hi > 0.0) return v >= 0.0 && v < hi;
  return 1;
}

/* The ayanamsa's rate at a date, in degrees per day: the quantity the sidereal
 * patch subtracts from ascmc_speed, formed the same way swehouse.c forms it
 * (a central difference at a step of 0.1 day). Returned so the sidereal class
 * can require the observed difference to BE this number rather than merely to
 * be small. The caller has already set the sidereal mode from the header. */
static int sid_rate_at(double jd, double *out)
{
  double tjde = jd + swe_deltat_ex(jd, -1, NULL);
  double aym, ayp;
  if (swe_get_ayanamsa_ex(tjde - 0.1, SEFLG_SIDEREAL, &aym, NULL) < 0) return 0;
  if (swe_get_ayanamsa_ex(tjde + 0.1, SEFLG_SIDEREAL, &ayp, NULL) < 0) return 0;
  *out = swe_difdeg2n(ayp, aym) / 0.2;
  return harness_finite(*out);
}

static double sepdeg(double a, double b, int *kind)
{
  double d;
  *kind = 0;
  if (!harness_finite(a) || !harness_finite(b)) { *kind = 2; return 0.0; }
  d = fabs(a - b);
  if (!(d >= 0.0)) { *kind = 2; return 0.0; }        /* NaN from inf - inf */
  if (d > 360.0) { *kind = 1; return d; }
  if (d > 180.0) d = 360.0 - d;
  return d;
}

static int read_row(FILE *fp, int32_t *rc, int32_t *nc, uint32_t *h,
                    double *cusp, double *cs, double *ascmc, double *as)
{
  int32_t hdr[2];
  if (fread(hdr, sizeof(int32_t), 2, fp) != 2) return 0;
  *rc = hdr[0]; *nc = hdr[1];
  if (*nc < 1 || *nc > HARNESS_MAXCUSP) return -1;
  if (fread(h, sizeof(uint32_t), 1, fp) != 1) return -1;
  if (fread(cusp, sizeof(double), *nc + 1, fp) != (size_t)(*nc + 1)) return -1;
  if (fread(cs, sizeof(double), *nc + 1, fp) != (size_t)(*nc + 1)) return -1;
  if (fread(ascmc, sizeof(double), 10, fp) != 10) return -1;
  if (fread(as, sizeof(double), 10, fp) != 10) return -1;
  return 1;
}

/* Read and validate a dump header. A dump that does not describe itself as the
 * same sample set, the same record layout, the same stride, the same flag set
 * and the same ephemeris is not comparable, and an empty file has no header at
 * all. */
static int read_header(const char *path, FILE *fp, harness_header *h)
{
  if (fread(h, sizeof *h, 1, fp) != 1) {
    fprintf(stderr, "%s: no dump header (empty or truncated file)\n", path);
    return 0;
  }
  if (h->magic != HARNESS_MAGIC) {
    fprintf(stderr, "%s: not a harness dump (magic %08lx, expected %08lx)\n",
            path, (unsigned long)h->magic, (unsigned long)HARNESS_MAGIC);
    return 0;
  }
  if (h->format != HARNESS_FORMAT) {
    fprintf(stderr, "%s: dump format %lu, this compare reads %lu\n",
            path, (unsigned long)h->format, (unsigned long)HARNESS_FORMAT);
    return 0;
  }
  if (h->seed != HARNESS_SEED || h->ndates != HARNESS_NDATES
   || h->ndegen != HARNESS_NDEGEN
   || h->nsys != HARNESS_NSYS || h->maxcusp != HARNESS_MAXCUSP
   || strncmp(h->systems, HARNESS_SYSTEMS, sizeof h->systems - 1) != 0) {
    fprintf(stderr, "%s: dump was made from a different sample set\n", path);
    return 0;
  }
  if (h->jd_lo != harness_jd_lo() || h->jd_hi != harness_jd_hi()) {
    fprintf(stderr, "%s: dump spans JD %.6f..%.6f, this build %.6f..%.6f\n",
            path, h->jd_lo, h->jd_hi, harness_jd_lo(), harness_jd_hi());
    return 0;
  }
  if (h->rows < 0) {
    fprintf(stderr, "%s: dump was never finalised (writer did not close it)\n", path);
    return 0;
  }
  if (h->rows != harness_expected_rows(h->stride, h->nsys)) {
    fprintf(stderr, "%s: header claims %ld rows, stride %d needs %ld\n",
            path, (long)h->rows, (int)h->stride,
            harness_expected_rows(h->stride, h->nsys));
    return 0;
  }
  if (h->rows == 0) {
    fprintf(stderr, "%s: dump contains no rows\n", path);
    return 0;
  }
  return 1;
}

/* Two dumps that are the same bytes are the same binary dumped twice, which is
 * the likeliest operator error there is and used to read as a perfect pass:
 * every field equal, "bit-identical", exit 0, nothing established. */
static int files_identical(const char *pa, const char *pb)
{
  FILE *a = fopen(pa, "rb"), *b = fopen(pb, "rb");
  char ba[65536], bb[65536];
  int same = 1;
  if (a == NULL || b == NULL) { if (a) fclose(a); if (b) fclose(b); return 0; }
  for (;;) {
    size_t na = fread(ba, 1, sizeof ba, a);
    size_t nb = fread(bb, 1, sizeof bb, b);
    if (na != nb || memcmp(ba, bb, na) != 0) { same = 0; break; }
    if (na == 0) break;
  }
  fclose(a); fclose(b);
  return same;
}

int main(int argc, char **argv)
{
  FILE *fa, *fb;
  harness_header ha_h, hb_h;
  harness_sample *S;
  double ca[HARNESS_MAXCUSP + 4], sa[HARNESS_MAXCUSP + 4], aa[12], asa[12];
  double cb[HARNESS_MAXCUSP + 4], sb[HARNESS_MAXCUSP + 4], ab[12], asb[12];
  int32_t rca, rcb, nca, ncb;
  uint32_t ha, hb;
  tally t[HARNESS_NSYS];
  diffclass cl[CL_N];
  /* Two budgets, not one. With a single counter the allowed differences --
   * hundreds of thousands of them, and they come first -- consumed the whole
   * of it before the first unexplained line was reached, so a FAILED run could
   * print twenty perfectly fine "[allowed]" examples and not one of the
   * differences it failed for. */
  long row = 0, printed_ok = 0, printed_bad = 0, maxprint;
  long unexplained_total = 0, class_over = 0, bound_over = 0;
  long sid_rate_missing = 0;
  double sid_rate = 0;
  int sid_rate_ok = 0, sid_rate_i = -1;
  int nd_used;
  double pos_hi;
  int k, c, r, i, allow = 0, argi;

  if (argc < 3) {
    fprintf(stderr, "usage: compare BASELINE PATCHED [maxprint] [--expect-apc-equator]\n");
    return 2;
  }
  /* The sidereal class evaluates the ayanamsa's rate, so this program now asks
   * the library a question of its own and has to ask it the same way the
   * harness does. Same call, same place in the sequence. */
  swe_set_ephe_path(NULL);
  if (sizeof(harness_header) != HARNESS_HEADER_BYTES) {
    fprintf(stderr, "compare: header is %d bytes, expected %d\n",
            (int)sizeof(harness_header), HARNESS_HEADER_BYTES);
    return 2;
  }
  maxprint = 20;
  for (argi = 3; argi < argc; argi++) {
    if (strcmp(argv[argi], "--expect-apc-equator") == 0) allow = 1;
    else {
      char *end = NULL;
      long v = strtol(argv[argi], &end, 10);
      if (end == argv[argi] || *end != '\0' || v < 0) {
        fprintf(stderr, "compare: unrecognised argument \"%s\"\n", argv[argi]);
        return 2;
      }
      maxprint = v;
    }
  }
  if (strcmp(argv[1], argv[2]) == 0) {
    fprintf(stderr, "compare: both arguments name %s; a file cannot be its own baseline\n",
            argv[1]);
    return 2;
  }
  if (files_identical(argv[1], argv[2])) {
    fprintf(stderr, "compare: %s and %s are byte for byte the same file.\n"
                    "Two identical dumps compare perfectly and establish nothing;"
                    " this is almost always\nthe same binary dumped twice.\n",
            argv[1], argv[2]);
    return 2;
  }
  fa = fopen(argv[1], "rb");
  if (!fa) { fprintf(stderr, "cannot open %s: %s\n", argv[1], strerror(errno)); return 2; }
  fb = fopen(argv[2], "rb");
  if (!fb) { fprintf(stderr, "cannot open %s: %s\n", argv[2], strerror(errno)); return 2; }
  if (!read_header(argv[1], fa, &ha_h)) return 2;
  if (!read_header(argv[2], fb, &hb_h)) return 2;
  if (ha_h.stride != hb_h.stride) {
    fprintf(stderr, "stride differs: baseline %d, patched %d; the two dumps do not\n"
                    "cover the same dates and cannot be compared\n",
            (int)ha_h.stride, (int)hb_h.stride);
    return 2;
  }
  if (ha_h.iflag != hb_h.iflag || ha_h.sid_mode != hb_h.sid_mode) {
    fprintf(stderr, "flag sets differ: baseline iflag %lu sid_mode %ld,"
                    " patched iflag %lu sid_mode %ld\n",
            (unsigned long)ha_h.iflag, (long)ha_h.sid_mode,
            (unsigned long)hb_h.iflag, (long)hb_h.sid_mode);
    return 2;
  }
  /* An ephemeris difference is a data difference and would be attributed to
   * the patch. Measured: 208 of 1,664 rows move between a build that finds
   * the Swiss files and one that falls back to Moshier. */
  if (ha_h.ephe_fp != hb_h.ephe_fp) {
    fprintf(stderr, "ephemeris fingerprints differ: baseline %016llX, patched %016llX.\n"
                    "The two dumps were answered by different data, so any difference"
                    " between them\nis not attributable to the patch. Set the same"
                    " SE_EPHE_PATH for both and dump again.\n",
            (unsigned long long)ha_h.ephe_fp, (unsigned long long)hb_h.ephe_fp);
    return 2;
  }
  if (ha_h.rows != hb_h.rows) {
    fprintf(stderr, "row counts differ: baseline %ld, patched %ld\n",
            (long)ha_h.rows, (long)hb_h.rows);
    return 2;
  }
  memset(t, 0, sizeof(t));
  memset(cl, 0, sizeof(cl));
  nd_used = harness_ndates_used((int)ha_h.stride);
  cl[CL_APC_ROUND].name   = "apc-rounding";
  cl[CL_APC_ROUND].what   = "'Y' position, |d| <= the bound (1e-10 deg, scaled if radians)";
  cl[CL_APC_ROUND].cap    = (long)(CAP_APC_ROUND_PER_DATE * nd_used);
  cl[CL_APC_ROUND].bound  = BOUND_APC_ROUND_DEG;
  cl[CL_APC_DEGEN].name   = "apc-degenerate";
  cl[CL_APC_DEGEN].what   = "'Y' position on a degenerate row, |d| <= half a turn";
  cl[CL_APC_DEGEN].cap    = CAP_APC_DEGEN_N;
  cl[CL_APC_DEGEN].bound  = 180.0;
  cl[CL_DEGNORM_360].name = "degnorm-360";
  cl[CL_DEGNORM_360].what = "position, raw gap exactly 360 deg (0 and 360 are one angle)";
  cl[CL_DEGNORM_360].cap  = CAP_DEGNORM_360_N;
  cl[CL_DEGNORM_360].bound = BOUND_SHAPE_ONLY;
  cl[CL_GAUQ_FILL].name   = "gauquelin-fill";
  cl[CL_GAUQ_FILL].what   = "'G' 13..36 unwritten in baseline; ERR row => exactly 0, else in range";
  cl[CL_GAUQ_FILL].cap    = (long)(CAP_GAUQ_FILL_PER_DATE * nd_used);
  cl[CL_GAUQ_FILL].bound  = BOUND_SHAPE_ONLY;
  cl[CL_GAUQ_CLEAR].name  = "gauquelin-clear";
  cl[CL_GAUQ_CLEAR].what  = "'G' index 13..36 on an ERR row, patched writes exactly 0";
  cl[CL_GAUQ_CLEAR].cap   = (long)(CAP_GAUQ_CLEAR_PER_DATE * nd_used);
  cl[CL_GAUQ_CLEAR].bound = BOUND_SHAPE_ONLY;
  cl[CL_POLE_NAN].name    = "pole-nan-both";
  cl[CL_POLE_NAN].what    = "'I' within 1e-10 deg of +-90, non-finite in BOTH builds";
  cl[CL_POLE_NAN].cap     = CAP_POLE_NAN_N;
  cl[CL_POLE_NAN].bound   = BOUND_SHAPE_ONLY;
  cl[CL_POLE_ROUND].name  = "pole-rounding";
  cl[CL_POLE_ROUND].what  = "position at geolat exactly +-90, |d| <= the bound (1e-12 deg)";
  cl[CL_POLE_ROUND].cap   = CAP_POLE_ROUND_N;
  cl[CL_POLE_ROUND].bound = BOUND_POLE_ROUND_DEG;
  cl[CL_RAD_ASCSPD].name  = "radians-ascmc-speed";
  cl[CL_RAD_ASCSPD].what  = "SEFLG_RADIANS only: ascmc_speed[0..7] == baseline * DEGTORAD";
  cl[CL_RAD_ASCSPD].cap   = (long)(CAP_RAD_ASCSPD_PER_DATE * nd_used);
  cl[CL_RAD_ASCSPD].bound = BOUND_RAD_REL;
  cl[CL_SID_ASCSPD].name  = "sidereal-ascmc-speed";
  cl[CL_SID_ASCSPD].what  = "sidereal: ascmc_speed[!=2] moved BY the ayanamsa's rate"
                            " (worst = fraction of the allowance used)";
  cl[CL_SID_ASCSPD].cap   = (long)(CAP_SID_ASCSPD_PER_DATE * nd_used);
  cl[CL_SID_ASCSPD].bound = 1.0;
  if (ha_h.sid_mode >= 0) swe_set_sid_mode(ha_h.sid_mode, 0, 0);
  pos_hi = (ha_h.iflag & (uint32_t)SEFLG_RADIANS) ? 2.0 * 3.14159265358979323846 : 360.0;
  /* Every positional bound above is stated in degrees, and under SEFLG_RADIANS
   * the dump holds radians, so an unscaled bound is 57.3 times looser there
   * than it is anywhere else. Measured: a 'Y' cusp moved by 5e-10 degrees is
   * unexplained on four flag sets and absorbed by apc-rounding on the fifth
   * for exactly that reason. The two position bounds are converted here so
   * that the class means the same thing in every flag set. */
  if (ha_h.iflag & (uint32_t)SEFLG_RADIANS) {
    cl[CL_APC_ROUND].bound  *= DEGTORAD;
    cl[CL_APC_DEGEN].bound  *= DEGTORAD;
    cl[CL_POLE_ROUND].bound *= DEGTORAD;
  }

  S = (harness_sample *)malloc(sizeof(harness_sample) * HARNESS_NDATES);
  if (S == NULL) { fprintf(stderr, "compare: out of memory\n"); return 2; }
  harness_samples(S, HARNESS_NDATES);

  while ((r = read_row(fa, &rca, &nca, &ha, ca, sa, aa, asa)) == 1) {
    int rb = read_row(fb, &rcb, &ncb, &hb, cb, sb, ab, asb);
    double geolat;
    int hs;
    if (rb != 1) {
      fprintf(stderr, "patched dump %s at row %ld\n",
              rb == 0 ? "ended early" : "is corrupt", row);
      return 2;
    }
    k = (int)(row % HARNESS_NSYS);
    hs = (unsigned char)HARNESS_SYSTEMS[k];
    /* The sample is regenerated here, so a difference can be reported with the
     * latitude it happened at and an allowlist class can be about a REGION of
     * the sample rather than only about a magnitude. */
    i = harness_date_at((int)(row / HARNESS_NSYS), (int)ha_h.stride);
    geolat = S[i].geolat;
    /* The ayanamsa's rate for this date, computed once per date rather than
     * once per row: the 26 letters of a date share it. */
    if (allow && ha_h.sid_mode >= 0 && i != sid_rate_i) {
      sid_rate_i = i;
      sid_rate_ok = sid_rate_at(S[i].jd, &sid_rate);
      if (!sid_rate_ok) sid_rate_missing++;
    }
    t[k].rows++;
    if (rca != rcb) { t[k].rc_diff++; t[k].unexplained++; }
    if (ha != hb)  { t[k].serr_diff++; t[k].unexplained++; }
    if (nca != ncb) { fprintf(stderr, "cusp count changed at row %ld\n", row); return 2; }

    for (c = 0; c <= nca; c++) {
      /* Bitwise identity, not `!=`. A slot that is NaN in BOTH builds is
       * unchanged; `sa[c] != sb[c]` called it a difference forever, inflating
       * the counts and forcing exit 1 with a maximum of zero. */
      if (!harness_same(ca[c], cb[c])) {
        if (ca[c] == SENTINEL) {
          t[k].pos_filled++;
          /* "finite" was the whole test, so the patch could have written any
           * number at all into the 24 sectors it exists to fill and this class
           * would have absorbed it. What the patch actually promises is
           * narrower: on an ERR row the sectors are CLEARED, to a literal
           * zero, and on any other row they are longitudes, so they lie in
           * [0,360) -- or [0,2pi) when the caller asked for radians. */
          if (allow && hs == 'G' && c >= 13 && c <= 36
           && gauq_fill_ok(cb[c], rcb, pos_hi))
            cl[CL_GAUQ_FILL].count++;
          else t[k].unexplained++;
        } else if (cb[c] == SENTINEL) {
          t[k].pos_lost++;
          t[k].unexplained++;
        } else {
          int kind, ok = 0;
          double d = sepdeg(ca[c], cb[c], &kind);
          t[k].pos_diff++;
          if (c == 0) t[k].pos0_diff++;
          if (kind == 2) t[k].pos_nonfinite++;
          else {
            if (kind == 1) t[k].pos_wild++;
            if (d > t[k].pos_max) {
              t[k].pos_max = d; t[k].pos_worst_cusp = c; t[k].pos_worst_row = row;
            }
          }
          /* Not restricted to kind 0: under SEFLG_RADIANS the baseline scales
           * the untouched 13..36 by DEGTORAD, so the sentinel arrives as
           * -1.55e87 rather than as itself and the "never wrote it" test above
           * misses. What matters is the shape -- an ERR row, sectors 13 to 36,
           * and a patched value of exactly zero -- not the magnitude of a
           * difference against uninitialised stack. */
          if (allow && kind != 2 && hs == 'G' && c >= 13 && c <= 36
           && cb[c] == 0.0 && rcb < 0) {
            cl[CL_GAUQ_CLEAR].count++;
            if (d > cl[CL_GAUQ_CLEAR].worst) cl[CL_GAUQ_CLEAR].worst = d;
            ok = 1;
          } else if (allow && kind == 0 && c >= 1
                  && S[i].fixed && fabs(geolat) >= 90.0
                  && d <= cl[CL_POLE_ROUND].bound) {
            cl[CL_POLE_ROUND].count++;
            if (d > cl[CL_POLE_ROUND].worst) cl[CL_POLE_ROUND].worst = d;
            ok = 1;
          } else if (allow && kind == 0 && c >= 1) {
            if (fabs(ca[c] - cb[c]) == 360.0) {
              cl[CL_DEGNORM_360].count++;
              ok = 1;
            } else if (hs == 'Y' && d <= cl[CL_APC_ROUND].bound) {
              cl[CL_APC_ROUND].count++;
              if (d > cl[CL_APC_ROUND].worst) cl[CL_APC_ROUND].worst = d;
              ok = 1;
            } else if (hs == 'Y' && S[i].fixed && d <= cl[CL_APC_DEGEN].bound) {
              cl[CL_APC_DEGEN].count++;
              if (d > cl[CL_APC_DEGEN].worst) cl[CL_APC_DEGEN].worst = d;
              ok = 1;
            }
          }
          if (!ok) t[k].unexplained++;
          if (ok ? (printed_ok < maxprint) : (printed_bad < maxprint)) {
            printf("POSITION %c cusp %2d row %ld geolat %.11f  %.17g -> %.17g  (%s%.3e deg)%s\n",
                   HARNESS_SYSTEMS[k], c, row, geolat, ca[c], cb[c],
                   kind == 2 ? "NON-FINITE, " : kind == 1 ? "OUT OF RANGE, raw " : "",
                   d, ok ? "  [allowed]" : "  <-- UNEXPLAINED");
            if (ok) printed_ok++; else printed_bad++;
          }
        }
      } else if (!harness_finite(ca[c]) && ca[c] != SENTINEL) {
        t[k].pos_nan_both++;   /* identical, but identical garbage */
        if (allow && harness_pole_nan_waived(hs, geolat)) cl[CL_POLE_NAN].count++;
        else t[k].unexplained++;
      }

      /* Index 0 included. cusp_speed[0] was written by the dump and then
       * excluded from the comparison by a `c >= 1` guard, so anything the
       * patch did to it was invisible here. */
      if (!harness_same(sa[c], sb[c])) {
        if (sa[c] == SENTINEL) {
          t[k].speed_filled++;
          /* Same shape, minus the range: a speed is not a longitude, so the
           * only positive statement available off an ERR row is finiteness.
           * On an ERR row the clearing to zero is asserted exactly as for the
           * positions -- which is where 42.0 in the speed array is caught. */
          if (allow && hs == 'G' && c >= 13 && c <= 36
           && gauq_fill_ok(sb[c], rcb, 0.0))
            cl[CL_GAUQ_FILL].count++;
          else t[k].unexplained++;
        } else if (sb[c] == SENTINEL) {
          t[k].speed_lost++;
          t[k].unexplained++;
        } else {
          double d;
          t[k].speed_diff++;
          if (c == 0) { t[k].speed0_diff++; t[k].unexplained++; }
          if (!harness_finite(sa[c]) || !harness_finite(sb[c])) {
            t[k].speed_nonfinite++;
            t[k].unexplained++;
          } else {
            d = fabs(sa[c] - sb[c]);
            if (!(d >= 0.0)) { t[k].speed_nonfinite++; t[k].unexplained++; }
            else {
              /* Cusp speeds legitimately reach 1e6 deg/day near a singularity,
               * so "wild" here is only the frankly impossible. Informational;
               * it is not part of the exit status. Changing a cusp SPEED is
               * what this patch set is for, so a speed difference is never
               * unexplained by itself. */
              if (d > 1e9) t[k].speed_wild++;
              if (d > t[k].speed_max_abs) t[k].speed_max_abs = d;
            }
          }
        }
      } else if (!harness_finite(sa[c]) && sa[c] != SENTINEL) {
        t[k].speed_nan_both++;
        if (allow && harness_pole_nan_waived(hs, geolat)) cl[CL_POLE_NAN].count++;
        else t[k].unexplained++;
      }
    }
    /* ascmc and ascmc_speed. These were counted and printed and then LEFT OUT
     * of the exit expression, so an Ascendant five degrees wrong printed
     * "positions differing: 0 (bit-identical)" and exited 0. */
    for (c = 0; c < 10; c++) {
      if (!harness_same(aa[c], ab[c])) {
        int both_nan = !harness_finite(aa[c]) && !harness_finite(ab[c]);
        int ok = allow && both_nan && harness_pole_nan_waived(hs, geolat);
        t[k].ascmc_diff++;
        if (!ok) t[k].unexplained++;
        if (!ok && printed_bad < maxprint) {
          printf("ASCMC %c index %d row %ld geolat %.11f  %.17g -> %.17g  <-- UNEXPLAINED\n",
                 HARNESS_SYSTEMS[k], c, row, geolat, aa[c], ab[c]);
          printed_bad++;
        }
      }
      if (!harness_same(asa[c], asb[c])) {
        int both_nan = !harness_finite(asa[c]) && !harness_finite(asb[c]);
        int ok = 0;
        t[k].ascmc_speed_diff++;
        if (allow && both_nan && harness_pole_nan_waived(hs, geolat)) ok = 1;
        /* SEFLG_RADIANS: the baseline left ascmc_speed in degrees. The patched
         * value has to be EXACTLY the baseline's times DEGTORAD; a factor that
         * is merely close, or a factor of two out, is not this class. */
        if (!ok && allow && (ha_h.iflag & (uint32_t)SEFLG_RADIANS)
         && c < 8 && harness_finite(asa[c]) && harness_finite(asb[c])) {
          double want = asa[c] * DEGTORAD;
          double s = fabs(want) > 1.0 ? fabs(want) : 1.0;
          double rel = fabs(asb[c] - want) / s;
          if (rel <= BOUND_RAD_REL) {
            cl[CL_RAD_ASCSPD].count++;
            if (rel > cl[CL_RAD_ASCSPD].worst) cl[CL_RAD_ASCSPD].worst = rel;
            ok = 1;
          }
        }
        /* Sidereal: the ayanamsa's rate is now subtracted from ascmc_speed too.
         * armc, index 2, is not shifted and must not have moved.
         *
         * The test is that the difference IS the rate, not that it is small.
         * A flat bound of 1e-3 deg/day was ten times the largest rate this
         * sample ever produces, so half a thousandth of a degree a day added
         * to the Ascendant's speed -- nothing to do with any ayanamsa -- sat
         * inside it. The two allowances are the rate's own evaluation error
         * (relative) and the cancellation floor of subtracting two speeds that
         * can reach 1e12 deg/day (absolute, in ULPs of the operands). */
        if (!ok && allow && ha_h.sid_mode >= 0 && c != 2 && c < 8
         && harness_finite(asa[c]) && harness_finite(asb[c]) && sid_rate_ok) {
          double dd = asa[c] - asb[c];
          double mag = fabs(asa[c]) > fabs(asb[c]) ? fabs(asa[c]) : fabs(asb[c]);
          double slack = BOUND_SID_RATE_REL * fabs(sid_rate)
                       + BOUND_SID_RATE_ULP * 2.220446049250313e-16 * mag;
          double miss = fabs(dd - sid_rate);
          /* Reported as the FRACTION of the allowance used, so the number in
           * the table is directly comparable with the bound beside it whether
           * the relative term or the cancellation term is the binding one. */
          double used = slack > 0.0 ? miss / slack : (miss > 0.0 ? 1e300 : 0.0);
          if (miss <= slack) {
            cl[CL_SID_ASCSPD].count++;
            if (used > cl[CL_SID_ASCSPD].worst) cl[CL_SID_ASCSPD].worst = used;
            ok = 1;
          }
        }
        if (!ok) t[k].unexplained++;
        if (!ok && printed_bad < maxprint) {
          printf("ASCMC_SPEED %c index %d row %ld geolat %.11f  %.17g -> %.17g"
                 "  <-- UNEXPLAINED\n",
                 HARNESS_SYSTEMS[k], c, row, geolat, asa[c], asb[c]);
          printed_bad++;
        }
      }
    }
    row++;
  }
  if (r < 0) { fprintf(stderr, "baseline dump is corrupt at row %ld\n", row); return 2; }
  /* The baseline ran out first, so anything still readable in the patched file
   * is trailing data the loop never compared. The message used to name the
   * baseline, which is the one file this branch says nothing about. */
  if (read_row(fb, &rcb, &ncb, &hb, cb, sb, ab, asb) != 0) {
    fprintf(stderr, "patched dump %s continues past row %ld, where the baseline ended\n",
            argv[2], row);
    return 2;
  }

  /* The count the old version never asserted. Without it an empty pair of
   * files, or a pair truncated to the same length, sailed through as a pass. */
  if (row != (long)ha_h.rows) {
    fprintf(stderr, "read %ld rows, headers promised %ld\n", row, (long)ha_h.rows);
    return 2;
  }

  {
    long tp = 0, ts = 0, trc = 0, te = 0, ta = 0, tas = 0, tr = 0;
    long tpf = 0, tsf = 0, tpl = 0, tsl = 0;
    long tpnf = 0, tsnf = 0, tpnb = 0, tsnb = 0, tpw = 0, tsw = 0;
    long tp0 = 0, ts0 = 0;
    printf("\nhsys   rows   pos_diff  max_pos_deg   speed_diff  max_speed_delta"
           "   rc  serr   ascmc  asp   pos_filled  spd_filled  pos_nonfin  spd_nonfin"
           "  pos_wild  spd_wild  nan_both  unexplained\n");
    for (k = 0; k < HARNESS_NSYS; k++) {
      printf("  %c  %7ld  %9ld  %11.3e  %11ld  %15.4f %4ld %5ld %7ld %4ld %12ld %11ld"
             " %11ld %11ld %9ld %9ld %9ld %12ld\n",
             HARNESS_SYSTEMS[k], t[k].rows, t[k].pos_diff, t[k].pos_max,
             t[k].speed_diff, t[k].speed_max_abs, t[k].rc_diff, t[k].serr_diff,
             t[k].ascmc_diff, t[k].ascmc_speed_diff,
             t[k].pos_filled, t[k].speed_filled,
             t[k].pos_nonfinite, t[k].speed_nonfinite,
             t[k].pos_wild, t[k].speed_wild,
             t[k].pos_nan_both + t[k].speed_nan_both,
             t[k].unexplained);
      tp += t[k].pos_diff; ts += t[k].speed_diff; trc += t[k].rc_diff;
      te += t[k].serr_diff; ta += t[k].ascmc_diff; tas += t[k].ascmc_speed_diff;
      tpf += t[k].pos_filled; tsf += t[k].speed_filled;
      tpl += t[k].pos_lost; tsl += t[k].speed_lost;
      tpnf += t[k].pos_nonfinite; tsnf += t[k].speed_nonfinite;
      tpnb += t[k].pos_nan_both; tsnb += t[k].speed_nan_both;
      tpw += t[k].pos_wild; tsw += t[k].speed_wild;
      tp0 += t[k].pos0_diff; ts0 += t[k].speed0_diff;
      tr += t[k].rows;
      unexplained_total += t[k].unexplained;
    }
    printf("\n%ld rows compared (stride %d, %d dates x %d systems, flagset iflag %lu"
           " sid_mode %ld, ephemeris %016llX)\n",
           tr, (int)ha_h.stride, harness_ndates_used((int)ha_h.stride), (int)ha_h.nsys,
           (unsigned long)ha_h.iflag, (long)ha_h.sid_mode,
           (unsigned long long)ha_h.ephe_fp);
    printf("positions differing      : %ld%s\n", tp, tp ? "" : "   (bit-identical)");
    printf("return codes differing   : %ld\n", trc);
    printf("error strings differing  : %ld\n", te);
    printf("cusp speeds differing    : %ld\n", ts);
    /* Index 0 is outside the documented range and both builds write a literal
     * zero there (swehouse.c sets cusp[0] and cusp_speed[0] before anything
     * else). A difference is therefore never an intended speed change and is
     * gated, not merely printed -- a change here used to be invisible, because
     * a `c >= 1` guard excluded index 0 from the comparison entirely. */
    printf("  of which at index 0    : positions %ld, speeds %ld%s\n", tp0, ts0,
           (tp0 || ts0) ? "   <-- REGRESSION, outside the documented range" : "");
    printf("ascmc differing          : %ld\n", ta);
    printf("ascmc speeds differing   : %ld\n", tas);
    printf("slots the baseline left untouched and the patched build fills:\n");
    printf("  positions %ld, speeds %ld\n", tpf, tsf);
    if (tpl || tsl)
      printf("REGRESSION: slots the baseline filled and the patched build does not:\n"
             "  positions %ld, speeds %ld\n", tpl, tsl);
    if (tpnf || tsnf)
      printf("REGRESSION: non-finite on one side only: positions %ld, speeds %ld\n",
             tpnf, tsnf);
    if (tpnb || tsnb)
      printf("non-finite in BOTH builds (unchanged, but wrong in both):"
             " positions %ld, speeds %ld\n", tpnb, tsnb);
    if (tpw)
      printf("REGRESSION: position differences beyond any plausible range: %ld\n", tpw);
    if (tsw)
      printf("speed differences beyond 1e9 deg/day: %ld  (reported, not gated: a cusp"
             " speed really does\n  reach 1e18 deg/day within a nanodegree of a"
             " singularity, and changing cusp speeds is\n  what this patch set is"
             " for)\n", tsw);

    printf("\npermitted difference classes (%s):\n",
           allow ? "--expect-apc-equator in force" : "none in force; every difference must be explained");
    printf("  the five per-date caps are %.0f/%.0f/%.0f/%.0f/%.0f x %d dates used;"
           " the other four are absolute\n  because their classes live entirely in the"
           " 396 degenerate rows, which no stride subsamples\n",
           CAP_APC_ROUND_PER_DATE, CAP_GAUQ_FILL_PER_DATE, CAP_GAUQ_CLEAR_PER_DATE,
           CAP_RAD_ASCSPD_PER_DATE, CAP_SID_ASCSPD_PER_DATE, nd_used);
    printf("  %-22s %10s %10s %12s %12s   %s\n",
           "class", "count", "cap", "worst", "bound", "shape");
    for (k = 0; k < CL_N; k++) {
      int over = cl[k].count > cl[k].cap;
      int oob = cl[k].bound >= 0.0 && cl[k].worst > cl[k].bound;
      if (over) class_over++;
      if (oob) bound_over++;
      printf("  %-22s %10ld %10ld %12.3e ", cl[k].name, cl[k].count,
             cl[k].cap, cl[k].worst);
      if (cl[k].bound >= 0.0) printf("%12.3e", cl[k].bound);
      else                    printf("%12s", "shape only");
      printf("   %s%s%s\n", cl[k].what, over ? "   <-- OVER CAP" : "",
             oob ? "   <-- OVER BOUND" : "");
    }
    if (sid_rate_missing)
      printf("  (%ld dates had no ayanamsa, so the sidereal class could not be"
             " applied to them)\n", sid_rate_missing);
    printf("\nunexplained differences  : %ld   %s\n", unexplained_total,
           unexplained_total ? "FAIL" : "pass");
    printf("classes over their cap   : %ld   %s\n", class_over,
           class_over ? "FAIL" : "pass");
    printf("classes over their bound : %ld   %s\n", bound_over,
           bound_over ? "FAIL" : "pass");
    printf("\n%s\n",
           (unexplained_total || class_over || bound_over) ? "compare: FAILED" : "compare: passed");
    free(S);
    swe_close();
    return (unexplained_total || class_over || bound_over) ? 1 : 0;
  }
}
