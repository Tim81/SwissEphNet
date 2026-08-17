/* Shared sampling for the swehouse.c cusp-speed harness.
 *
 * The sample set has to be identical on every platform and in every build, or
 * a cross-build comparison compares different points and means nothing. That
 * rules out rand(): MSVC's and glibc's differ, and neither is specified. The
 * generator below is splitmix64, pure 64 bit integer arithmetic with no
 * library call in it, so the same seed yields the same doubles everywhere.
 *
 * Dates are drawn uniformly in Julian day between 1 January 1000 BC and
 * 31 December 4100, which is 5100 years and about 1.86 million days. The
 * bounds are taken from swe_julday() rather than written as constants, so they
 * follow the library's own calendar handling.
 *
 * A random sample never lands on a degeneracy. Measured on this one: the
 * closest approach to the equator is 8.58e-03 degrees and to a quadrant of
 * armc 4.03e-04 degrees, so every configuration the patch demonstrations use
 * -- the equator to 5e-11, armc exactly 270 -- sat outside everything the
 * harness proved anything about. The random block is therefore followed by a
 * deterministic degenerate block (HARNESS_NDEGEN rows) which pins latitude,
 * armc and obliquity on the values the formulae divide by. Those rows carry
 * their own armc and obliquity rather than deriving them from a date, because
 * no date produces armc exactly 270.
 *
 * Only the Sunshine systems need an ephemeris. swe_houses_ex2() asks for the
 * Sun with a hardcoded flag set (swehouse.c:146-148 and :260-262), so the
 * library picks its own source and falls back to Moshier when no file is
 * present; the harness therefore needs no ephemeris files at all. Beyond about
 * 3000 AD that call fails, and the harness then supplies its own declination
 * rather than the 99 sentinel: 99 makes swe_houses_armc_ex2() reuse a static
 * `saved_sundec` from whatever call happened to run before it, which made
 * 'I' results depend on the stride and on iteration order for 21.7% of dates.
 */
#ifndef HARNESS_H
#define HARNESS_H

#include "swephexp.h"
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <math.h>

#define HARNESS_NRANDOM  25520
#define HARNESS_SEED     0x5753454850484F55ULL   /* "SWEHPHOU" */
#define HARNESS_MAXCUSP  37                      /* 'G' uses 36 sectors + [0] */

/* ---------------------------------------------------------- degenerate block */

/* Obliquity used for the degenerate rows that are not testing an obliquity
 * degeneracy, and the polar circle that follows from it. */
#define HARNESS_EPS0     23.4392911
#define HARNESS_POLARC   (90.0 - HARNESS_EPS0)

#define HARNESS_NDEGLAT  11
#define HARNESS_NDEGARMC 12
#define HARNESS_NDEGEPS  3
#define HARNESS_NDEGEN   (HARNESS_NDEGLAT * HARNESS_NDEGARMC * HARNESS_NDEGEPS)

#define HARNESS_NDATES   (HARNESS_NRANDOM + HARNESS_NDEGEN)

/* The date the degenerate rows carry, for the entry points that need one.
 * J2000, so that anything printed beside it is recognisable. */
#define HARNESS_DEGEN_JD 2451545.0

/* Declination handed to Sunshine when the Sun cannot be computed. A definite
 * number, so that the answer does not depend on what ran before. */
#define HARNESS_SUNDEC_FALLBACK 0.0

/* Written into every output slot before every call. If one comes back, the
 * library left it untouched and the caller would have read uninitialised
 * stack. Chosen to be finite, negative and absurd, so it cannot be mistaken
 * for a plausible answer and cannot be produced by any of the formulae.
 * Defined here, not in each .c, so the writer and the reader cannot drift. */
#define HARNESS_SENTINEL (-8.8888888888e88)

static int harness_is_sentinel(double v) { return v == HARNESS_SENTINEL; }

static void harness_fill_sentinel(double *a, int n)
{
  int i;
  for (i = 0; i < n; i++) a[i] = HARNESS_SENTINEL;
}

/* Bitwise identity, so that a slot which is NaN in both builds counts as
 * unchanged instead of differing forever (NaN != NaN). `==` first so that
 * +0.0 and -0.0 are still one value. */
static int harness_same(double a, double b)
{
  uint64_t x, y;
  if (a == b) return 1;
  memcpy(&x, &a, sizeof x);
  memcpy(&y, &b, sizeof y);
  return x == y;
}

static int harness_finite(double v) { return v > -1e300 && v < 1e300; }

/* The 26 house letters swe_houses_armc_ex2() accepts, 'i' distinct from 'I'. */
static const char *HARNESS_SYSTEMS = "ABCDEFGHIiJKLMNOPQRSTUVWXY";
#define HARNESS_NSYS 26

/* `fixed` rows carry their own armc and obliquity and ignore jd for the frame;
 * jd and geolon are still filled, so the date entry points remain usable. */
typedef struct {
  double jd; double geolat; double geolon;
  double armc; double eps; int fixed;
} harness_sample;

static uint64_t harness_rng;

static void harness_seed(uint64_t s) { harness_rng = s; }

static uint64_t harness_next(void)
{
  uint64_t z = (harness_rng += 0x9E3779B97F4A7C15ULL);
  z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
  z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
  return z ^ (z >> 31);
}

/* uniform in [0,1), 53 significant bits, exactly representable */
static double harness_unit(void)
{
  return (double)(harness_next() >> 11) * (1.0 / 9007199254740992.0);
}

static double harness_jd_lo(void) { return swe_julday(-999, 1, 1, 0.0, SE_GREG_CAL); }
static double harness_jd_hi(void) { return swe_julday(4100, 12, 31, 0.0, SE_GREG_CAL); }

/* lo + u*span, computed so that no compiler can fuse the multiply into the add.
 * `lo + harness_unit() * (hi - lo)` written inline is exactly the a + b*c shape
 * that -ffp-contract=fast turns into an FMA, and an FMA here shifts the sampled
 * coordinate by about one ULP. Two builds would then be asked different
 * questions and every "bit-identical" claim would be measuring the optimiser.
 * build.sh and build.ps1 also pass -ffp-contract=off / /fp:precise; the
 * volatile is the belt to those braces, because a harness that samples
 * different points is worse than no harness. */
static double harness_mix(double lo, double span, double u)
{
  volatile double p = u * span;
  return lo + p;
}

/* Fill dst[0..n-1] with the sample set. Always call this, never generate
 * samples inline, so every mode of every build walks the same points. */
static void harness_samples(harness_sample *dst, int n)
{
  double lo = harness_jd_lo(), hi = harness_jd_hi();
  double lat[HARNESS_NDEGLAT], arm[HARNESS_NDEGARMC], ep[HARNESS_NDEGEPS];
  int i, a, b, c;
  harness_seed(HARNESS_SEED);
  for (i = 0; i < n && i < HARNESS_NRANDOM; i++) {
    dst[i].jd     = harness_mix(lo, hi - lo, harness_unit());
    dst[i].geolat = harness_mix(-89.9, 179.8, harness_unit());
    dst[i].geolon = harness_mix(-180.0, 360.0, harness_unit());
    dst[i].armc   = 0.0;
    dst[i].eps    = 0.0;
    dst[i].fixed  = 0;
  }
  /* The equator, the polar circle either side, the poles, and one ULP-scale
   * step off each; every quadrant of armc and one nanodegree either side;
   * an obliquity of zero and of ninety. These are the points at which the
   * formulae in swehouse.c divide by something that has just become zero. */
  lat[0] = 0.0;  lat[1] = 1e-11;  lat[2] = -1e-11;
  lat[3] = HARNESS_POLARC;  lat[4] = HARNESS_POLARC + 1e-9;  lat[5] = HARNESS_POLARC - 1e-9;
  lat[6] = -HARNESS_POLARC; lat[7] = -HARNESS_POLARC + 1e-9; lat[8] = -HARNESS_POLARC - 1e-9;
  lat[9] = 90.0; lat[10] = -90.0;
  arm[0] = 0.0;   arm[1] = 1e-9;         arm[2] = 360.0 - 1e-9;
  arm[3] = 90.0;  arm[4] = 90.0 + 1e-9;  arm[5] = 90.0 - 1e-9;
  arm[6] = 180.0; arm[7] = 180.0 + 1e-9; arm[8] = 180.0 - 1e-9;
  arm[9] = 270.0; arm[10] = 270.0 + 1e-9; arm[11] = 270.0 - 1e-9;
  ep[0] = HARNESS_EPS0; ep[1] = 0.0; ep[2] = 90.0;
  i = HARNESS_NRANDOM;
  for (c = 0; c < HARNESS_NDEGEPS; c++)
    for (a = 0; a < HARNESS_NDEGLAT; a++)
      for (b = 0; b < HARNESS_NDEGARMC; b++) {
        if (i >= n) return;
        dst[i].jd     = HARNESS_DEGEN_JD;
        dst[i].geolat = lat[a];
        dst[i].geolon = 0.0;
        dst[i].armc   = arm[b];
        dst[i].eps    = ep[c];
        dst[i].fixed  = 1;
        i++;
      }
}

/* Stride subsamples the RANDOM block only. Subsampling the degenerate block
 * would throw away the points it exists to test: at stride 40, 386 of its 396
 * rows would never be walked. */
static int harness_ndates_used(int stride)
{
  return (HARNESS_NRANDOM + stride - 1) / stride + HARNESS_NDEGEN;
}

static int harness_date_at(int n, int stride)
{
  int nr = (HARNESS_NRANDOM + stride - 1) / stride;
  return (n < nr) ? n * stride : HARNESS_NRANDOM + (n - nr);
}

static int harness_ncusp(int hsys) { return (hsys == 'G') ? 36 : 12; }

/* FNV-1a over the error string, so a dump row can carry serr in four bytes. */
static uint32_t harness_hash_str(const char *s)
{
  uint32_t h = 2166136261u;
  while (*s) { h ^= (unsigned char)*s++; h *= 16777619u; }
  return h;
}

/* ------------------------------------------------------ ephemeris fingerprint
 *
 * Sunshine houses ask the library for the Sun, so a dump depends on which
 * ephemeris answered. Measured: with SE_EPHE_PATH set to a directory holding
 * the Swiss files, 208 of 1,664 rows differ from the same build with no files
 * present, because Moshier answered instead. Comparing those two dumps
 * attributes a data difference to the patch. The fingerprint below is folded
 * into the dump header and `compare` refuses a pair that disagrees.
 *
 * It is a hash of the library version and of the Sun at four fixed epochs,
 * taken through the same public call the harness itself uses, so it changes
 * when the answer changes and not merely when a path string changes. */
static uint64_t harness_fnv64(uint64_t h, const void *p, size_t n)
{
  const unsigned char *b = (const unsigned char *)p;
  size_t i;
  for (i = 0; i < n; i++) { h ^= b[i]; h *= 1099511628211ULL; }
  return h;
}

static uint64_t harness_ephe_fingerprint(void)
{
  static const double jds[4] = { 1000000.5, 1500000.5, 2451545.0, 2600000.5 };
  uint64_t h = 14695981039346656037ULL;
  double x[6];
  char serr[AS_MAXCH];
  char vbuf[AS_MAXCH];
  int i, j, rc;
  const char *v;
  vbuf[0] = '\0';
  v = swe_version(vbuf);          /* writes into the buffer; NULL would crash */
  if (v != NULL) h = harness_fnv64(h, v, strlen(v));
  for (i = 0; i < 4; i++) {
    int32_t r32;
    for (j = 0; j < 6; j++) x[j] = HARNESS_SENTINEL;
    serr[0] = '\0';
    rc = swe_calc_ut(jds[i], SE_SUN, 0, x, serr);
    r32 = (int32_t)rc;
    h = harness_fnv64(h, &r32, sizeof r32);
    h = harness_fnv64(h, x, sizeof x);
  }
  return h;
}

/* ---------------------------------------------------------------- waivers --
 *
 * One shape of non-finite result survives on the sample set, and it is
 * upstream's rather than these patches': Sunshine ('I', not 'i') within about
 * 1e-10 degrees of a pole returns NaN for four cusps and their four speeds and
 * still reports OK. Measured identical in the pristine v2.10.3bfinal tree and
 * in the patched one, so nothing here causes it and nothing here fixes it.
 *
 * It is waived by SHAPE -- one letter, a latitude at the pole, and NaN in both
 * builds -- and never by magnitude or by count alone. Any other non-finite
 * value, in any other letter or at any other latitude, still fails. Without the
 * degenerate block the sample never reached a pole and this never appeared;
 * refusing to add the block would have been the cheaper way to keep the table
 * clean, which is exactly the move this harness exists to stop. */
static int harness_pole_nan_waived(int hsys, double geolat)
{
  return hsys == 'I' && fabs(geolat) > 89.99999999;
}

/* The three configurations at which a house cusp has no derivative at all, so
 * that a numerical difference of the position is not a second opinion about
 * the closed form but a different question entirely:
 *
 *   a geographic pole, where the horizon is the equator;
 *   an obliquity of exactly 0 or exactly 90, which no Earth has;
 *   the horizon exactly tangent to the ecliptic, |geolat| + eps == 90 with the
 *     ascendant at a solstice (armc 90 or 270), where the cusp POSITIONS are
 *     discontinuous -- measured: at lat = 90 - eps and armc = 270 the reported
 *     speed and the numerical difference disagree by 78%, and one nanodegree
 *     of latitude away they agree to 6e-11.
 *
 * These rows are sampled, computed and structurally gated like every other row.
 * What is held to a different statement is only the derivative agreement, and
 * the reason is written down here rather than left as an unexplained filter. */
static int harness_outside_domain(double geolat, double eps, double armc)
{
  double t;
  if (fabs(geolat) >= 90.0) return 1;
  if (!(eps > 0.0 && eps < 90.0)) return 1;
  t = fabs(fabs(geolat) + eps - 90.0);
  if (t <= 1e-12) {
    double a = fabs(swe_difdeg2n(armc, 90.0));
    double b = fabs(swe_difdeg2n(armc, 270.0));
    if (a <= 1e-8 || b <= 1e-8) return 1;
  }
  return 0;
}

/* ------------------------------------------------------------ argument help */

/* atoi() answers 0 for "ten", and the caller then silently used stride 1 on a
 * run the operator believed was subsampled -- or, worse, believed was not. */
static int harness_parse_stride(const char *s, int *out)
{
  char *end = NULL;
  long v;
  if (s == NULL || *s == '\0') return 0;
  errno = 0;
  v = strtol(s, &end, 10);
  if (end == s || *end != '\0' || errno == ERANGE) return 0;
  if (v < 1 || v > HARNESS_NRANDOM) return 0;
  *out = (int)v;
  return 1;
}

/* ------------------------------------------------------------ dump header --
 *
 * A dump used to be a bare stream of records. Two empty files then compared
 * equal, and `compare` printed "bit-identical" and exited 0 having read
 * nothing at all. The header below makes a dump self-describing, so the reader
 * can refuse a file that is empty, truncated, from a different sample set, a
 * different record layout, a different flag set or a different ephemeris,
 * instead of blessing it.
 *
 * Field order is chosen so that every member is naturally aligned with no
 * padding on any ABI the harness builds on: the 8 byte members sit at offsets
 * 8, 16, 56, 64 and 72. HARNESS_HEADER_BYTES is asserted against sizeof at run
 * time. */
#define HARNESS_MAGIC        0x48535044u   /* "HSPD" */
#define HARNESS_FORMAT       3u            /* bump whenever a record changes */
#define HARNESS_HEADER_BYTES 112

typedef struct {
  uint32_t magic;
  uint32_t format;
  uint64_t seed;
  uint64_t ephe_fp;      /* fingerprint of the data that answered */
  int32_t  ndates;
  int32_t  ndegen;
  int32_t  stride;
  int32_t  nsys;
  int32_t  maxcusp;
  uint32_t iflag;        /* the flag set the rows were computed with */
  int32_t  sid_mode;     /* swe_set_sid_mode() argument, -1 if not sidereal */
  int32_t  pad0;
  int64_t  rows;         /* patched in when the dump is closed; -1 while open */
  double   jd_lo;
  double   jd_hi;
  char     systems[32];  /* NUL padded */
} harness_header;

static void harness_header_init(harness_header *h, int stride,
                                uint32_t iflag, int32_t sid_mode)
{
  memset(h, 0, sizeof *h);
  h->magic    = HARNESS_MAGIC;
  h->format   = HARNESS_FORMAT;
  h->seed     = HARNESS_SEED;
  h->ephe_fp  = harness_ephe_fingerprint();
  h->ndates   = HARNESS_NDATES;
  h->ndegen   = HARNESS_NDEGEN;
  h->stride   = stride;
  h->nsys     = HARNESS_NSYS;
  h->maxcusp  = HARNESS_MAXCUSP;
  h->iflag    = iflag;
  h->sid_mode = sid_mode;
  h->pad0     = 0;
  h->rows     = -1;
  h->jd_lo    = harness_jd_lo();
  h->jd_hi    = harness_jd_hi();
  strncpy(h->systems, HARNESS_SYSTEMS, sizeof h->systems - 1);
}

/* Rows a dump with this stride must contain, so the reader can insist on it. */
static long harness_expected_rows(int stride, int nsys)
{
  return (long)harness_ndates_used(stride) * nsys;
}

/* ---------------------------------------------------------------- flagsets --
 *
 * Named flag sets, so that a dump can cover the sidereal and radians paths as
 * well as the tropical one. Before this the harness ran the tropical path only
 * and three whole functions -- sidereal_houses_ecl_t0(), sidereal_houses_ssypl()
 * and the SEFLG_RADIANS block -- were never entered by any mode. */
typedef struct {
  const char *name;
  uint32_t iflag;
  int32_t sid_mode;
} harness_flagset;

static const harness_flagset HARNESS_FLAGSETS[] = {
  { "trop",   0,                                            -1 },
  { "rad",    (uint32_t)SEFLG_RADIANS,                      -1 },
  { "sid",    (uint32_t)SEFLG_SIDEREAL,                     SE_SIDM_FAGAN_BRADLEY },
  { "sidt0",  (uint32_t)SEFLG_SIDEREAL,
              SE_SIDM_FAGAN_BRADLEY | SE_SIDBIT_ECL_T0 },
  { "sidssy", (uint32_t)SEFLG_SIDEREAL,
              SE_SIDM_FAGAN_BRADLEY | SE_SIDBIT_SSY_PLANE }
};
#define HARNESS_NFLAGSETS 5

static const harness_flagset *harness_flagset_by_name(const char *s)
{
  int i;
  for (i = 0; i < HARNESS_NFLAGSETS; i++)
    if (strcmp(s, HARNESS_FLAGSETS[i].name) == 0) return &HARNESS_FLAGSETS[i];
  return NULL;
}

static void harness_apply_flagset(const harness_flagset *f)
{
  if (f->sid_mode >= 0) swe_set_sid_mode(f->sid_mode, 0, 0);
}

#endif /* HARNESS_H */
