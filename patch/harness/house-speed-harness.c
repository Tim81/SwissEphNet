/* Regression harness for the swehouse.c cusp-speed patches.
 *
 * Build it twice, once against an unpatched Swiss Ephemeris and once against a
 * patched one, and compare. The unpatched library is the baseline: positions
 * must not move at all, return codes and error strings must not change, and
 * every speed that does change has to be accounted for.
 *
 * Three modes:
 *
 *   dump FILE [stride] [flagset]
 *                        every sampled value, in a compact binary record, for
 *                        cross-build comparison by compare.c. flagset is one of
 *                        trop, rad, sid, sidt0, sidssy and selects the flag set
 *                        the rows are computed with; it is recorded in the
 *                        header and compare refuses a mismatched pair
 *   deriv [stride]       each reported cusp speed against a Richardson
 *                        extrapolated central difference of the cusp position.
 *                        Needs no second build: it checks the library against
 *                        its own positions
 *   claims [stride]      the checks that need two calls rather than two builds:
 *                        the two entry points against each other, the Porphyry
 *                        fallback, the sidereal identity in all three sidereal
 *                        frames, the radians conversion, the ascmc derivatives,
 *                        non-finite values and a magnitude histogram
 *
 * stride subsamples the random part of the date set: 1 is all 25520 random
 * dates, 10 every tenth. The 396 degenerate dates are never subsampled.
 *
 * All three modes are gates. `dump` exits non-zero if the file it wrote is not
 * complete and readable; `deriv` and `claims` exit non-zero when a check they
 * make fails.
 *
 * WHAT A MEAN CANNOT SEE. The derivative gate used to be a mean per house
 * letter over about 306,000 comparisons. An error confined to a latitude band
 * is diluted by that band's share of the sample: a defect on two Placidus
 * cusps between 60 and 70 degrees of latitude touches 1.85% of the letter's
 * comparisons, so the mean sees 1/54th of it. The gate therefore also carries
 * a WORST, and -- because a worst is deaf to a systematic error smaller than
 * the sample's own outliers -- a MEDIAN per (letter, cusp, 10-degree latitude
 * band) bucket. The median is what makes a small band-local error visible: it
 * cannot be moved by the handful of near-singular points that set the worst,
 * and it moves the moment most of a bucket shifts.
 *
 * The derivative reference differentiates with respect to armc at fixed
 * obliquity and fixed solar declination, through swe_houses_armc_ex2(). That
 * is deliberate. The analytic speeds are defined the same way, and the date
 * based entry point would also move the obliquity and, for Sunshine, the Sun,
 * adding terms the speeds never claimed to carry.
 */
#include "harness.h"
#include <stdlib.h>

#define SOLAR_YEAR 365.24219893
#define ARMCS ((SOLAR_YEAR + 1) / SOLAR_YEAR * 360)   /* deg of armc per day */

/* Gate thresholds. Named and gathered here so that what the gate will and will
 * not tolerate is a stated number rather than a buried literal. Every one of
 * them was set from a measured run of the shipping stack and carries its
 * measured margin in ../harness-README.md. */
#define GATE_DERIV_MEAN_REL    1e-8    /* mean rel. err. per letter, random rows */
#define GATE_DERIV_MEAN_REL_D  1e-7    /* ... and within the degenerate block    */
#define GATE_DERIV_WORST_REL   1e-3    /* worst rel. err. over every comparison */
#define GATE_DERIV_SMALL_ABS   5e-4    /* abs. err. where the reference is <1   */
#define GATE_DERIV_BUCKET_MED  1e-8    /* median per (letter, cusp, lat band)   */
/* The same buckets read further up the distribution. A median moves only when
 * MOST of a bucket shifts; a defect confined to part of one -- an armc range,
 * one branch of a conditional, one season -- leaves the median exactly where
 * it was and lifts the tail instead. Two points are taken, because one is not
 * enough: the 90th catches a defect on as little as a tenth of a bucket but is
 * the noisier statistic, and the 80th is quieter and bites harder on the
 * commoner shape.
 *
 * Both numbers below are the clean stack's own ceiling over 92 strides from 1
 * to 25,520, not a stride-1 reading: p90 never exceeds 1.000e-07 and p80 never
 * exceeds 3.162e-08. The metric is quantised to half-decade bins and the value
 * reported is the bin's UPPER edge, so a threshold is only meaningful on a bin
 * edge; the p80 gate is one bin above its ceiling and the p90 gate two. For
 * scale, a uniform (1+2e-7) on one cusp over a third of a bucket reads
 * 3.162e-07 at both percentiles, so the 80th gate catches it with a bin to
 * spare and the 90th does not -- which is why both are here.
 *
 * Between them they leave a window, and it is stated rather than left to be
 * discovered: a speed defect on 10-20% of ONE bucket escapes if its binned
 * reading stays at or under 1e-6 and share x magnitude / 12 stays under the
 * 1e-8 per-letter mean. At a 15% share that is any magnitude up to about
 * 7e-7. ../harness-README.md carries the arithmetic. */
#define GATE_DERIV_BUCKET_P90  1e-6    /* 10x over the measured 1.000e-07 */
#define GATE_DERIV_BUCKET_P80  1e-7    /* 3.2x over the measured 3.162e-08 */
#define GATE_DERIV_STEEP_RATIO 1e-5    /* |got/ref - 1| where |ref| > 1e4       */
#define GATE_DERIV_REJ_SHARE   0.02    /* share of a letter a reject class may take */
#define GATE_DERIV_UNCONV_SHARE 0.01   /* ... for the unconverged class             */
#define GATE_DERIV_REJ_SHARE_D   0.05  /* the same, within the degenerate block     */
#define GATE_DERIV_UNCONV_SHARE_D 0.25 /* ... which is where the hard points live   */
#define GATE_DERIV_TINYBUCKET_SHARE 0.02 /* share of comparisons in ungated buckets */
/* The three points the house formulae are not defined at: a geographic pole,
 * and an obliquity of exactly 0 or exactly 90. The library still answers, and
 * its answer still has to be finite, written and stable -- but the closed form
 * and the numerical difference are not two estimates of one derivative there,
 * and holding them to 1e-8 would only mean deleting the rows. These bounds are
 * the measured behaviour of the shipping stack with headroom, so a change that
 * made those points worse still fails. */
/* The outside-domain rows are exactly the 396 degenerate ones, which are never
 * subsampled, so these two numbers are the whole population and not a sample:
 * they read the same at every stride and the margins below are exact.
 * Measured: 98.91% agree, and 21.78% of the comparisons are given up as
 * unconverged. The floor was 0.50, which left 48 points of slack nothing could
 * ever use; at 0.95 the slack is 3.91 points against a measured 1.09 of
 * disagreement, 4.6x on the side that moves. */
#define GATE_DERIV_OUTSIDE_SHARE 0.95
/* The one class in the degenerate block that was gated by nothing at all:
 * 21,958 comparisons, 21.8% of it, discarded as an unconverged reference and
 * then never counted against anything. A build that drove the whole block into
 * this column would have emptied the agreement gate above of its denominator
 * and still read pass. 0.40 against a measured 0.2178 is 1.8x. */
#define GATE_DERIV_BND_UNCONV_SHARE 0.40
/* claims' ascmc gate asks the same question of a different population and gets
 * a different answer -- 96.27%, not 98.91% -- so it gets its own floor rather
 * than borrowing one measured somewhere else. Both are exact rather than
 * sampled: 0.90 leaves 10 points against a measured 3.73 of disagreement. */
#define GATE_ASCMC_OUTSIDE_SHARE 0.90
#define GATE_ENTRY_DEG         1e-7    /* ex2 vs armc_ex2, worst cusp, degrees  */
#define GATE_ENTRY_SPEED_REL   1e-5    /* ... and the same for the speeds       */
#define GATE_SID_RATE_DEGDAY   1e-10   /* subtracted rate vs swehouse's own d/dt */
#define GATE_SID_RATE_FRAC     0.01    /* ... vs an independent d/dt, per value  */
#define GATE_SID_FRAC_SHARE    0.05    /* ... and the share allowed to miss it   */
#define GATE_SID_POS_DEG       1e-9    /* sidereal pos vs tropical - ayanamsa   */
#define GATE_ASCMC_REL         1e-6    /* ascmc_speed vs derivative of ascmc    */
#define GATE_ASCMC_SMALL_ABS   1e-4    /* ... where the reference is below 1    */
/* The ascmc derivative check discards a comparison whose reference is out of
 * range or has not converged. Both were printed and neither was gated, which
 * made them the last ungated `continue` in the harness: a change that pushed
 * every ascmc reference out of range would have emptied the check and still
 * read pass. Split random from degenerate for the usual reason -- the
 * degenerate block is 396 fixed dates, 1.5% of the sample at stride 1 and 97%
 * at stride 2000, and it is where every hard reference lives. */
#define GATE_ASCMC_SKIP_SHARE     0.01 /* reference out of range, random rows   */
#define GATE_ASCMC_SKIP_SHARE_D   0.40 /* ... and within the degenerate block   */
#define GATE_ASCMC_UNCONV_SHARE   0.01 /* reference unconverged, random rows    */
#define GATE_ASCMC_UNCONV_SHARE_D 0.05 /* ... and within the degenerate block   */
#define GATE_RADIANS_REL       1e-15   /* radians value vs degrees * DEGTORAD   */

/* Reported, not gated: swe_houses_ex2() forms its obliquity internally as
 * swi_epsiln(tjde) + nutlo[1] and nothing public returns that same double, so
 * the two entry points are handed inputs that differ in the last bit or two.
 * One ULP of eps moves a cusp by about 1e-12 degrees. This is the harness's
 * own input floor, which is why the gate above sits two decades higher. */
#define REPORT_ENTRY_DEG      1e-9

#define SENTINEL HARNESS_SENTINEL

/* How many times the derivative reference may halve its step before the
 * comparison is given up as unconverged. Each halving costs four more library
 * calls and only for the cusps still pending, so the cost is set by how many
 * points are genuinely hard rather than by the sample size. */
#define DERIV_MAXHALVE 4

/* ------------------------------------------------------------------ dump -- */

/* Every fwrite is checked. The previous version checked none of them and did
 * not check fclose either, so a full disk, a short write or a failed flush
 * produced a truncated or empty dump and an exit status of 0; `compare` then
 * read two empty files, found no differences, printed "bit-identical" and
 * exited 0 as well. Two consecutive silent successes reported a pass that
 * nothing had actually established. */
static int wr(const void *p, size_t sz, size_t n, FILE *fp, const char *what)
{
  if (fwrite(p, sz, n, fp) != n) {
    fprintf(stderr, "dump: short write on %s: %s\n", what, strerror(errno));
    return 0;
  }
  return 1;
}

/* One record per (date, house system). Variable length, because 'G' returns 36
 * sectors and everything else 12; the reader follows the same rule. A fixed
 * header goes in front so the reader can tell a real dump from an empty file,
 * a truncated one, or one written by a different sample set, record layout,
 * flag set or ephemeris.
 *
 * The degenerate rows carry an armc and an obliquity of their own. For the
 * tropical flag set they are computed through swe_houses_armc_ex2(), which is
 * the only entry point that will accept them; the other flag sets have no armc
 * entry point at all, so there the degenerate rows contribute their latitude
 * through the date entry point and nothing else. Both dumps in a comparison
 * are held to the same flag set, so both follow the same rule. */
static int mode_dump(const char *path, int stride, const harness_flagset *fset)
{
  harness_sample *S;
  double cusp[HARNESS_MAXCUSP + 4], cs[HARNESS_MAXCUSP + 4];
  double ascmc[12], as[12];
  char serr[AS_MAXCH];
  harness_header hdrec;
  FILE *fp;
  int n, i, k, rc, nc, nd;
  long rows = 0, expect;

  if (sizeof(harness_header) != HARNESS_HEADER_BYTES) {
    fprintf(stderr, "dump: header is %d bytes, expected %d; this ABI pads it\n",
            (int)sizeof(harness_header), HARNESS_HEADER_BYTES);
    return 2;
  }

  S = (harness_sample *)malloc(sizeof(harness_sample) * HARNESS_NDATES);
  if (S == NULL) { fprintf(stderr, "dump: out of memory\n"); return 2; }
  harness_samples(S, HARNESS_NDATES);
  harness_apply_flagset(fset);

  fp = fopen(path, "wb");
  if (fp == NULL) {
    fprintf(stderr, "dump: cannot write %s: %s\n", path, strerror(errno));
    free(S); return 2;
  }

  harness_header_init(&hdrec, stride, fset->iflag, fset->sid_mode);
  if (!wr(&hdrec, sizeof hdrec, 1, fp, "header")) { fclose(fp); free(S); return 2; }

  nd = harness_ndates_used(stride);
  for (n = 0; n < nd; n++) {
    i = harness_date_at(n, stride);
    for (k = 0; k < HARNESS_NSYS; k++) {
      int hs = (unsigned char)HARNESS_SYSTEMS[k];
      int32_t hdr[2];
      uint32_t h32;
      nc = harness_ncusp(hs);
      harness_fill_sentinel(cusp, HARNESS_MAXCUSP + 4);
      harness_fill_sentinel(cs, HARNESS_MAXCUSP + 4);
      harness_fill_sentinel(ascmc, 12);
      harness_fill_sentinel(as, 12);
      serr[0] = '\0';
      if (S[i].fixed && fset->iflag == 0) {
        int j;
        for (j = 0; j < 10; j++) ascmc[j] = 0;
        ascmc[9] = HARNESS_SUNDEC_FALLBACK;
        rc = swe_houses_armc_ex2(S[i].armc, S[i].geolat, S[i].eps, hs,
                                 cusp, ascmc, cs, as, serr);
      } else {
        rc = swe_houses_ex2(S[i].jd, (int32)fset->iflag, S[i].geolat, S[i].geolon,
                            hs, cusp, ascmc, cs, as, serr);
      }
      hdr[0] = (int32_t)rc;
      hdr[1] = (int32_t)nc;
      h32 = harness_hash_str(serr);
      if (!wr(hdr, sizeof(int32_t), 2, fp, "row header")
       || !wr(&h32, sizeof(uint32_t), 1, fp, "serr hash")
       || !wr(cusp, sizeof(double), nc + 1, fp, "cusp")
       || !wr(cs, sizeof(double), nc + 1, fp, "cusp_speed")
       || !wr(ascmc, sizeof(double), 10, fp, "ascmc")
       || !wr(as, sizeof(double), 10, fp, "ascmc_speed")) {
        fclose(fp); free(S); return 2;
      }
      rows++;
    }
  }

  /* Patch the row count into the header now that it is known, so the reader
   * can insist the file holds exactly the rows it says it holds. */
  expect = harness_expected_rows(stride, HARNESS_NSYS);
  if (rows != expect) {
    fprintf(stderr, "dump: wrote %ld rows, expected %ld\n", rows, expect);
    fclose(fp); free(S); return 2;
  }
  hdrec.rows = (int64_t)rows;
  if (fseek(fp, 0L, SEEK_SET) != 0
   || !wr(&hdrec, sizeof hdrec, 1, fp, "header rewrite")) {
    fprintf(stderr, "dump: cannot finalise header in %s\n", path);
    fclose(fp); free(S); return 2;
  }
  if (ferror(fp)) {
    fprintf(stderr, "dump: error indicator set on %s\n", path);
    fclose(fp); free(S); return 2;
  }
  if (fclose(fp) != 0) {
    fprintf(stderr, "dump: fclose failed on %s: %s\n", path, strerror(errno));
    free(S); return 2;
  }
  free(S);
  fprintf(stderr, "dump: %ld rows, %d dates (stride %d, %d random + %d degenerate),"
          " %d systems, flagset %s, ephemeris %016llX -> %s\n",
          rows, nd, stride, nd - HARNESS_NDEGEN, HARNESS_NDEGEN, HARNESS_NSYS,
          fset->name, (unsigned long long)hdrec.ephe_fp, path);
  return 0;
}

/* ----------------------------------------------------------------- deriv -- */

typedef struct {
  long ok;            /* frame usable, 'P' probe succeeded                    */
  long err_usable;    /* 'P' probe returned ERR but armc came back usable     */
  long skip_armc;     /* armc untouched or non-finite: nothing to stand on    */
  long skip_eclnut;   /* SE_ECL_NUT failed: no obliquity                      */
  long sun_out;       /* Sun out of range; the harness supplies a declination */
  long degen;         /* rows carrying their own armc and obliquity           */
} frame_stats;

/* armc, true obliquity and solar declination for a sampled date, taken from
 * the library's own public entry points rather than reconstructed here.
 *
 * The previous version tested the 'P' call with `< -1`. ERR is -1, so that
 * branch was dead and 6,661 of 25,520 probes came back ERR and were accepted
 * without a word. It also left its local cusp/ascmc uninitialised, so an early
 * return handed the caller whatever was on the stack as armc.
 *
 * ERR from 'P' is not, by itself, a reason to drop the date: inside the polar
 * circle Placidus reports ERR and substitutes Porphyry, and armc is computed
 * before that switch and is perfectly good. So the failure is now seen,
 * counted and separated, and armc is validated against the sentinel before it
 * is trusted, instead of the return code being ignored or the whole date being
 * thrown away.
 *
 * sun_ok says whether the declination is the Sun's or the harness's stand-in.
 * It is never the 99 sentinel: that makes swe_houses_armc_ex2() reuse a static
 * from an earlier call, so an 'I' result depended on the stride and on the
 * order the letters happened to run in, on 21.7% of dates. */
static int frame_of(const harness_sample *s, double *armc, double *eps,
                    double *sundec, int *sun_ok, frame_stats *fs)
{
  double cusp[HARNESS_MAXCUSP + 4], ascmc[12], x[6];
  char serr[AS_MAXCH];
  int rc;

  *armc = SENTINEL; *eps = SENTINEL; *sundec = SENTINEL; *sun_ok = 0;
  harness_fill_sentinel(cusp, HARNESS_MAXCUSP + 4);
  harness_fill_sentinel(ascmc, 12);
  harness_fill_sentinel(x, 6);
  serr[0] = '\0';

  serr[0] = '\0';
  if (swe_calc_ut(s->jd, SE_SUN, SEFLG_EQUATORIAL, x, serr) < 0) {
    *sundec = HARNESS_SUNDEC_FALLBACK;
  } else {
    *sundec = x[1];
    *sun_ok = 1;
  }
  if (!*sun_ok) fs->sun_out++;

  if (s->fixed) {
    *armc = s->armc;
    *eps  = s->eps;
    fs->degen++;
    return 1;
  }

  serr[0] = '\0';
  rc = swe_houses_ex2(s->jd, 0, s->geolat, s->geolon, 'P',
                      cusp, ascmc, NULL, NULL, serr);
  if (harness_is_sentinel(ascmc[2]) || !harness_finite(ascmc[2])) {
    fs->skip_armc++;
    return 0;
  }
  *armc = ascmc[2];

  harness_fill_sentinel(x, 6);
  serr[0] = '\0';
  if (swe_calc_ut(s->jd, SE_ECL_NUT, 0, x, serr) < 0) { fs->skip_eclnut++; return 0; }
  *eps = x[0];                       /* true obliquity, what CalcH is given */
  if (rc < 0) { fs->err_usable++; return 2; }
  fs->ok++;
  return 1;
}

static int cusps_at(double armc, double lat, double eps, double sundec, int hs,
                    double *out, int nc)
{
  double ascmc[12];
  char serr[AS_MAXCH];
  int i;
  harness_fill_sentinel(out, nc + 1);
  for (i = 0; i < 10; i++) ascmc[i] = 0;
  ascmc[9] = sundec;
  serr[0] = '\0';
  return swe_houses_armc_ex2(swe_degnorm(armc), lat, eps, hs, out, ascmc,
                             NULL, NULL, serr);
}

/* --- the bucket histogram -------------------------------------------------
 *
 * One log-spaced histogram per (letter, cusp, 10-degree latitude band), so
 * that the MEDIAN of each bucket can be gated. A mean over a whole letter is
 * blind to an error confined to a band; a worst is blind to a systematic error
 * smaller than the sample's own near-singular outliers. A per-bucket median is
 * blind to neither, and it is the only statistic here that a 1e-5 relative
 * error on one cusp above one latitude cannot hide from.
 *
 * Half a decade per bin, from 1e-20 to 1e20. That is coarse on purpose: the
 * number the gate compares is the bin's UPPER edge, so the reported median is
 * an over-estimate and never flatters the library. */
#define DERIV_NBAND 18          /* the finest banding, used at stride 1        */
#define DERIV_NBIN  80
#define DERIV_MINBUCKET 16      /* below this a median is not a median          */
#define DERIV_WANT_PER_BUCKET 64

/* How many latitude bands the sample can actually support. A median needs
 * points under it, and at stride 200 there are 128 random dates, so eighteen
 * bands would leave about seven comparisons in each and the statistic would
 * evaporate -- which is a real stride dependence and not a reason to stop
 * gating. The banding therefore follows the sample: enough bands that every
 * bucket expects at least DERIV_WANT_PER_BUCKET comparisons, and no more than
 * DERIV_NBAND. A coarser banding dilutes a band-local error more, so a strided
 * run is a weaker test than stride 1 -- which is why the certified figures in
 * ../harness-README.md are all stride 1. */
static int deriv_nband(int stride)
{
  int nr = (HARNESS_NRANDOM + stride - 1) / stride;
  int n = nr / DERIV_WANT_PER_BUCKET;
  if (n < 1) n = 1;
  if (n > DERIV_NBAND) n = DERIV_NBAND;
  return n;
}

static int deriv_band(double lat, int nband)
{
  int b = (int)floor((lat + 90.0) * nband / 180.0);
  if (b < 0) b = 0;
  if (b > nband - 1) b = nband - 1;
  return b;
}

static int deriv_bin(double x)
{
  int b;
  if (!(x > 0.0)) return 0;
  b = (int)floor(2.0 * log10(x)) + 40;
  if (b < 0) b = 0;
  if (b > DERIV_NBIN - 1) b = DERIV_NBIN - 1;
  return b;
}

static double deriv_bin_upper(int b) { return pow(10.0, (b - 39) * 0.5); }

static size_t deriv_bucket(int k, int c, int band, int nband)
{
  return ((size_t)(k * HARNESS_MAXCUSP + c) * nband + band) * DERIV_NBIN;
}

static int mode_deriv(int stride)
{
  harness_sample *S = (harness_sample *)malloc(sizeof(harness_sample) * HARNESS_NDATES);
  uint32_t *hist = (uint32_t *)calloc((size_t)HARNESS_NSYS * HARNESS_MAXCUSP
                                      * DERIV_NBAND * DERIV_NBIN, sizeof(uint32_t));
  double cusp[HARNESS_MAXCUSP + 4], cs[HARNESS_MAXCUSP + 4], ascmc[12], as[12];
  double pm1[HARNESS_MAXCUSP + 4], pp1[HARNESS_MAXCUSP + 4];
  double pm2[HARNESS_MAXCUSP + 4], pp2[HARNESS_MAXCUSP + 4];
  char serr[AS_MAXCH];
  int pend[HARNESS_MAXCUSP + 4];
  double worst[HARNESS_NSYS], sum[HARNESS_NSYS];
  /* The mean is kept for the two blocks separately, for the same reason the
   * reject shares are: the degenerate block is a fixed 396 dates, so at stride
   * 1 it is 1.5% of the sample and at stride 2000 it is 97%, and one threshold
   * over the mixture cannot mean the same thing in both. Its rows also sit at
   * genuinely harder geometry, so mixing them in makes the mean a function of
   * the stride rather than of the library. */
  double sum_r[HARNESS_NSYS], sum_dg[HARNESS_NSYS];
  long cnt_r[HARNESS_NSYS], cnt_dg[HARNESS_NSYS];
  long cnt[HARNESS_NSYS], sentinels[HARNESS_NSYS], nonfinite[HARNESS_NSYS];
  long pos_sent[HARNESS_NSYS], pos_nonfin[HARNESS_NSYS];
  long waived[HARNESS_NSYS], pos_waived[HARNESS_NSYS];
  long attempted[HARNESS_NSYS];
  long rej_unconv[HARNESS_NSYS], rej_small[HARNESS_NSYS];
  long rej_refsent[HARNESS_NSYS], rej_refnonfin[HARNESS_NSYS], rej_rcmix[HARNESS_NSYS];
  /* The same counters restricted to the degenerate block. A share gate whose
   * denominator changes with the stride is not a gate: the degenerate block is
   * a fixed 396 dates, so at stride 40 it is 38% of the sample and at stride 1
   * it is 1.5%, and one threshold cannot mean the same thing in both. The two
   * blocks are therefore gated separately, each against its own denominator. */
  long att_d[HARNESS_NSYS], unconv_d[HARNESS_NSYS];
  long rcmix_d[HARNESS_NSYS], refnf_d[HARNESS_NSYS], refsent_d[HARNESS_NSYS];
  long steep_cnt[HARNESS_NSYS];
  double steep_worst[HARNESS_NSYS], steep_wgot[HARNESS_NSYS], steep_wref[HARNESS_NSYS];
  double wlat[HARNESS_NSYS], warmc[HARNESS_NSYS], wgot[HARNESS_NSYS], wref[HARNESS_NSYS];
  int wcusp[HARNESS_NSYS];
  long small_cnt[HARNESS_NSYS];
  double small_worst[HARNESS_NSYS], small_wgot[HARNESS_NSYS], small_wref[HARNESS_NSYS];
  int small_wcusp[HARNESS_NSYS];
  long bnd_any[HARNESS_NSYS];
  frame_stats fs;
  long dates_seen = 0, unaccounted_total = 0, fail = 0;
  long tiny_bucket_n = 0, judged_total = 0;
  double med_worst = 0, med_sys[HARNESS_NSYS];
  int med_k = -1, med_c = -1, med_band = -1; long med_n = 0;
  /* The same buckets read at the 80th and 90th percentiles instead of the
   * 50th. A median needs most of a bucket to move; an error confined to a
   * third of one -- one armc range, one branch of a conditional -- leaves the
   * median exactly where it was and pushes the top of the distribution up. */
  double p90_worst = 0, p90_sys[HARNESS_NSYS], p80_worst = 0;
  int p90_k = -1, p90_c = -1, p90_band = -1; long p90_n = 0;
  int p80_k = -1, p80_c = -1, p80_band = -1; long p80_n = 0;
  /* The rows the house formulae are not defined for: a geographic pole, or an
   * obliquity of exactly zero or ninety. They are sampled, computed, scanned
   * and gated like everything else on structure -- written, finite, consistent
   * return codes -- but their derivative agreement is held to its own stated
   * bound rather than folded into the main one, because at those three points
   * neither the closed form nor the numerical difference is a derivative of
   * anything. Reported in full so the bound can be checked, never skipped. */
  long bnd_n = 0, bnd_rel_n = 0, bnd_small_n = 0, bnd_steep_n = 0, bnd_agree = 0;
  long bnd_unconv = 0, bnd_rcmix = 0, bnd_refsent = 0, bnd_refnonfin = 0;
  double bnd_rel_worst = 0, bnd_small_worst = 0, bnd_steep_worst = 0;
  double bnd_wlat = 0, bnd_warmc = 0, bnd_weps = 0, bnd_wgot = 0, bnd_wref = 0;
  int bnd_wk = -1, bnd_wc = 0;
  int n, i, k, c, nc, nd, lev, nband;
  uint64_t fp;

  if (S == NULL || hist == NULL) { fprintf(stderr, "deriv: out of memory\n"); return 2; }
  memset(&fs, 0, sizeof fs);
  harness_samples(S, HARNESS_NDATES);
  fp = harness_ephe_fingerprint();
  nband = deriv_nband(stride);
  for (k = 0; k < HARNESS_NSYS; k++) {
    worst[k] = 0; sum[k] = 0; cnt[k] = 0; sentinels[k] = 0; nonfinite[k] = 0;
    pos_sent[k] = 0; pos_nonfin[k] = 0; waived[k] = 0; pos_waived[k] = 0;
    wlat[k] = 0; warmc[k] = 0; wgot[k] = 0; wref[k] = 0; wcusp[k] = 0;
    rej_unconv[k] = 0; rej_small[k] = 0;
    rej_refsent[k] = 0; rej_refnonfin[k] = 0; rej_rcmix[k] = 0;
    steep_cnt[k] = 0; steep_worst[k] = 0; steep_wgot[k] = 0; steep_wref[k] = 0;
    attempted[k] = 0;
    small_cnt[k] = 0; small_worst[k] = 0; small_wgot[k] = 0; small_wref[k] = 0;
    small_wcusp[k] = 0; bnd_any[k] = 0;
    att_d[k] = 0; unconv_d[k] = 0; rcmix_d[k] = 0; refnf_d[k] = 0; refsent_d[k] = 0;
    med_sys[k] = 0; p90_sys[k] = 0;
    sum_r[k] = 0; sum_dg[k] = 0; cnt_r[k] = 0; cnt_dg[k] = 0;
  }

  nd = harness_ndates_used(stride);
  for (n = 0; n < nd; n++) {
    double armc, eps, sundec;
    int sun_ok, fr, bnd;
    i = harness_date_at(n, stride);
    fr = frame_of(&S[i], &armc, &eps, &sundec, &sun_ok, &fs);
    if (fr == 0) continue;
    dates_seen++;
    bnd = harness_outside_domain(S[i].geolat, eps, armc);
    for (k = 0; k < HARNESS_NSYS; k++) {
      int hs = (unsigned char)HARNESS_SYSTEMS[k];
      int rc0, r1, r2, r3, r4, npend;
      double h1 = 1e-3, h2 = 5e-4;
      nc = harness_ncusp(hs);
      harness_fill_sentinel(cusp, HARNESS_MAXCUSP + 4);
      harness_fill_sentinel(cs, HARNESS_MAXCUSP + 4);
      harness_fill_sentinel(as, 12);
      for (c = 0; c < 10; c++) ascmc[c] = 0;
      ascmc[9] = sundec;
      serr[0] = '\0';
      rc0 = swe_houses_armc_ex2(armc, S[i].geolat, eps, hs, cusp, ascmc, cs, as, serr);

      /* The positions the speeds are supposed to be the derivative of. A
       * non-finite one was never gated here at all, so a build that returned
       * NaN cusps and matching NaN speeds could still be "judged 0" and pass. */
      for (c = 1; c <= nc; c++) {
        if (harness_is_sentinel(cusp[c])) pos_sent[k]++;
        else if (!harness_finite(cusp[c])) {
          if (harness_pole_nan_waived(hs, S[i].geolat)) pos_waived[k]++;
          else pos_nonfin[k]++;
        }
      }

      npend = 0;
      for (c = 1; c <= nc; c++) {
        attempted[k]++;
        if (S[i].fixed) att_d[k]++;
        pend[c] = 1;
        npend++;
        /* A slot the library never wrote. Counted, never quietly skipped:
         * this is exactly how 'G' hid 24 unwritten sectors. */
        if (harness_is_sentinel(cs[c])) { sentinels[k]++; pend[c] = 0; npend--; continue; }
        if (!harness_finite(cs[c])) {
          if (harness_pole_nan_waived(hs, S[i].geolat)) waived[k]++;
          else nonfinite[k]++;
          pend[c] = 0; npend--;
        }
      }

      /* Richardson extrapolation, and where it does not converge the step is
       * halved and the reference asked again -- for the cusps still pending
       * only. Previously an unconverged comparison was an uncounted `continue`
       * that no gate ever looked at, so a whole letter could disappear into it. */
      for (lev = 0; npend > 0 && lev <= DERIV_MAXHALVE; lev++) {
        int last = (lev == DERIV_MAXHALVE);
        r1 = cusps_at(armc - h1, S[i].geolat, eps, sundec, hs, pm1, nc);
        r2 = cusps_at(armc + h1, S[i].geolat, eps, sundec, hs, pp1, nc);
        r3 = cusps_at(armc - h2, S[i].geolat, eps, sundec, hs, pm2, nc);
        r4 = cusps_at(armc + h2, S[i].geolat, eps, sundec, hs, pp2, nc);
        for (c = 1; c <= nc; c++) {
          double d1, d2, ref, got, rel, metric;
          if (!pend[c]) continue;
          got = cs[c];
          /* The reference calls have to have answered the same question. If one
           * of them took a different branch, differencing across them compares
           * two systems. Retried at a smaller step first, because near a
           * boundary a wide stencil straddles it and a narrow one does not. */
          if (rc0 != r1 || rc0 != r2 || rc0 != r3 || rc0 != r4) {
            if (last) {
              if (bnd) { bnd_rcmix++; bnd_any[k]++; }
              else { rej_rcmix[k]++; if (S[i].fixed) rcmix_d[k]++; }
              pend[c] = 0; npend--;
            }
            continue;
          }
          if (harness_is_sentinel(pm1[c]) || harness_is_sentinel(pp1[c])
           || harness_is_sentinel(pm2[c]) || harness_is_sentinel(pp2[c])) {
            if (bnd) { bnd_refsent++; bnd_any[k]++; }
            else { rej_refsent[k]++; if (S[i].fixed) refsent_d[k]++; }
            pend[c] = 0; npend--; continue;
          }
          if (!harness_finite(pm1[c]) || !harness_finite(pp1[c])
           || !harness_finite(pm2[c]) || !harness_finite(pp2[c])) {
            if (last) {
              if (harness_pole_nan_waived(hs, S[i].geolat)) waived[k]++;
              else if (bnd) { bnd_refnonfin++; bnd_any[k]++; }
              else { rej_refnonfin[k]++; if (S[i].fixed) refnf_d[k]++; }
              pend[c] = 0; npend--;
            }
            continue;
          }
          d1 = swe_difdeg2n(pp1[c], pm1[c]) / (2 * h1);
          d2 = swe_difdeg2n(pp2[c], pm2[c]) / (2 * h2);
          ref = (4 * d2 - d1) / 3 * ARMCS;
          if (!harness_finite(ref)) {
            if (last) {
              if (harness_pole_nan_waived(hs, S[i].geolat)) waived[k]++;
              else if (bnd) { bnd_refnonfin++; bnd_any[k]++; }
              else { rej_refnonfin[k]++; if (S[i].fixed) refnf_d[k]++; }
              pend[c] = 0; npend--;
            }
            continue;
          }
          /* The reference has to earn the right to judge. Halving the step must
           * barely move it; where it does move, the cusp is turning fast enough
           * that the difference carries its own truncation error, or it is
           * straddling a jump and averaging across it. Retried at a smaller
           * step up to DERIV_MAXHALVE times before it is given up.
           *
           * This test now runs BEFORE the steep one. It used to run after, so
           * every steep comparison was thrown out without ever being asked
           * whether its reference was any good -- and the steep class is
           * exactly where the good and the useless references live side by
           * side. Converged first, then judged, is what lets the class be
           * gated at all. */
          if (fabs(d2 - d1) > 1e-6 * (fabs(d2) + 1.0)) {
            if (last) {
              if (bnd) { bnd_unconv++; bnd_any[k]++; }
              else { rej_unconv[k]++; if (S[i].fixed) unconv_d[k]++; }
              pend[c] = 0; npend--;
            }
            continue;
          }
          /* A reference this steep cannot carry a relative error worth the
           * name, but it has just proved itself convergent, so it can carry a
           * RATIO. Before this the class was an uncounted `continue`, and a
           * build that multiplied every steep speed by 1.5 walked through the
           * gate untouched. */
          if (fabs(ref) > 1e4) {
            double rat = fabs(got / ref - 1.0);
            if (!harness_finite(rat)) rat = 1e300;
            if (bnd) {
              bnd_steep_n++; bnd_any[k]++;
              if (rat <= 1e-6) bnd_agree++;
              if (rat > bnd_steep_worst) bnd_steep_worst = rat;
            } else {
              steep_cnt[k]++;
              if (rat > steep_worst[k]) {
                steep_worst[k] = rat; steep_wgot[k] = got; steep_wref[k] = ref;
              }
            }
            pend[c] = 0; npend--;
            continue;
          }
          /* A relative error against a reference this small is meaningless, but
           * an ABSOLUTE one is not, and discarding the whole comparison is a
           * hole rather than a caution: 'W' and 'N' pin their cusps to sign
           * boundaries, so every reference for them is zero, so every comparison
           * was discarded -- and a build that reported a constant 7.5 deg/day
           * for a cusp that does not move passed the gate with "0 judged". These
           * are judged on absolute error and counted in their own column.
           *
           * Both classes feed ONE bucket metric, |got - ref| / max(|ref|, 1),
           * which is the relative error above one degree per day and the
           * absolute error below it. */
          metric = fabs(got - ref) / (fabs(ref) > 1.0 ? fabs(ref) : 1.0);
          if (!harness_finite(metric)) metric = 1e300;
          if (bnd) {
            bnd_n++; bnd_any[k]++;
            if (metric <= 1e-6) bnd_agree++;
            if (fabs(ref) < 1.0) {
              double ae = fabs(got - ref);
              bnd_small_n++;
              if (ae > bnd_small_worst) bnd_small_worst = ae;
            } else {
              rel = fabs(got - ref) / fabs(ref);
              bnd_rel_n++;
              if (rel > bnd_rel_worst) {
                bnd_rel_worst = rel; bnd_wk = k; bnd_wc = c;
                bnd_wlat = S[i].geolat; bnd_warmc = armc; bnd_weps = eps;
                bnd_wgot = got; bnd_wref = ref;
              }
            }
            pend[c] = 0; npend--;
            continue;
          }
          hist[deriv_bucket(k, c, deriv_band(S[i].geolat, nband), nband) + deriv_bin(metric)]++;
          judged_total++;
          if (fabs(ref) < 1.0) {
            double ae = fabs(got - ref);
            rej_small[k]++;
            small_cnt[k]++;
            if (ae > small_worst[k]) {
              small_worst[k] = ae; small_wcusp[k] = c;
              small_wgot[k] = got; small_wref[k] = ref;
            }
          } else {
            rel = fabs(got - ref) / fabs(ref);
            sum[k] += rel; cnt[k]++;
            if (S[i].fixed) { sum_dg[k] += rel; cnt_dg[k]++; }
            else { sum_r[k] += rel; cnt_r[k]++; }
            if (rel > worst[k]) {
              worst[k] = rel; wcusp[k] = c; wlat[k] = S[i].geolat;
              warmc[k] = armc; wgot[k] = got; wref[k] = ref;
            }
          }
          pend[c] = 0; npend--;
        }
        if (npend == 0) break;
        h1 *= 0.5; h2 *= 0.5;
      }
    }
  }

  printf("derivative gate: reported cusp_speed vs Richardson extrapolated\n");
  printf("central difference of the position in armc, %d dates (stride %d,"
         " %d random + %d degenerate)\n", nd, stride, nd - HARNESS_NDEGEN, HARNESS_NDEGEN);
  printf("seed 0x%016llX, ephemeris fingerprint %016llX\n\n",
         (unsigned long long)HARNESS_SEED, (unsigned long long)fp);
  printf("frames: %ld usable (%ld clean, %ld 'P' returned ERR with armc intact,"
         " %ld carrying their own armc), %ld dropped (%ld no armc, %ld no obliquity),"
         " %ld with the Sun out of range\n\n",
         dates_seen, fs.ok, fs.err_usable, fs.degen, fs.skip_armc + fs.skip_eclnut,
         fs.skip_armc, fs.skip_eclnut, fs.sun_out);
  printf("hsys       judged     mean rel     worst rel  cusp     geolat       armc"
         "     analytic      numeric  sentinel nonfin\n");
  for (k = 0; k < HARNESS_NSYS; k++) {
    printf("  %c   %10ld  %11.3e  %12.3e  %4d %10.4f %10.4f %12.4f %12.4f  %8ld %6ld\n",
           HARNESS_SYSTEMS[k], cnt[k],
           cnt_r[k] ? sum_r[k] / cnt_r[k] : 0.0, worst[k], wcusp[k], wlat[k], warmc[k],
           wgot[k], wref[k], sentinels[k], nonfinite[k]);
  }
  /* What the gate declined to judge, and why. A gate that silently drops the
   * hard points reads as "all clear" when it is really "not looked at". Every
   * `continue` above lands in one of these columns, and `unaccounted` proves
   * it: judged + every reject column must equal what was attempted. */
  printf("\nnot judged, by reason (the reference, not the formula, is at fault):\n");
  printf("hsys   attempted  unconverged   ref sentinel  ref nonfin    rc mix"
         "  pos sentinel  pos nonfin  pos waived   waived  unaccounted\n");
  for (k = 0; k < HARNESS_NSYS; k++) {
    long acct = cnt[k] + sentinels[k] + nonfinite[k] + steep_cnt[k] + rej_unconv[k]
              + rej_small[k] + rej_refsent[k] + rej_refnonfin[k] + rej_rcmix[k]
              + waived[k] + bnd_any[k];
    long un = attempted[k] - acct;
    unaccounted_total += un;
    printf("  %c   %9ld %12ld %14ld %11ld %9ld %13ld %11ld %11ld %8ld %12ld\n",
           HARNESS_SYSTEMS[k], attempted[k], rej_unconv[k],
           rej_refsent[k], rej_refnonfin[k], rej_rcmix[k],
           pos_sent[k], pos_nonfin[k], pos_waived[k], waived[k], un);
  }

  /* The |ref| < 1 class, judged on absolute error. This is where the cusps
   * that stand still live: 'W' and 'N' are entirely here, and nothing else in
   * this mode looks at them at all. And the |ref| > 1e4 class, judged on the
   * ratio, which used to be discarded outright. */
  printf("\nreferences below 1 deg/day (absolute error) and above 1e4 (ratio):\n");
  printf("hsys       judged   worst abs deg/day  cusp     reported    reference"
         "     steep     |got/ref-1|     reported    reference\n");
  for (k = 0; k < HARNESS_NSYS; k++)
    printf("  %c   %10ld  %18.3e  %4d %12.6f %12.6f %9ld  %14.3e %12.1f %12.1f\n",
           HARNESS_SYSTEMS[k], small_cnt[k], small_worst[k], small_wcusp[k],
           small_wgot[k], small_wref[k], steep_cnt[k], steep_worst[k],
           steep_wgot[k], steep_wref[k]);

  /* The per-bucket medians. One bucket is one (letter, cusp, 10-degree band),
   * and the median of its metric is the number a band-local error cannot hide
   * from. Buckets smaller than 16 comparisons are not gated -- a median of
   * three points is one point -- but the SHARE of comparisons living in them
   * is, so a defect cannot be hidden by scattering it into small buckets. */
  {
    long b;
    long nb = (long)HARNESS_NSYS * HARNESS_MAXCUSP * nband;
    for (b = 0; b < nb; b++) {
      size_t base = (size_t)b * DERIV_NBIN;
      long tot = 0, run = 0;
      int j, mb = -1, pb = -1, qb = -1, bk;
      for (j = 0; j < DERIV_NBIN; j++) tot += hist[base + j];
      if (tot == 0) continue;
      if (tot < DERIV_MINBUCKET) { tiny_bucket_n += tot; continue; }
      for (j = 0; j < DERIV_NBIN; j++) {
        run += hist[base + j];
        if (mb < 0 && run * 2 >= tot) mb = j;
        if (qb < 0 && run * 5 >= tot * 4) qb = j;
        if (run * 10 >= tot * 9) { pb = j; break; }
      }
      if (mb < 0 || pb < 0 || qb < 0) continue;
      bk = (int)(b / (HARNESS_MAXCUSP * nband));
      if (deriv_bin_upper(mb) > med_sys[bk]) med_sys[bk] = deriv_bin_upper(mb);
      if (deriv_bin_upper(mb) > med_worst) {
        med_worst = deriv_bin_upper(mb);
        med_k = bk;
        med_c = (int)((b / nband) % HARNESS_MAXCUSP);
        med_band = (int)(b % nband);
        med_n = tot;
      }
      if (deriv_bin_upper(pb) > p90_sys[bk]) p90_sys[bk] = deriv_bin_upper(pb);
      if (deriv_bin_upper(pb) > p90_worst) {
        p90_worst = deriv_bin_upper(pb);
        p90_k = bk;
        p90_c = (int)((b / nband) % HARNESS_MAXCUSP);
        p90_band = (int)(b % nband);
        p90_n = tot;
      }
      if (deriv_bin_upper(qb) > p80_worst) {
        p80_worst = deriv_bin_upper(qb);
        p80_k = bk;
        p80_c = (int)((b / nband) % HARNESS_MAXCUSP);
        p80_band = (int)(b % nband);
        p80_n = tot;
      }
    }
    printf("\nsensitivity floor, per house letter: the largest MEDIAN of any one\n"
           "(cusp, %.0f-degree latitude band) bucket. A systematic error confined to\n"
           "one cusp above one latitude has to exceed its letter's number here to\n"
           "move the median at all, and the gate stands at %.0e.\n",
           180.0 / nband, (double)GATE_DERIV_BUCKET_MED);
    for (k = 0; k < HARNESS_NSYS; k++) {
      printf("  %c %9.3e ", HARNESS_SYSTEMS[k], med_sys[k]);
      if ((k % 6) == 5 || k == HARNESS_NSYS - 1) printf("\n");
    }
    printf("\nthe same buckets at the 90th percentile, which is what a defect confined to\n"
           "part of a bucket -- one armc range, one branch -- moves while the median does\n"
           "not. The gate stands at %.0e here and at %.0e on the 80th percentile.\n",
           (double)GATE_DERIV_BUCKET_P90, (double)GATE_DERIV_BUCKET_P80);
    for (k = 0; k < HARNESS_NSYS; k++) {
      printf("  %c %9.3e ", HARNESS_SYSTEMS[k], p90_sys[k]);
      if ((k % 6) == 5 || k == HARNESS_NSYS - 1) printf("\n");
    }
  }

  printf("\noutside the domain of the formulae (a pole, an obliquity of exactly 0 or 90,\n"
         "or the horizon exactly tangent to the ecliptic). Every one of these rows is\n"
         "computed, and its slots, its finiteness and its return codes are gated exactly\n"
         "as any other row's are. What is NOT held to 1e-8 is the derivative agreement,\n"
         "because a cusp has no derivative at those points -- the positions are\n"
         "discontinuous there. What IS gated is the SHARE that still agrees: a change\n"
         "that broke the formulae generally would drive that share down, and a change\n"
         "that only fails at the singular point itself cannot hide in here, because the\n"
         "singular point is one row and the share is over all of them.\n");
  printf("  %ld comparisons: %ld relative, %ld absolute (|ref|<1), %ld ratio (|ref|>1e4)\n",
         bnd_n + bnd_steep_n, bnd_rel_n, bnd_small_n, bnd_steep_n);
  printf("  %ld not judged: %ld unconverged, %ld rc mix, %ld reference sentinel,"
         " %ld reference non-finite\n",
         bnd_unconv + bnd_rcmix + bnd_refsent + bnd_refnonfin,
         bnd_unconv, bnd_rcmix, bnd_refsent, bnd_refnonfin);
  printf("  worst relative %.3e", bnd_rel_worst);
  if (bnd_wk >= 0)
    printf("  (hsys '%c' cusp %d, geolat %.11f, armc %.9f, eps %.7f, %.4f vs %.4f)",
           HARNESS_SYSTEMS[bnd_wk], bnd_wc, bnd_wlat, bnd_warmc, bnd_weps,
           bnd_wgot, bnd_wref);
  printf("\n  worst absolute %.3e deg/day, worst ratio %.3e\n",
         bnd_small_worst, bnd_steep_worst);

  printf("\nVERDICT\n");
  {
    long tsent = 0, tnonfin = 0, trefsent = 0, tnojudge = 0, tpsent = 0, tpnf = 0;
    long twaived = 0, tsteep = 0;
    double wmean = 0, wsmall = 0, wworst = 0, wsteep = 0, wmean_d = 0;
    int wmeank = -1, wsmallk = -1, wworstk = -1, wsteepk = -1, wmean_dk = -1;
    int share_bad = 0, unconv_bad = 0, share_bad_d = 0, unconv_bad_d = 0;
    for (k = 0; k < HARNESS_NSYS; k++)
      if (small_worst[k] > wsmall) { wsmall = small_worst[k]; wsmallk = k; }
    for (k = 0; k < HARNESS_NSYS; k++) {
      tsent += sentinels[k]; tnonfin += nonfinite[k]; trefsent += rej_refsent[k];
      tsteep += steep_cnt[k];
      tpsent += pos_sent[k]; tpnf += pos_nonfin[k];
      twaived += waived[k] + pos_waived[k];
      if (cnt[k] + small_cnt[k] + steep_cnt[k] == 0) tnojudge++;
      if (worst[k] > wworst) { wworst = worst[k]; wworstk = k; }
      if (steep_worst[k] > wsteep) { wsteep = steep_worst[k]; wsteepk = k; }
      if (cnt_r[k]) {
        double m = sum_r[k] / cnt_r[k];
        if (m > wmean) { wmean = m; wmeank = k; }
      }
      if (cnt_dg[k]) {
        double m = sum_dg[k] / cnt_dg[k];
        if (m > wmean_d) { wmean_d = m; wmean_dk = k; }
      }
      {
        long ar = attempted[k] - att_d[k];
        long ad = att_d[k];
        if (ar > 0) {
          if (rej_rcmix[k] - rcmix_d[k] > GATE_DERIV_REJ_SHARE * ar
           || rej_refnonfin[k] - refnf_d[k] > GATE_DERIV_REJ_SHARE * ar
           || rej_refsent[k] - refsent_d[k] > 0) share_bad++;
          if (rej_unconv[k] - unconv_d[k] > GATE_DERIV_UNCONV_SHARE * ar) unconv_bad++;
        }
        if (ad > 0) {
          if (rcmix_d[k] > GATE_DERIV_REJ_SHARE_D * ad
           || refnf_d[k] > GATE_DERIV_REJ_SHARE_D * ad
           || refsent_d[k] > 0) share_bad_d++;
          if (unconv_d[k] > GATE_DERIV_UNCONV_SHARE_D * ad) unconv_bad_d++;
        }
      }
    }
    printf("  worst mean rel, random rows : %.3e", wmean);
    if (wmeank >= 0) printf("  (hsys '%c')", HARNESS_SYSTEMS[wmeank]);
    printf("   threshold %.0e  %s\n", (double)GATE_DERIV_MEAN_REL,
           wmean <= GATE_DERIV_MEAN_REL ? "pass" : "FAIL");
    if (wmean > GATE_DERIV_MEAN_REL) fail++;
    printf("  worst mean rel, degenerate  : %.3e", wmean_d);
    if (wmean_dk >= 0) printf("  (hsys '%c')", HARNESS_SYSTEMS[wmean_dk]);
    printf("   threshold %.0e  %s\n", (double)GATE_DERIV_MEAN_REL_D,
           wmean_d <= GATE_DERIV_MEAN_REL_D ? "pass" : "FAIL");
    if (wmean_d > GATE_DERIV_MEAN_REL_D) fail++;
    printf("  worst relative error        : %.3e", wworst);
    if (wworstk >= 0) printf("  (hsys '%c')", HARNESS_SYSTEMS[wworstk]);
    printf("   threshold %.0e  %s\n", (double)GATE_DERIV_WORST_REL,
           wworst <= GATE_DERIV_WORST_REL ? "pass" : "FAIL");
    if (wworst > GATE_DERIV_WORST_REL) fail++;
    printf("  worst absolute error, |ref|<1: %.3e deg/day", wsmall);
    if (wsmallk >= 0) printf("  (hsys '%c')", HARNESS_SYSTEMS[wsmallk]);
    printf("   threshold %.0e  %s\n", (double)GATE_DERIV_SMALL_ABS,
           wsmall <= GATE_DERIV_SMALL_ABS ? "pass" : "FAIL");
    if (wsmall > GATE_DERIV_SMALL_ABS) fail++;
    /* The steep class empties out as the stride grows -- 244 comparisons at
     * stride 1, 2 at stride 20, none at all at 200 or 2000 -- so a strided run
     * that printed "pass" here was reporting that nothing had been looked at.
     * It says so now instead. */
    printf("  worst ratio, |ref|>1e4      : ");
    if (tsteep == 0) {
      printf("gate not exercised at this stride (0 comparisons)\n");
    } else {
      printf("%.3e over %ld comparisons", wsteep, tsteep);
      if (wsteepk >= 0) printf("  (hsys '%c')", HARNESS_SYSTEMS[wsteepk]);
      printf("   threshold %.0e  %s\n", (double)GATE_DERIV_STEEP_RATIO,
             wsteep <= GATE_DERIV_STEEP_RATIO ? "pass" : "FAIL");
      if (wsteep > GATE_DERIV_STEEP_RATIO) fail++;
    }
    printf("  worst bucket median         : %.3e", med_worst);
    if (med_k >= 0)
      printf("  (hsys '%c' cusp %d, geolat %.0f..%.0f, %ld judged)",
             HARNESS_SYSTEMS[med_k], med_c,
             med_band * 180.0 / nband - 90.0, (med_band + 1) * 180.0 / nband - 90.0, med_n);
    printf("\n                                threshold %.0e  %s\n",
           (double)GATE_DERIV_BUCKET_MED,
           med_worst <= GATE_DERIV_BUCKET_MED ? "pass" : "FAIL");
    if (med_worst > GATE_DERIV_BUCKET_MED) fail++;
    printf("  worst bucket 80th percentile: %.3e", p80_worst);
    if (p80_k >= 0)
      printf("  (hsys '%c' cusp %d, geolat %.0f..%.0f, %ld judged)",
             HARNESS_SYSTEMS[p80_k], p80_c,
             p80_band * 180.0 / nband - 90.0, (p80_band + 1) * 180.0 / nband - 90.0, p80_n);
    printf("\n                                threshold %.0e  %s\n",
           (double)GATE_DERIV_BUCKET_P80,
           p80_worst <= GATE_DERIV_BUCKET_P80 ? "pass" : "FAIL");
    if (p80_worst > GATE_DERIV_BUCKET_P80) fail++;
    printf("  worst bucket 90th percentile: %.3e", p90_worst);
    if (p90_k >= 0)
      printf("  (hsys '%c' cusp %d, geolat %.0f..%.0f, %ld judged)",
             HARNESS_SYSTEMS[p90_k], p90_c,
             p90_band * 180.0 / nband - 90.0, (p90_band + 1) * 180.0 / nband - 90.0, p90_n);
    printf("\n                                threshold %.0e  %s\n",
           (double)GATE_DERIV_BUCKET_P90,
           p90_worst <= GATE_DERIV_BUCKET_P90 ? "pass" : "FAIL");
    if (p90_worst > GATE_DERIV_BUCKET_P90) fail++;
    printf("  comparisons in tiny buckets : %ld of %ld (%.4f%%, gate %.0f%%)   %s\n",
           tiny_bucket_n, judged_total,
           judged_total ? 100.0 * tiny_bucket_n / judged_total : 0.0,
           GATE_DERIV_TINYBUCKET_SHARE * 100.0,
           (judged_total && tiny_bucket_n > GATE_DERIV_TINYBUCKET_SHARE * judged_total)
             ? "FAIL" : "pass");
    if (judged_total && tiny_bucket_n > GATE_DERIV_TINYBUCKET_SHARE * judged_total) fail++;
    {
      long bt = bnd_n + bnd_steep_n;
      long batt = bt + bnd_unconv + bnd_rcmix + bnd_refsent + bnd_refnonfin;
      double sh = bt ? (double)bnd_agree / bt : 1.0;
      double ush = batt ? (double)bnd_unconv / batt : 0.0;
      printf("  outside-domain agreeing to 1e-6: %ld of %ld (%.2f%%)"
             "   floor %.0f%%  %s\n", bnd_agree, bt, 100.0 * sh,
             GATE_DERIV_OUTSIDE_SHARE * 100.0,
             sh >= GATE_DERIV_OUTSIDE_SHARE ? "pass" : "FAIL");
      if (bt && sh < GATE_DERIV_OUTSIDE_SHARE) fail++;
      /* The denominator of the gate above, gated in its turn. Without this a
       * build could push the whole block into the unconverged column, leave
       * three comparisons behind, and pass the agreement gate on those three. */
      printf("  outside-domain unconverged  : %ld of %ld (%.2f%%)"
             "   ceiling %.0f%%  %s\n", bnd_unconv, batt, 100.0 * ush,
             GATE_DERIV_BND_UNCONV_SHARE * 100.0,
             ush <= GATE_DERIV_BND_UNCONV_SHARE ? "pass" : "FAIL");
      if (batt && ush > GATE_DERIV_BND_UNCONV_SHARE) fail++;
    }
    printf("  uninitialised speed slots   : %ld   %s\n", tsent, tsent ? "FAIL" : "pass");
    if (tsent) fail++;
    printf("  uninitialised position slots: %ld   %s\n", tpsent, tpsent ? "FAIL" : "pass");
    if (tpsent) fail++;
    printf("  non-finite speeds           : %ld   %s\n", tnonfin, tnonfin ? "FAIL" : "pass");
    if (tnonfin) fail++;
    printf("  non-finite positions        : %ld   %s\n", tpnf, tpnf ? "FAIL" : "pass");
    if (tpnf) fail++;
    printf("  non-finite, waived at a pole: %ld   (Sunshine 'I' within 1e-10 deg of"
           " +-90; identical in the pristine tree)\n", twaived);
    printf("  uninitialised reference cusp: %ld   %s\n", trefsent, trefsent ? "FAIL" : "pass");
    if (trefsent) fail++;
    printf("  random rows, letters losing over %.0f%% to rc mix / non-finite reference: %d   %s\n",
           GATE_DERIV_REJ_SHARE * 100.0, share_bad, share_bad ? "FAIL" : "pass");
    if (share_bad) fail++;
    printf("  random rows, letters losing over %.0f%% to an unconverged reference     : %d   %s\n",
           GATE_DERIV_UNCONV_SHARE * 100.0, unconv_bad, unconv_bad ? "FAIL" : "pass");
    if (unconv_bad) fail++;
    printf("  degenerate rows, letters over %.0f%% rc mix / non-finite reference      : %d   %s\n",
           GATE_DERIV_REJ_SHARE_D * 100.0, share_bad_d, share_bad_d ? "FAIL" : "pass");
    if (share_bad_d) fail++;
    printf("  degenerate rows, letters over %.0f%% unconverged reference              : %d   %s\n",
           GATE_DERIV_UNCONV_SHARE_D * 100.0, unconv_bad_d, unconv_bad_d ? "FAIL" : "pass");
    if (unconv_bad_d) fail++;
    printf("  unaccounted comparisons     : %ld   %s\n", unaccounted_total,
           unaccounted_total ? "FAIL" : "pass");
    if (unaccounted_total) fail++;
    printf("  letters with nothing judged : %ld   %s\n", tnojudge, tnojudge ? "FAIL" : "pass");
    if (tnojudge) fail++;
    /* A whole DATE dropped before any letter is walked. This was printed in the
     * frames line and gated by nothing, which made it the last way to empty the
     * mode without failing it: poisoning the frame probe for |geolat| > 60
     * dropped 8,459 dates -- 33% of the sample, every one of them polar -- and
     * this mode still exited 0, because a bucket that loses all its comparisons
     * is not a tiny bucket, it is no bucket, so the tiny-bucket share read
     * 0.0000%. Neither reason ever fires on the shipping stack, at any stride,
     * so the gate is zero rather than a share: 'P' always returns a usable armc
     * and SE_ECL_NUT always answers. */
    {
      long dropped = fs.skip_armc + fs.skip_eclnut;
      printf("  dates dropped by the frame  : %ld   (%ld no armc, %ld no obliquity)   %s\n",
             dropped, fs.skip_armc, fs.skip_eclnut, dropped ? "FAIL" : "pass");
      if (dropped) fail++;
    }
    printf("\n%s\n", fail ? "deriv: FAILED" : "deriv: passed");
  }
  free(hist);
  free(S);
  return fail ? 1 : 0;
}

/* ---------------------------------------------------------------- claims -- */

/* Count the sentinels and the non-finite values in one output array. The
 * previous version scanned only cusp_speed, so the harness's own headline
 * finding -- that 'G' leaves 24 positions as well as 24 speeds unwritten --
 * was half invisible to the mode that reports uninitialised slots. */
static void scan_slots(const double *v, int lo, int hi, long *sent, long *nonfin,
                       int hsys, double geolat, long *waived)
{
  int c;
  for (c = lo; c <= hi; c++) {
    if (harness_is_sentinel(v[c])) (*sent)++;
    else if (!harness_finite(v[c])) {
      if (harness_pole_nan_waived(hsys, geolat)) (*waived)++;
      else (*nonfin)++;
    }
  }
}

/* Relative difference with a scale floor, so that a speed of a million degrees
 * a day and a speed of a tenth are held to the same statement. Two non-finite
 * values are NOT a difference here -- being non-finite at all is gated by the
 * slot scan, and counting it twice would only make the second gate redundant --
 * but one non-finite against one finite is the largest difference there is. */
static double reldiff(double a, double b)
{
  double s = fabs(a) > fabs(b) ? fabs(a) : fabs(b);
  double d;
  if (!harness_finite(a) && !harness_finite(b)) return 0.0;
  if (!harness_finite(a) || !harness_finite(b)) return 1e300;
  if (s < 1.0) s = 1.0;
  d = fabs(a - b) / s;
  if (!(d >= 0.0)) return 1e300;
  return d;
}

/* One SEFLG_RADIANS value against the same value in degrees. */
static void rad_check(double got, double deg, int k, long *n, long *bad,
                      double *worst, int *worstk)
{
  double d;
  if (harness_is_sentinel(got) || harness_is_sentinel(deg)) return;
  d = reldiff(got, deg * DEGTORAD);
  (*n)++;
  if (d > *worst) { *worst = d; *worstk = k; }
  if (d > GATE_RADIANS_REL) (*bad)++;
}

typedef struct {
  long rows, err_rows;
  long fb_rows, fb_bad, fb_no_porphyry;
  long sent_cusp, sent_speed, sent_ascmc, sent_ascmc_speed, waived;
  long nf_cusp, nf_speed, nf_ascmc, nf_ascmc_speed;
  long idx0_nonzero_cusp, idx0_nonzero_speed;
} pass_stats;

/* One flag set, one date, one letter: call it, scan every output array, and
 * -- where the call reported ERR -- hold the twelve cusps it returned against
 * what Porphyry returns for the same call. Every system that fails substitutes
 * Porphyry, so that claim covers all 26 letters rather than the two Sunshine
 * ones the previous version checked, and it is the only thing that looks
 * inside sidereal_houses_ecl_t0() and sidereal_houses_ssypl() at all. */
static void claims_scan_pass(const harness_sample *s, int hs, int nc,
                             const harness_flagset *fset, pass_stats *ps,
                             double *cusp, double *cs, double *ascmc, double *as,
                             int *rc_out)
{
  double cO[HARNESS_MAXCUSP + 4], sO[HARNESS_MAXCUSP + 4], aO[12], asO[12];
  char serr[AS_MAXCH];
  int rc, c;

  harness_fill_sentinel(cusp, HARNESS_MAXCUSP + 4);
  harness_fill_sentinel(cs, HARNESS_MAXCUSP + 4);
  harness_fill_sentinel(ascmc, 12);
  harness_fill_sentinel(as, 12);
  serr[0] = '\0';
  rc = swe_houses_ex2(s->jd, (int32)fset->iflag, s->geolat, s->geolon, hs,
                      cusp, ascmc, cs, as, serr);
  *rc_out = rc;
  ps->rows++;
  scan_slots(cusp, 1, nc, &ps->sent_cusp, &ps->nf_cusp, hs, s->geolat, &ps->waived);
  scan_slots(cs, 1, nc, &ps->sent_speed, &ps->nf_speed, hs, s->geolat, &ps->waived);
  scan_slots(ascmc, 0, 9, &ps->sent_ascmc, &ps->nf_ascmc, hs, s->geolat, &ps->waived);
  scan_slots(as, 0, 9, &ps->sent_ascmc_speed, &ps->nf_ascmc_speed, hs, s->geolat, &ps->waived);
  /* swehouse.c writes a literal zero into cusp[0] and cusp_speed[0] before it
   * does anything else. Anything else there is the library scribbling outside
   * the range it documents, which no amount of sentinel scanning would show
   * because the slot IS written -- just wrongly. */
  if (!harness_is_sentinel(cusp[0]) && cusp[0] != 0.0) ps->idx0_nonzero_cusp++;
  if (!harness_is_sentinel(cs[0]) && cs[0] != 0.0) ps->idx0_nonzero_speed++;

  if (rc >= 0) return;
  ps->err_rows++;
  if (hs == 'O' || hs == 'o') return;     /* Porphyry is what it falls back TO */
  harness_fill_sentinel(cO, HARNESS_MAXCUSP + 4);
  harness_fill_sentinel(sO, HARNESS_MAXCUSP + 4);
  harness_fill_sentinel(aO, 12); harness_fill_sentinel(asO, 12);
  serr[0] = '\0';
  if (swe_houses_ex2(s->jd, (int32)fset->iflag, s->geolat, s->geolon, 'O',
                     cO, aO, sO, asO, serr) < 0) {
    /* Porphyry itself failed, so there is no reference to hold the fallback
     * against. That is a legitimate reason to say nothing -- but it used to be
     * an unlabelled `return`, so a change that made 'O' fail everywhere would
     * have emptied this backstop of every row it checks and still printed
     * "0 of 0 failed". The abandoned rows are counted now. */
    ps->fb_no_porphyry++;
    return;
  }
  ps->fb_rows++;
  for (c = 1; c <= 12; c++)
    if (!harness_same(cusp[c], cO[c]) || !harness_same(cs[c], sO[c])) {
      ps->fb_bad++;
      return;
    }
  /* 'G' clears 13..36 on failure rather than leaving the caller's stack. */
  if (hs == 'G')
    for (c = 13; c <= 36; c++)
      if (cusp[c] != 0.0 || cs[c] != 0.0) { ps->fb_bad++; return; }
}

static void claims_print_pass(const char *name, const pass_stats *p)
{
  printf("  %-7s rows %8ld  ERR %7ld | sentinel cusp %ld speed %ld ascmc %ld ascmc_speed %ld"
         " | non-finite %ld/%ld/%ld/%ld (waived %ld)"
         " | index0 nonzero %ld/%ld | Porphyry fallback %ld of %ld carried"
         " (%ld rows had no 'O' reference)\n",
         name, p->rows, p->err_rows,
         p->sent_cusp, p->sent_speed, p->sent_ascmc, p->sent_ascmc_speed,
         p->nf_cusp, p->nf_speed, p->nf_ascmc, p->nf_ascmc_speed, p->waived,
         p->idx0_nonzero_cusp, p->idx0_nonzero_speed,
         p->fb_rows - p->fb_bad, p->fb_rows, p->fb_no_porphyry);
}

static int mode_claims(int stride)
{
  harness_sample *S = (harness_sample *)malloc(sizeof(harness_sample) * HARNESS_NDATES);
  double c1[HARNESS_MAXCUSP + 4], s1[HARNESS_MAXCUSP + 4], a1[12], as1[12];
  double c2[HARNESS_MAXCUSP + 4], s2[HARNESS_MAXCUSP + 4], a2[12], as2[12];
  double cR[HARNESS_MAXCUSP + 4], sR[HARNESS_MAXCUSP + 4], aR[12], asR[12];
  double cS[HARNESS_MAXCUSP + 4], sS[HARNESS_MAXCUSP + 4], aS[12], asS[12];
  double cI[HARNESS_MAXCUSP + 4], sI[HARNESS_MAXCUSP + 4];
  int rcf[HARNESS_NFLAGSETS];
  char e1[AS_MAXCH], e2[AS_MAXCH];
  pass_stats ps[HARNESS_NFLAGSETS];
  long entry_rows = 0, entry_bad = 0, entry_gate_bad = 0, entry_speed_bad = 0;
  long entry_bad_sys[HARNESS_NSYS];
  long entry_skipped = 0;
  double entry_worst = 0, entry_speed_worst = 0;
  int entry_worst_sys = 0, entry_speed_worst_sys = 0;
  long rad_n = 0, rad_bad = 0; double rad_worst = 0; int rad_worst_sys = 0;
  long sid_rows = 0, sid_speed_same = 0, sid_rate_bad = 0, sid_pos_bad = 0;
  long sid_frac_bad = 0, sid_frac_n = 0, sid_skipped = 0;
  long sid_pin_n = 0, sid_pin_bad = 0;
  double sid_rate_worst = 0, sid_pos_worst = 0, sid_rate_seen = 0, sid_frac_worst = 0;
  long total_speeds = 0;
  long over[4] = {0, 0, 0, 0};
  const double th[4] = {1e3, 1e4, 1e5, 1e6};
  long ascmc_rows = 0, ascmc_sent = 0, ascmc_skipped = 0, ascmc_err = 0;
  /* The denominator the two discard columns are a share OF: every (row, index)
   * pair that reached the derivative test at all, which is the seven indices
   * that are neither armc nor outside SE_NASCMC. Split random from degenerate,
   * because the degenerate block is a fixed 396 dates and one threshold over
   * the mixture would mean something different at every stride. */
  long ascmc_slots = 0, ascmc_slots_d = 0;
  long ascmc_skip_d = 0, ascmc_unconv_d = 0;
  long ascmc_unconv = 0, ascmc_judged[10], ascmc_small[10];
  double ascmc_worst[10], ascmc_small_worst[10];
  long armcs_bad = 0, ascmc89_bad = 0;
  long ascmc_bnd_n = 0, ascmc_bnd_agree = 0;
  double ascmc_bnd_worst = 0;
  frame_stats fs;
  long dates_seen = 0, fail = 0;
  int n, i, k, c, j, nd, f, bnd;
  uint64_t fp;

  if (S == NULL) { fprintf(stderr, "claims: out of memory\n"); return 2; }
  memset(&fs, 0, sizeof fs);
  memset(ps, 0, sizeof ps);
  for (j = 0; j < 10; j++) {
    ascmc_worst[j] = 0; ascmc_judged[j] = 0;
    ascmc_small_worst[j] = 0; ascmc_small[j] = 0;
  }
  for (j = 0; j < HARNESS_NSYS; j++) entry_bad_sys[j] = 0;
  harness_samples(S, HARNESS_NDATES);
  fp = harness_ephe_fingerprint();

  nd = harness_ndates_used(stride);
  for (n = 0; n < nd; n++) {
    double armc, eps, sundec;
    int sun_ok, fr;
    i = harness_date_at(n, stride);
    fr = frame_of(&S[i], &armc, &eps, &sundec, &sun_ok, &fs);
    if (fr == 0) continue;
    dates_seen++;
    bnd = harness_outside_domain(S[i].geolat, eps, armc);

    for (k = 0; k < HARNESS_NSYS; k++) {
      int hs = (unsigned char)HARNESS_SYSTEMS[k];
      int nc = harness_ncusp(hs), rc1, rc2;

      /* Every flag set, scanned and gated the same way. Before this the
       * harness ran the tropical path only, so the SEFLG_RADIANS block and
       * two of the three sidereal functions were never entered at all. */
      for (f = 0; f < HARNESS_NFLAGSETS; f++) {
        harness_apply_flagset(&HARNESS_FLAGSETS[f]);
        if (f == 0)
          claims_scan_pass(&S[i], hs, nc, &HARNESS_FLAGSETS[f], &ps[f], c1, s1, a1, as1, &rcf[f]);
        else if (f == 1)
          claims_scan_pass(&S[i], hs, nc, &HARNESS_FLAGSETS[f], &ps[f], cR, sR, aR, asR, &rcf[f]);
        else if (f == 2)
          claims_scan_pass(&S[i], hs, nc, &HARNESS_FLAGSETS[f], &ps[f], cS, sS, aS, asS, &rcf[f]);
        else
          claims_scan_pass(&S[i], hs, nc, &HARNESS_FLAGSETS[f], &ps[f], c2, s2, a2, as2, &rcf[f]);
      }
      rc1 = rcf[0];
      harness_apply_flagset(&HARNESS_FLAGSETS[0]);
      /* sidereal_houses_trad() upper-cases the letter before it computes, so
       * the sidereal answer for 'i' is the sidereal answer for 'I'. The
       * identity below therefore has to hold the sidereal 'i' against the
       * TROPICAL 'I', not against the tropical 'i'; held against the wrong one
       * it fails on every row and says nothing about the ayanamsa. 'I' is
       * walked one letter earlier, so its tropical result is still to hand. */
      if (hs == 'I') {
        memcpy(cI, c1, sizeof cI);
        memcpy(sI, s1, sizeof sI);
      }

      /* claim: SEFLG_RADIANS converts every value it returns, positions AND
       * speeds, by exactly DEGTORAD. The conversion is a multiplication the
       * library performs on the degree result, so this is bit-exact and the
       * threshold is only a guard against a compiler reassociating it. Nothing
       * in any mode looked at this path before; the whole speed half of it
       * could have been missing, and was. */
      for (c = 1; c <= nc; c++) {
        rad_check(cR[c], c1[c], k, &rad_n, &rad_bad, &rad_worst, &rad_worst_sys);
        rad_check(sR[c], s1[c], k, &rad_n, &rad_bad, &rad_worst, &rad_worst_sys);
      }
      for (j = 0; j < 8; j++) {
        rad_check(aR[j], a1[j], k, &rad_n, &rad_bad, &rad_worst, &rad_worst_sys);
        rad_check(asR[j], as1[j], k, &rad_n, &rad_bad, &rad_worst, &rad_worst_sys);
      }

      /* claim: the date entry point and the armc entry point agree when handed
       * the same true obliquity -- in the SPEEDS as well as the positions,
       * which the previous version never compared at all. */
      harness_fill_sentinel(c2, HARNESS_MAXCUSP + 4);
      harness_fill_sentinel(s2, HARNESS_MAXCUSP + 4);
      for (j = 0; j < 10; j++) a2[j] = 0;
      a2[9] = sundec;
      harness_fill_sentinel(as2, 12); e2[0] = '\0';
      rc2 = swe_houses_armc_ex2(armc, S[i].geolat, eps, hs, c2, a2, s2, as2, e2);

      /* Skip only the rows where the two calls were not given the same
       * problem: beyond the solar ephemeris the date entry point fails over to
       * Porphyry and reports ERR while the armc entry point, handed a
       * declination directly, computes Sunshine and succeeds. That is a
       * Sunshine story, so it skips the two Sunshine letters -- not, as
       * before, all 26 of them on 21.66% of the dates. */
      entry_rows++;
      if (rc1 != rc2 || (!sun_ok && (hs == 'I' || hs == 'i')) || S[i].fixed) {
        entry_skipped++;
      } else {
        double worst = 0, sworst = 0;
        for (c = 1; c <= nc; c++) {
          double d;
          if (harness_is_sentinel(c1[c]) || harness_is_sentinel(c2[c])) continue;
          d = fabs(swe_difdeg2n(c1[c], c2[c]));
          if (!harness_finite(d)) { d = 1e300; }   /* a NaN cusp is not "no difference" */
          if (d > worst) worst = d;
          if (harness_is_sentinel(s1[c]) || harness_is_sentinel(s2[c])) continue;
          d = reldiff(s1[c], s2[c]);
          if (d > sworst) sworst = d;
        }
        for (j = 0; j < 8; j++) {
          double d;
          if (harness_is_sentinel(as1[j]) || harness_is_sentinel(as2[j])) continue;
          d = reldiff(as1[j], as2[j]);
          if (d > sworst) sworst = d;
        }
        if (worst > entry_worst) { entry_worst = worst; entry_worst_sys = k; }
        if (sworst > entry_speed_worst) { entry_speed_worst = sworst; entry_speed_worst_sys = k; }
        if (worst > REPORT_ENTRY_DEG) { entry_bad++; entry_bad_sys[k]++; }
        if (worst > GATE_ENTRY_DEG) entry_gate_bad++;
        if (sworst > GATE_ENTRY_SPEED_REL) entry_speed_bad++;
      }

      for (c = 1; c <= nc; c++) {
        if (harness_is_sentinel(s1[c])) continue;
        total_speeds++;
        if (harness_finite(s1[c]))
          for (j = 0; j < 4; j++) if (fabs(s1[c]) > th[j]) over[j]++;
      }

      /* The sidereal path must subtract the ayanamsa from the positions AND
       * its rate from the speeds. The old check asked only "did the speeds
       * change at all", which a rate wrong by a factor of a thousand, or of
       * the wrong sign, or applied to the wrong cusps, passes without a
       * murmur. What is checked now is the identity itself:
       *
       *     sidereal position = tropical position - ayanamsa(t)
       *     sidereal speed    = tropical speed    - d(ayanamsa)/dt
       *
       * twice over, because one reference alone would not settle it:
       *
       *   exact  the rate swehouse.c is defined to subtract, its own central
       *          difference at a step of 0.1 day. Matching this to 1e-10
       *          deg/day says the subtracted term IS that derivative and not
       *          something that merely resembles it.
       *   loose  a Richardson extrapolation over 0.5 and 0.25 day, which does
       *          not restate the library's arithmetic and so cannot be fooled
       *          by a defect that lives in the 0.1-day expression itself. The
       *          ayanamsa is not smooth everywhere, and on a small share of
       *          dates the wider stencil straddles a break and disagrees by a
       *          few per cent; that is the reference failing, not the library,
       *          so this side gates on the SHARE of comparisons outside 1%,
       *          not on any single one. A rate wrong by a factor, a sign or
       *          omitted entirely misses on every comparison, not on 1%.
       *
       * All 26 letters, not just 'P'. With 'P' alone, 25 of 26 were unchecked
       * and a defect that spared Placidus and multiplied the rate by a thousand
       * everywhere else was invisible. 'W' and 'N' are held to the OTHER half
       * of the claim: their cusps are pinned to sidereal sign boundaries, so
       * their sidereal speeds must be exactly zero rather than shifted. */
      {
        double tjde, ay, aym, ayp, rate_lib, rate_ref, r1, r2;
        double am1, ap1, am2, ap2;
        int usable, ito, pinned = (hs == 'W' || hs == 'N');
        int identical = 1;
        const double *ct = c1, *st = s1;
        swe_set_sid_mode(SE_SIDM_FAGAN_BRADLEY, 0, 0);
        if (hs == 'i') { ct = cI; st = sI; }
        /* cS/sS/aS/asS already hold the trad sidereal pass for this row.
         * Gauquelin's 13..36 exist only when the call succeeded on BOTH
         * sides; on failure swehouse.c clears them to a literal zero and they
         * are "not computed", not a cusp standing at zero degrees Aries. */
        ito = (nc > 12 && rcf[0] >= 0 && rcf[2] >= 0) ? nc : 12;
        for (c = 1; c <= 12; c++) if (!harness_same(sS[c], st[c])) { identical = 0; break; }
        sid_rows++;
        if (identical) sid_speed_same++;

        tjde = S[i].jd + swe_deltat_ex(S[i].jd, -1, NULL);
        usable = swe_get_ayanamsa_ex(tjde, SEFLG_SIDEREAL, &ay, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde - 0.1, SEFLG_SIDEREAL, &aym, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde + 0.1, SEFLG_SIDEREAL, &ayp, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde - 0.5, SEFLG_SIDEREAL, &am1, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde + 0.5, SEFLG_SIDEREAL, &ap1, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde - 0.25, SEFLG_SIDEREAL, &am2, NULL) >= 0
              && swe_get_ayanamsa_ex(tjde + 0.25, SEFLG_SIDEREAL, &ap2, NULL) >= 0;
        if (!usable) {
          sid_skipped++;
        } else {
          rate_lib = swe_difdeg2n(ayp, aym) / 0.2;
          r1 = swe_difdeg2n(ap1, am1) / 1.0;
          r2 = swe_difdeg2n(ap2, am2) / 0.5;
          rate_ref = (4 * r2 - r1) / 3;
          if (fabs(rate_lib) > sid_rate_seen) sid_rate_seen = fabs(rate_lib);
          for (c = 1; c <= ito; c++) {
            double dp, dv, dvr, got;
            if (harness_is_sentinel(cS[c]) || harness_is_sentinel(ct[c])
             || harness_is_sentinel(sS[c]) || harness_is_sentinel(st[c])) {
              sid_skipped++; continue;
            }
            if (pinned) {
              /* Pinned to sidereal sign boundaries: the speed is zero, and
               * exactly zero, not merely small. This is the whole content of
               * the whole-sign hunk in sidereal_houses_trad(), and nothing in
               * the harness looked at it while only 'P' was tested. */
              sid_pin_n++;
              if (sS[c] != 0.0) sid_pin_bad++;
              continue;
            }
            got = st[c] - sS[c];              /* what the library subtracted */
            dp  = fabs(swe_difdeg2n(swe_degnorm(ct[c] - ay), cS[c]));
            dv  = fabs(got - rate_lib);
            dvr = fabs(got - rate_ref);
            if (!harness_finite(dp)) dp = 1e300;
            if (!harness_finite(dv)) dv = 1e300;
            if (!harness_finite(dvr)) dvr = 1e300;
            if (dp > sid_pos_worst) sid_pos_worst = dp;
            if (dv > sid_rate_worst) sid_rate_worst = dv;
            if (fabs(rate_ref) > 0 && dvr / fabs(rate_ref) > sid_frac_worst)
              sid_frac_worst = dvr / fabs(rate_ref);
            sid_frac_n++;
            if (dp > GATE_SID_POS_DEG) sid_pos_bad++;
            if (dv > GATE_SID_RATE_DEGDAY) sid_rate_bad++;
            if (dvr > GATE_SID_RATE_FRAC * fabs(rate_ref) + 1e-12) sid_frac_bad++;
          }
        }
        harness_apply_flagset(&HARNESS_FLAGSETS[0]);
      }
    }

    /* claim: every ascmc_speed entry is the derivative of its ascmc entry */
    {
      /* Same construction as the cusp gate, and for the same reason: a single
       * central difference at one step size cannot tell its own truncation
       * error from the library's error, so it convicts on both. Richardson
       * extrapolation over two steps plus the convergence test below drops the
       * reference's own error by three orders and makes a 1e-6 threshold mean
       * something. Every rejection is counted; none is silent.
       *
       * Three holes are closed here. A reference below one degree per day was
       * skipped outright, which is the same blind spot the cusp gate had; it is
       * now judged on absolute error. Index 2, armc, was skipped as "armc
       * itself" and is now held against the exact constant the library is
       * defined to return. And the loop stopped at 8, so ascmc_speed[8] and
       * [9] -- which swehouse.c sets to zero -- were never looked at. */
      double aM[12], asM[12], aP1[12], aQ1[12], aP2[12], aQ2[12];
      double cM[HARNESS_MAXCUSP + 4];
      const double h1 = 1e-3, h2 = 5e-4;
      int rcM;
      for (j = 0; j < 10; j++) {
        aM[j] = 0; aP1[j] = 0; aQ1[j] = 0; aP2[j] = 0; aQ2[j] = 0;
      }
      aM[9] = sundec; aP1[9] = sundec; aQ1[9] = sundec;
      aP2[9] = sundec; aQ2[9] = sundec;
      harness_fill_sentinel(asM, 12);
      harness_fill_sentinel(cM, HARNESS_MAXCUSP + 4);
      /* Placidus inside the polar circle reports ERR and substitutes Porphyry.
       * ascmc is computed either way and is the real one, so ERR is recorded
       * rather than used as a reason to stop -- but it is recorded, which the
       * previous `< -1` test (ERR is -1) never was. */
      rcM = swe_houses_armc_ex2(armc, S[i].geolat, eps, 'P', cM, aM, NULL, asM, e1);
      if (rcM < 0) ascmc_err++;
      swe_houses_armc_ex2(swe_degnorm(armc + h1), S[i].geolat, eps, 'P', cM, aP1, NULL, NULL, e1);
      swe_houses_armc_ex2(swe_degnorm(armc - h1), S[i].geolat, eps, 'P', cM, aQ1, NULL, NULL, e1);
      swe_houses_armc_ex2(swe_degnorm(armc + h2), S[i].geolat, eps, 'P', cM, aP2, NULL, NULL, e1);
      swe_houses_armc_ex2(swe_degnorm(armc - h2), S[i].geolat, eps, 'P', cM, aQ2, NULL, NULL, e1);
      for (j = 0; j < 10; j++) {
        double d1, d2, ref, rel;
        if (harness_is_sentinel(asM[j])) { ascmc_sent++; continue; }
        if (j == 2) {
          /* armc advances at exactly ARMCS, the constant swehouse.c defines
           * and the harness restates. Not a derivative claim, an identity. */
          ascmc_judged[j]++;
          if (!harness_same(asM[j], ARMCS)) armcs_bad++;
          continue;
        }
        if (j >= 8) {
          /* Outside SE_NASCMC. swehouse.c sets both the value and the speed to
           * a literal zero, so anything else is the library writing where it
           * says it does not. */
          ascmc_judged[j]++;
          if (asM[j] != 0.0 || aM[j] != 0.0) ascmc89_bad++;
          continue;
        }
        ascmc_slots++;
        if (S[i].fixed) ascmc_slots_d++;
        d1 = swe_difdeg2n(aP1[j], aQ1[j]) / (2 * h1);
        d2 = swe_difdeg2n(aP2[j], aQ2[j]) / (2 * h2);
        ref = (4 * d2 - d1) / 3 * ARMCS;
        if (!harness_finite(ref) || fabs(ref) > 1e4) {
          ascmc_skipped++;
          if (S[i].fixed) ascmc_skip_d++;
          continue;
        }
        if (fabs(d2 - d1) > 1e-6 * (fabs(d2) + 1.0)) {
          ascmc_unconv++;
          if (S[i].fixed) ascmc_unconv_d++;
          continue;
        }
        if (fabs(ref) < 1.0) {
          double ae = fabs(asM[j] - ref);
          if (!harness_finite(ae)) ae = 1e300;
          if (bnd) {
            ascmc_bnd_n++;
            if (ae <= GATE_ASCMC_SMALL_ABS) ascmc_bnd_agree++;
            if (ae > ascmc_bnd_worst) ascmc_bnd_worst = ae;
          } else {
            ascmc_small[j]++;
            if (ae > ascmc_small_worst[j]) ascmc_small_worst[j] = ae;
          }
          continue;
        }
        rel = fabs(asM[j] - ref) / fabs(ref);
        if (!harness_finite(rel)) rel = 1e300;
        if (bnd) {
          ascmc_bnd_n++;
          if (rel <= GATE_ASCMC_REL) ascmc_bnd_agree++;
          if (rel > ascmc_bnd_worst) ascmc_bnd_worst = rel;
          continue;
        }
        ascmc_judged[j]++;
        if (rel > ascmc_worst[j]) ascmc_worst[j] = rel;
      }
      ascmc_rows++;
    }
  }

  printf("claims, %d dates (stride %d, %d random + %d degenerate), %ld frames usable\n",
         nd, stride, nd - HARNESS_NDEGEN, HARNESS_NDEGEN, dates_seen);
  printf("seed 0x%016llX, ephemeris fingerprint %016llX\n",
         (unsigned long long)HARNESS_SEED, (unsigned long long)fp);
  printf("frames: %ld clean, %ld 'P' returned ERR with armc intact, %ld carrying their own"
         " armc, %ld dropped, %ld with the Sun out of range\n\n",
         fs.ok, fs.err_usable, fs.degen, fs.skip_armc + fs.skip_eclnut, fs.sun_out);

  printf("  per flag set (every one of them entered, which only the first was before):\n");
  for (f = 0; f < HARNESS_NFLAGSETS; f++)
    claims_print_pass(HARNESS_FLAGSETS[f].name, &ps[f]);

  printf("\n  entry points, ex2 vs armc_ex2        : %ld of %ld rows over %.0e deg"
         " (%ld not comparable)\n", entry_bad, entry_rows - entry_skipped,
         (double)REPORT_ENTRY_DEG, entry_skipped);
  printf("      worst cusp difference %.3e deg, hsys '%c';"
         " worst speed difference %.3e relative, hsys '%c'\n",
         entry_worst, HARNESS_SYSTEMS[entry_worst_sys],
         entry_speed_worst, HARNESS_SYSTEMS[entry_speed_worst_sys]);
  if (entry_bad) {
    printf("      by system:");
    for (j = 0; j < HARNESS_NSYS; j++)
      if (entry_bad_sys[j]) printf(" %c=%ld", HARNESS_SYSTEMS[j], entry_bad_sys[j]);
    printf("\n");
  }
  printf("  SEFLG_RADIANS = degrees * DEGTORAD   : worst %.3e relative over %ld values,"
         " %ld over %.0e (hsys '%c')\n",
         rad_worst, rad_n, rad_bad, (double)GATE_RADIANS_REL,
         HARNESS_SYSTEMS[rad_worst_sys]);
  printf("  sidereal speeds identical to tropical: %ld of %ld  (all of them means the\n"
         "                                         ayanamsa's rate is never subtracted)\n",
         sid_speed_same, sid_rows);
  printf("  sidereal identity, sid = trop - ayanamsa (largest |rate| seen %.3e deg/day):\n",
         sid_rate_seen);
  printf("      positions: worst %.3e deg,     %ld over %.0e   %s\n",
         sid_pos_worst, sid_pos_bad, (double)GATE_SID_POS_DEG, sid_pos_bad ? "FAIL" : "pass");
  printf("      speeds vs swehouse's own d/dt : worst %.3e deg/day, %ld over %.0e   %s\n",
         sid_rate_worst, sid_rate_bad, (double)GATE_SID_RATE_DEGDAY,
         sid_rate_bad ? "FAIL" : "pass");
  printf("      speeds vs an independent d/dt : worst %.3e of the rate, %ld of %ld"
         " over %.0f%% (%.3f%% of comparisons, gate %.0f%%)   %s\n",
         sid_frac_worst, sid_frac_bad, sid_frac_n, GATE_SID_RATE_FRAC * 100.0,
         sid_frac_n ? 100.0 * sid_frac_bad / sid_frac_n : 0.0,
         GATE_SID_FRAC_SHARE * 100.0,
         (sid_frac_n && sid_frac_bad > GATE_SID_FRAC_SHARE * sid_frac_n) ? "FAIL" : "pass");
  printf("      'W' and 'N' pinned to zero    : %ld of %ld nonzero   %s\n",
         sid_pin_bad, sid_pin_n, sid_pin_bad ? "FAIL" : "pass");
  if (sid_skipped) printf("      %ld comparisons skipped (ayanamsa unavailable or slot unwritten)\n",
                          sid_skipped);
  printf("\n  cusp speed magnitude, share of %ld written values:\n", total_speeds);
  for (j = 0; j < 4; j++)
    printf("    > %9.0f deg/day : %8ld  (%.4f%%)\n",
           th[j], over[j], total_speeds ? 100.0 * over[j] / total_speeds : 0.0);
  printf("\n  ascmc_speed vs Richardson extrapolated derivative of ascmc, %ld rows\n"
         "  (%ld slots unwritten, %ld reference out of range, %ld reference unconverged,\n"
         "   %ld rows where 'P' reported ERR and Porphyry was substituted):\n",
         ascmc_rows, ascmc_sent, ascmc_skipped, ascmc_unconv, ascmc_err);
  {
    const char *nm[10] = {"Ascendant", "MC", "ARMC", "Vertex", "equat. Asc",
                          "co-Asc Koch", "co-Asc Munkasey", "polar Asc",
                          "ascmc[8]", "ascmc[9]"};
    printf("    %-16s %12s %12s %12s %14s\n", "", "judged", "worst rel",
           "|ref|<1", "worst abs");
    for (j = 0; j < 10; j++)
      printf("    %-16s %12ld %12.3e %12ld %14.3e\n", nm[j], ascmc_judged[j],
             ascmc_worst[j], ascmc_small[j], ascmc_small_worst[j]);
    printf("    ARMC held against the exact constant %.10f: %ld disagreed\n",
           (double)ARMCS, armcs_bad);
    printf("    ascmc_speed[8] and [9] held at exactly zero: %ld disagreed\n", ascmc89_bad);
    printf("    outside the domain of the formulae: %ld of %ld agree, worst %.3e\n",
           ascmc_bnd_agree, ascmc_bnd_n, ascmc_bnd_worst);
  }

  printf("\nVERDICT\n");
  {
    double aworst = 0, asmall = 0;
    long tsent = 0, tnf = 0, tidx0 = 0, tfb_bad = 0, tfb = 0, tfb_noO = 0;
    for (j = 0; j < 10; j++) {
      if (ascmc_worst[j] > aworst) aworst = ascmc_worst[j];
      if (ascmc_small_worst[j] > asmall) asmall = ascmc_small_worst[j];
    }
    for (f = 0; f < HARNESS_NFLAGSETS; f++) {
      tsent += ps[f].sent_cusp + ps[f].sent_speed + ps[f].sent_ascmc + ps[f].sent_ascmc_speed;
      tnf += ps[f].nf_cusp + ps[f].nf_speed + ps[f].nf_ascmc + ps[f].nf_ascmc_speed;
      tidx0 += ps[f].idx0_nonzero_cusp + ps[f].idx0_nonzero_speed;
      tfb_bad += ps[f].fb_bad; tfb += ps[f].fb_rows;
      tfb_noO += ps[f].fb_no_porphyry;
    }
    printf("  uninitialised slots (1..n)  : %ld   %s\n", tsent, tsent ? "FAIL" : "pass");
    if (tsent) fail++;
    printf("  index 0 not zero            : %ld   %s\n", tidx0, tidx0 ? "FAIL" : "pass");
    if (tidx0) fail++;
    printf("  non-finite values           : %ld   %s\n", tnf, tnf ? "FAIL" : "pass");
    if (tnf) fail++;
    printf("  entry points over %.0e deg  : %ld   %s\n", (double)GATE_ENTRY_DEG,
           entry_gate_bad, entry_gate_bad ? "FAIL" : "pass");
    if (entry_gate_bad) fail++;
    printf("  entry point speeds over %.0e: %ld   %s\n", (double)GATE_ENTRY_SPEED_REL,
           entry_speed_bad, entry_speed_bad ? "FAIL" : "pass");
    if (entry_speed_bad) fail++;
    printf("  SEFLG_RADIANS conversion    : %ld of %ld   %s\n", rad_bad, rad_n,
           rad_bad ? "FAIL" : "pass");
    if (rad_bad) fail++;
    printf("  Porphyry fallback carried   : %ld of %ld failed, over all five flag sets   %s\n",
           tfb_bad, tfb, tfb_bad ? "FAIL" : "pass");
    if (tfb_bad) fail++;
    /* Counting the abandoned rows was only half the fix; nothing read the
     * counter. An 'O' that fails everywhere empties the check above of every
     * row it looks at, and "0 of 0 failed" reads as a pass. Porphyry is the
     * fallback of last resort and answers on every row of this sample, at every
     * stride, so the gate is zero. */
    printf("  fallback rows with no 'O'   : %ld   %s\n", tfb_noO, tfb_noO ? "FAIL" : "pass");
    if (tfb_noO) fail++;
    {
      int fracfail = sid_frac_n && sid_frac_bad > GATE_SID_FRAC_SHARE * sid_frac_n;
      printf("  sidereal identity           : pos %ld, rate(exact) %ld,"
             " rate(1%%) %ld of %ld, pinned %ld   %s\n",
             sid_pos_bad, sid_rate_bad, sid_frac_bad, sid_frac_n, sid_pin_bad,
             (sid_pos_bad || sid_rate_bad || fracfail || sid_pin_bad) ? "FAIL" : "pass");
      if (sid_pos_bad || sid_rate_bad || fracfail || sid_pin_bad) fail++;
    }
    printf("  ascmc_speed worst relative  : %.3e   threshold %.0e   %s\n",
           aworst, (double)GATE_ASCMC_REL, aworst <= GATE_ASCMC_REL ? "pass" : "FAIL");
    if (aworst > GATE_ASCMC_REL) fail++;
    printf("  ascmc_speed worst abs, |ref|<1: %.3e   threshold %.0e   %s\n",
           asmall, (double)GATE_ASCMC_SMALL_ABS,
           asmall <= GATE_ASCMC_SMALL_ABS ? "pass" : "FAIL");
    if (asmall > GATE_ASCMC_SMALL_ABS) fail++;
    {
      double sh = ascmc_bnd_n ? (double)ascmc_bnd_agree / ascmc_bnd_n : 1.0;
      printf("  ascmc outside the domain    : %ld of %ld agree (%.2f%%)   floor %.0f%%  %s\n",
             ascmc_bnd_agree, ascmc_bnd_n, 100.0 * sh,
             GATE_ASCMC_OUTSIDE_SHARE * 100.0,
             sh >= GATE_ASCMC_OUTSIDE_SHARE ? "pass" : "FAIL");
      if (ascmc_bnd_n && sh < GATE_ASCMC_OUTSIDE_SHARE) fail++;
    }
    printf("  ascmc_speed[2] vs ARMCS     : %ld   %s\n", armcs_bad, armcs_bad ? "FAIL" : "pass");
    if (armcs_bad) fail++;
    printf("  ascmc_speed[8],[9] zero     : %ld   %s\n", ascmc89_bad, ascmc89_bad ? "FAIL" : "pass");
    if (ascmc89_bad) fail++;
    printf("  ascmc_speed slots unwritten : %ld   %s\n", ascmc_sent, ascmc_sent ? "FAIL" : "pass");
    if (ascmc_sent) fail++;
    /* The same date-level drop deriv gates, and for the same reason: a date
     * that never gets a frame is never walked by any of the 26 letters or any
     * of the five flag sets, and every count in this mode simply gets smaller.
     * Zero on the shipping stack at every stride. */
    {
      long dropped = fs.skip_armc + fs.skip_eclnut;
      printf("  dates dropped by the frame  : %ld   (%ld no armc, %ld no obliquity)   %s\n",
             dropped, fs.skip_armc, fs.skip_eclnut, dropped ? "FAIL" : "pass");
      if (dropped) fail++;
    }
    /* The two columns that were printed and gated by nothing. Each is held
     * against the block it happened in, never against the mixture. */
    {
      long sr = ascmc_slots - ascmc_slots_d;
      long sd = ascmc_slots_d;
      long kr = ascmc_skipped - ascmc_skip_d, ur = ascmc_unconv - ascmc_unconv_d;
      int bad;
      bad = sr > 0 && kr > GATE_ASCMC_SKIP_SHARE * sr;
      printf("  ascmc reference out of range: %ld of %ld random (%.4f%%, gate %.0f%%)"
             "   %s\n", kr, sr, sr ? 100.0 * kr / sr : 0.0,
             GATE_ASCMC_SKIP_SHARE * 100.0, bad ? "FAIL" : "pass");
      if (bad) fail++;
      bad = sd > 0 && ascmc_skip_d > GATE_ASCMC_SKIP_SHARE_D * sd;
      printf("                              : %ld of %ld degenerate (%.4f%%, gate %.0f%%)"
             "   %s\n", ascmc_skip_d, sd, sd ? 100.0 * ascmc_skip_d / sd : 0.0,
             GATE_ASCMC_SKIP_SHARE_D * 100.0, bad ? "FAIL" : "pass");
      if (bad) fail++;
      bad = sr > 0 && ur > GATE_ASCMC_UNCONV_SHARE * sr;
      printf("  ascmc reference unconverged : %ld of %ld random (%.4f%%, gate %.0f%%)"
             "   %s\n", ur, sr, sr ? 100.0 * ur / sr : 0.0,
             GATE_ASCMC_UNCONV_SHARE * 100.0, bad ? "FAIL" : "pass");
      if (bad) fail++;
      bad = sd > 0 && ascmc_unconv_d > GATE_ASCMC_UNCONV_SHARE_D * sd;
      printf("                              : %ld of %ld degenerate (%.4f%%, gate %.0f%%)"
             "   %s\n", ascmc_unconv_d, sd, sd ? 100.0 * ascmc_unconv_d / sd : 0.0,
             GATE_ASCMC_UNCONV_SHARE_D * 100.0, bad ? "FAIL" : "pass");
      if (bad) fail++;
    }
    printf("\n%s\n", fail ? "claims: FAILED" : "claims: passed");
  }
  free(S);
  return fail ? 1 : 0;
}

/* ------------------------------------------------------------------ main -- */

static void usage(void)
{
  int i;
  fprintf(stderr,
    "usage: house-speed-harness dump FILE [stride] [flagset]\n"
    "       house-speed-harness deriv [stride]\n"
    "       house-speed-harness claims [stride]\n\n"
    "seed 0x%016llX, %d dates (%d random + %d degenerate), %d house systems,\n"
    "JD %.1f .. %.1f, ephemeris fingerprint %016llX\n"
    "flagsets:",
    (unsigned long long)HARNESS_SEED, HARNESS_NDATES, HARNESS_NRANDOM,
    HARNESS_NDEGEN, HARNESS_NSYS, harness_jd_lo(), harness_jd_hi(),
    (unsigned long long)harness_ephe_fingerprint());
  for (i = 0; i < HARNESS_NFLAGSETS; i++) fprintf(stderr, " %s", HARNESS_FLAGSETS[i].name);
  fprintf(stderr, "\nstride subsamples the random dates only; the degenerate block is"
                  " always walked in full\n"
                  "exit 0 pass, 1 a check failed, 2 the harness itself could not run\n");
}

int main(int argc, char **argv)
{
  const char *mode = (argc > 1) ? argv[1] : "";
  int stride = 1, rc;

  swe_set_ephe_path(NULL);

  if (strcmp(mode, "dump") == 0 && argc > 2) {
    const harness_flagset *f = &HARNESS_FLAGSETS[0];
    if (argc > 3 && !harness_parse_stride(argv[3], &stride)) {
      fprintf(stderr, "dump: stride \"%s\" is not a whole number in 1..%d\n",
              argv[3], HARNESS_NRANDOM);
      return 2;
    }
    if (argc > 4) {
      f = harness_flagset_by_name(argv[4]);
      if (f == NULL) { fprintf(stderr, "dump: unknown flagset \"%s\"\n", argv[4]); return 2; }
    }
    rc = mode_dump(argv[2], stride, f);
  } else if (strcmp(mode, "deriv") == 0) {
    if (argc > 2 && !harness_parse_stride(argv[2], &stride)) {
      fprintf(stderr, "deriv: stride \"%s\" is not a whole number in 1..%d\n",
              argv[2], HARNESS_NRANDOM);
      return 2;
    }
    rc = mode_deriv(stride);
  } else if (strcmp(mode, "claims") == 0) {
    if (argc > 2 && !harness_parse_stride(argv[2], &stride)) {
      fprintf(stderr, "claims: stride \"%s\" is not a whole number in 1..%d\n",
              argv[2], HARNESS_NRANDOM);
      return 2;
    }
    rc = mode_claims(stride);
  } else {
    usage();
    return 2;
  }
  swe_close();
  return rc;
}
