/*
 * The claims in this patch set that swetest cannot show, one probe per mode, so
 * that verify-claims.sh can re-derive them the same way it re-derives the
 * swetest ones. Build it against a pristine tree and against a patched one and
 * diff the output; the expected text for both is recorded in claims.tsv.
 *
 *   cc -O2 -ffp-contract=off -I<tree> -o claim-samples claim-samples.c \
 *      <tree>/libswe.a -lm
 *   ./claim-samples gauquelin | makransky | radians | sunshine | apcx
 *
 * Needs no ephemeris files.
 */
#include "swephexp.h"
#include <stdio.h>
#include <string.h>

#define SENTINEL (-8.8888888888e88)

/* swehouse-gauquelin-porphyry-fallback: inside the polar circle 'G' falls back
 * to Porphyry and writes only cusps 1..12, leaving 13..36 of the caller's
 * arrays holding whatever was in them. swephprg tells the caller to declare
 * cusp[37]. */
static void gauquelin(void)
{
  double cusp[37], ascmc[10], cusp_speed[37], ascmc_speed[10];
  char serr[256];
  int i, rc, npos = 0, nspd = 0;
  for (i = 0; i < 37; i++) { cusp[i] = SENTINEL; cusp_speed[i] = SENTINEL; }
  for (i = 0; i < 10; i++) { ascmc[i] = SENTINEL; ascmc_speed[i] = SENTINEL; }
  serr[0] = '\0';
  rc = swe_houses_ex2(2451545.0, 0, 70.0, 13.4, 'G',
                      cusp, ascmc, cusp_speed, ascmc_speed, serr);
  for (i = 1; i <= 36; i++) {
    if (cusp[i] == SENTINEL) npos++;
    if (cusp_speed[i] == SENTINEL) nspd++;
  }
  printf("gauquelin rc=%d\n", rc);
  printf("  positions still holding the sentinel : %d of 36\n", npos);
  printf("  speeds    still holding the sentinel : %d of 36\n", nspd);
  printf("  cusp[13]=%s cusp[36]=%s\n",
         cusp[13] == SENTINEL ? "sentinel" : "written",
         cusp[36] == SENTINEL ? "sentinel" : "written");
  printf("  serr=%s\n", serr[0] ? serr : "(empty)");
}

/* swehouse-makransky-w180: the Makransky Sunshine variant returns a cusp 180
 * degrees wrong when the house circle lands exactly on w = 180. hsys 'I', the
 * Treindl variant, is right at the same input and is untouched by the patch,
 * so the two disagreeing by 180 is the defect. */
static void makransky(void)
{
  double cusp[13], ascmc[10], cs[13], as[10];
  char serr[256];
  ascmc[9] = 0.0;                      /* Sun declination exactly on the equator */
  swe_houses_armc_ex2(30.0, 0.0, 23.4392911, 'i', cusp, ascmc, cs, as, serr);
  printf("makransky i cusp[3] = %.7f\n", cusp[3]);
  ascmc[9] = 0.0;
  swe_houses_armc_ex2(30.0, 0.0, 23.4392911, 'I', cusp, ascmc, cs, as, serr);
  printf("treindl   I cusp[3] = %.7f\n", cusp[3]);
  /* the southern 90/270 side error, and the continuity either side of it */
  ascmc[9] = 0.0;
  swe_houses_armc_ex2(60.0, -45.0, 23.4392911, 'i', cusp, ascmc, cs, as, serr);
  printf("makransky i cusp[11] at armc 60, lat -45 = %.12f\n", cusp[11]);
}

/* swehouse-radians-speed-units: under SEFLG_RADIANS the positions are converted
 * and both speed arrays are left in degrees, so the two come back a factor of
 * 57.3 apart. swetest has no radians flag, so this is the test. */
static void radians(void)
{
  double cusp[13], ascmc[10], cs[13], as[10];
  char serr[256];
  swe_houses_ex2(2451545.0, SEFLG_RADIANS, 52.5, 13.4, 'P',
                 cusp, ascmc, cs, as, serr);
  printf("radians cusp[1]       = %.9f rad\n", cusp[1]);
  printf("radians cusp_speed[1] = %.9f per day\n", cs[1]);
  printf("radians ascmc_speed[0]= %.9f per day\n", as[0]);
}

/* swehouse-sunshine-error-path: the declination check crashes through the
 * serr-less entry points and returns with nothing written. Before the patch
 * this probe dies on signal 11 at the first call; after it, both calls
 * return ERR with zeroed outputs. */
static void sunshine(void)
{
  double cusp[13], ascmc[10];
  int i, rc, nz;
  for (i = 0; i < 13; i++) cusp[i] = SENTINEL;
  for (i = 0; i < 10; i++) ascmc[i] = 0;
  ascmc[9] = 90.0;                    /* out of range, caller-supplied */
  rc = swe_houses_armc(100.0, 45.0, 23.4392911, 'I', cusp, ascmc);
  nz = 0;
  for (i = 1; i <= 12; i++) if (cusp[i] == 0.0) nz++;
  printf("sunshine armc rc=%d zeroed cusps %d of 12 ascmc9=%.1f\n",
         rc, nz, ascmc[9]);
  for (i = 0; i < 13; i++) cusp[i] = SENTINEL;
  for (i = 0; i < 10; i++) ascmc[i] = 0;
  rc = swe_houses(630070.0, 52.5, 13.4, 'I', cusp, ascmc);
  printf("sunshine ancient rc=%d cusp1=%.1f\n", rc, cusp[1]);
}

/* swehouse-apc-equator-fix: at a latitude inside the equator band, APC 'Y'
 * must reproduce the Meridian system 'X', which swehouse.c computes by an
 * independent route. Before the patch the two disagree by 32.4 degrees at
 * the worst cusp; after it, all twelve agree to about 2e-11 degrees at
 * this latitude (8.5e-14 at latitude zero, growing linearly), below
 * anything this probe prints. */
static void apcx(void)
{
  double cy[13], cx[13], a[10], dummy;
  int i, ry, rx;
  double worst = 0;
  (void)dummy;
  ry = swe_houses_ex2(2451545.0, 0, 0.00000000005, 13.4, 'Y', cy, a,
                      NULL, NULL, NULL);
  rx = swe_houses_ex2(2451545.0, 0, 0.00000000005, 13.4, 'X', cx, a,
                      NULL, NULL, NULL);
  for (i = 1; i <= 12; i++) {
    double d = cy[i] - cx[i];
    if (d < 0) d = -d;
    if (d > 180) d = 360 - d;
    if (d > worst) worst = d;
  }
  printf("apcx rc %d %d worst |Y-X| over 12 cusps = %.9f deg", ry, rx, worst);
}

int main(int argc, char **argv)
{
  const char *what = (argc > 1) ? argv[1] : "";
  if (strcmp(what, "gauquelin") == 0) gauquelin();
  else if (strcmp(what, "makransky") == 0) makransky();
  else if (strcmp(what, "radians") == 0) radians();
  else if (strcmp(what, "sunshine") == 0) sunshine();
  else if (strcmp(what, "apcx") == 0) apcx();
  else {
    fprintf(stderr, "usage: claim-samples gauquelin|makransky|radians|sunshine|apcx\n");
    return 2;
  }
  return 0;
}
