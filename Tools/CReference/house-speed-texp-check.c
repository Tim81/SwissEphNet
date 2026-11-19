/*
 * house-speed-texp-check.c -- evidence for the setest/t.exp 6.8 and 6.9 rows that the
 * swehouse.c sync to upstream 6c989727 moves outside t.exp's 1e-3 tolerance.
 *
 * t.exp (Astrodienst's setest reference, dated 14.12.2023) records swe_houses_ex2 (6.8) and
 * swe_houses_armc_ex2 (6.9) cusp and ascmc speeds from the finite-difference code that
 * 6c989727 replaced with analytic derivatives. This driver replays every 6.8/6.9 iteration
 * through the linked C library exactly as Tests/SwissEphNet.Conformance.Tests'
 * Dispatch/Suite06Houses.cs does, and for every compared field that differs from t.exp by
 * more than the testcase's precision it also takes the numerical derivative of the library's
 * own positions: a Richardson-extrapolated central difference in time (6.8, steps 1e-4 and
 * 5e-5 day, same iflag) or in armc (6.9, steps 0.04 and 0.02 degree, times ARMCS, with eps
 * held fixed as swe_houses_armc_ex2 does). Each failing field is classified
 *
 *   CORRECTION   |C - numerical| <= max(0.05, 1e-3 * |t.exp - numerical|) degrees per day
 *                (radians per day under SEFLG_RADIANS): the library's speed is the true
 *                derivative of its positions and t.exp's is not
 *   STEP         |numerical| > 1e4 degrees per day: the difference interval straddles a
 *                cusp jump (a whole sign cusp at a sign ingress), where no derivative exists
 *   UNEXPLAINED  anything else, including any position field that moved
 *
 * and an iteration takes the worst of its fields (UNEXPLAINED > STEP > CORRECTION). The output
 * is one tab-separated row per failing iteration on stdout, a summary on stderr.
 *
 * Build against the MSVC reference library Tools/CReference/build-c.ps1 produces, e.g. from a
 * Developer Command Prompt at the repository root:
 *   cl /O2 /fp:precise /MD /I external\swisseph Tools\CReference\house-speed-texp-check.c
 *      external\.c-reference\build-2.10.03\libswe-2.10.03.lib
 *   house-speed-texp-check external\swisseph\setest\t.exp > Tests\conformance\house-speed-drift-6c989727.tsv
 */
#define _CRT_SECURE_NO_WARNINGS   /* standard C library calls; MSVC's C4996 deprecations */
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "swephexp.h"

#define SOLAR_YEAR 365.24219893                       /* swehouse.c:69 */
#define ARMCS ((SOLAR_YEAR + 1) / SOLAR_YEAR * 360)   /* swehouse.c:70 */
#define NFIELD 38   /* cusps[0..12], cusp_speed[0..12], ascmc[0..5], ascmc_speed[0..5] */

struct iter {
  int tc, it;
  double jd, ut, geolat, geolon, armc, precision;
  int ihsy, iflag;
  double exp[NFIELD];
  int has[NFIELD];
};

static void field_name(int k, char *s)
{
  if (k < 13) sprintf(s, "cusps[%d]", k);
  else if (k < 26) sprintf(s, "cusp_speed[%d]", k - 13);
  else if (k < 32) sprintf(s, "ascmc[%d]", k - 26);
  else sprintf(s, "ascmc_speed[%d]", k - 32);
}

static int field_index(const char *key)
{
  int i;
  if (sscanf(key, "cusps[%d]", &i) == 1 && i >= 0 && i < 13) return i;
  if (sscanf(key, "cusp_speed[%d]", &i) == 1 && i >= 0 && i < 13) return 13 + i;
  if (sscanf(key, "ascmc[%d]", &i) == 1 && i >= 0 && i < 6) return 26 + i;
  if (sscanf(key, "ascmc_speed[%d]", &i) == 1 && i >= 0 && i < 6) return 32 + i;
  return -1;
}

/* the fields Suite06Houses.cs compares, in its order */
static void run(const struct iter *r, double x, double *out)
{
  double c[37] = {0}, a[10] = {0}, cs[37] = {0}, as[10] = {0}, xx[6];
  char serr[AS_MAXCH];
  int i;
  if (r->tc == 8) {
    swe_houses_ex2(x, r->iflag, r->geolat, r->geolon, r->ihsy, c, a, cs, as, serr);
  } else {
    swe_calc(r->jd + r->ut / 24.0, SE_ECL_NUT, 0, xx, serr);
    swe_houses_armc_ex2(x, r->geolat, xx[0], r->ihsy, c, a, cs, as, serr);
  }
  for (i = 0; i < 13; i++) { out[i] = c[i]; out[13 + i] = cs[i]; }
  for (i = 0; i < 6; i++) { out[26 + i] = a[i]; out[32 + i] = as[i]; }
}

static double ddif(double p, double m, int rad)
{
  return rad ? swe_radnorm(p - m + M_PI) - M_PI : swe_difdeg2n(p, m);
}

static int counts[3], nfail_iter, nfail_field, niter;

static void check(const struct iter *r)
{
  static const char *cls[] = {"CORRECTION", "STEP", "UNEXPLAINED"};
  double v[NFIELD], p1[NFIELD], m1[NFIELD], p2[NFIELD], m2[NFIELD];
  double x0, h1, h2, scale, unit;
  int rad = (r->tc == 8 && (r->iflag & SEFLG_RADIANS));
  int k, worst = -1;
  char line[16384] = "", buf[512], name[32];
  niter++;
  x0 = (r->tc == 8) ? r->jd + r->ut / 24.0 : r->armc;
  run(r, x0, v);
  if (r->tc == 8) { h1 = 1e-4; h2 = 5e-5; scale = 1; }
  else { h1 = 0.04; h2 = 0.02; scale = ARMCS; }
  unit = rad ? DEGTORAD : 1;
  run(r, x0 + h1, p1); run(r, x0 - h1, m1);
  run(r, x0 + h2, p2); run(r, x0 - h2, m2);
  for (k = 0; k < NFIELD; k++) {
    int c, pos;
    double num, n1, n2, ec, et;
    if (!r->has[k] || fabs(v[k] - r->exp[k]) <= r->precision)
      continue;
    pos = (k < 13) || (k >= 26 && k < 32);
    if (pos) {
      c = 2;
      num = 0;
    } else {
      int j = (k < 26) ? k - 13 : k - 32;
      int pk = (k < 26) ? j : 26 + j;   /* the position this speed belongs to */
      n1 = ddif(p1[pk], m1[pk], rad) / (2 * h1);
      n2 = ddif(p2[pk], m2[pk], rad) / (2 * h2);
      num = (4 * n2 - n1) / 3 * scale;
      ec = fabs(v[k] - num);
      et = fabs(r->exp[k] - num);
      if (fabs(num) > 1e4 * unit) c = 1;
      else if (ec <= 0.05 * unit || ec <= 1e-3 * et) c = 0;
      else c = 2;
    }
    if (c > worst) worst = c;
    nfail_field++;
    field_name(k, name);
    sprintf(buf, "%s%s: texp=%.17g c=%.17g num=%.17g", *line ? "; " : "", name, r->exp[k], v[k], num);
    strcat(line, buf);
  }
  if (worst < 0)
    return;
  nfail_iter++;
  counts[worst]++;
  printf("6\t%d\t%d\t%s\t%s\n", r->tc, r->it, cls[worst], line);
}

int main(int argc, char **argv)
{
  FILE *f;
  char s[1024], key[256], val[256];
  int depth = 0, suite = 0, tc = 0, in_it = 0;
  double precision = 1e-3;
  struct iter r;
  if (argc < 2 || (f = fopen(argv[1], "r")) == NULL) {
    fprintf(stderr, "usage: house-speed-texp-check <setest/t.exp>\n");
    return 2;
  }
  swe_set_ephe_path(NULL);
  printf("suite\ttestcase\titeration\tclassification\tfields (texp=t.exp expected, c=C library output, num=numerical derivative of the library's own positions)\n");
  while (fgets(s, sizeof s, f) != NULL) {
    char *p = s;
    while (*p == ' ' || *p == '\t') p++;
    p[strcspn(p, "\r\n")] = 0;
    if (!strcmp(p, "TESTSUITE") || !strcmp(p, "TESTCASE") || !strcmp(p, "ITERATION")) {
      if (in_it) check(&r);
      in_it = 0;
      depth = (p[4] == 'S') ? 1 : (p[4] == 'C') ? 2 : 3;
      if (depth == 3 && suite == 6 && (tc == 8 || tc == 9)) {
        memset(&r, 0, sizeof r);
        r.tc = tc;
        r.precision = precision;
        in_it = 1;
      }
      continue;
    }
    if (sscanf(p, "%255[^:]: %255s", key, val) != 2)
      continue;
    if (!strcmp(key, "section-id")) {
      if (depth == 1) suite = atoi(val);
      else if (depth == 2) { tc = atoi(val); precision = 1e-3; }
      else if (in_it) r.it = atoi(val);
      continue;
    }
    if (depth == 2 && !strcmp(key, "precision")) { precision = atof(val); continue; }
    if (!in_it) continue;
    if (!strcmp(key, "jd")) r.jd = atof(val);
    else if (!strcmp(key, "ut")) r.ut = atof(val);
    else if (!strcmp(key, "ihsy")) r.ihsy = atoi(val);
    else if (!strcmp(key, "geolat")) r.geolat = atof(val);
    else if (!strcmp(key, "geolon")) r.geolon = atof(val);
    else if (!strcmp(key, "iflag")) r.iflag = atoi(val);
    else if (!strcmp(key, "armc")) r.armc = atof(val);
    else {
      int k = field_index(key);
      if (k >= 0) { r.exp[k] = atof(val); r.has[k] = 1; }
    }
  }
  if (in_it) check(&r);
  fclose(f);
  fprintf(stderr, "iterations: %d, failing: %d (%d fields); CORRECTION %d, STEP %d, UNEXPLAINED %d\n",
          niter, nfail_iter, nfail_field, counts[0], counts[1], counts[2]);
  return counts[2] ? 1 : 0;
}
