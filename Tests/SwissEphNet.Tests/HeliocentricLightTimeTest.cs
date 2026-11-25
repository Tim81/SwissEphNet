using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Heliocentric light-time (upstream 8178d0ac, v2.10.04). With SEFLG_HELCTR the second light-time
    /// pass of app_pos_etc_plan used the barycentric distance of the planet (xx0[] is barycentric), so
    /// dt was too long: Mars 2016 0.8 s, 21 mas; Mercury up to 0.84 arcsec. app_pos_etc_plan_osc had the
    /// same error for the fictitious planets (it subtracted the observer only when not heliocentric).
    ///
    /// Reference values: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib
    /// (built from e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), %.17g:
    ///   swe_set_ephe_path("&lt;external/swisseph/ephe&gt;");
    ///   rc = swe_calc(tjd, ipl, flags, x, serr);   // serr empty
    /// with tjd 2457570.5 (30 Jun 2016) and 2457754.25 (1 Jan 2017 6h), both inside sepl_18, and
    /// flags 266 = SEFLG_SWIEPH | SEFLG_HELCTR | SEFLG_SPEED (rc 1802: the library adds NOABERR |
    /// NOGDEFL) and 10 = SEFLG_SWIEPH | SEFLG_HELCTR (rc 1546; speeds are zero).
    /// Each row: ipl, flags, tjd, rc, lon, lat, dist, dlon, dlat, ddist. The planets are Mercury (2),
    /// Mars (4) and the fictitious planet Cupido (40, from seorbel.txt).
    /// </summary>
    public class HeliocentricLightTimeTest
    {
        [EpheTheory]
        [InlineData(4, 266, 2457570.5, 1802, 263.18126434658012, -1.0211108837764453, 1.4706630894902437, 0.55997113045922831, -0.015064409748346086, -0.0012524431205966258)]
        [InlineData(4, 266, 2457754.25, 1802, 16.016483892329937, -1.0257309444450231, 1.4088706061309952, 0.61018250280203479, 0.016396305984410102, 0.00083405057417505215)]
        [InlineData(4, 10, 2457570.5, 1546, 263.18126434658012, -1.0211108837764364, 1.4706630894902439, 0, 0, 0)]
        [InlineData(4, 10, 2457754.25, 1546, 16.016483892329941, -1.0257309444450222, 1.4088706061309952, 0, 0, 0)]
        [InlineData(2, 266, 2457570.5, 1802, 66.977863646256878, 2.2273301641691412, 0.30839983117727876, 6.2720785579633827, 0.72995454345743327, -0.0010691867938375141)]
        [InlineData(2, 266, 2457754.25, 1802, 115.55685914960793, 6.454585683896588, 0.31903264833197437, 5.9270164464137594, 0.28067877781607531, 0.0035762714858432031)]
        [InlineData(2, 10, 2457570.5, 1546, 66.977863646256878, 2.2273301641691403, 0.30839983117727887, 0, 0, 0)]
        [InlineData(2, 10, 2457754.25, 1546, 115.55685914960796, 6.4545856838965854, 0.31903264833197437, 0, 0, 0)]
        [InlineData(40, 266, 2457570.5, 1802, 266.07843952686983, 0.75590073735769869, 40.847085553345245, 0.0038365064918948846, -4.1106482406950482e-05, -7.4058925026561403e-06)]
        [InlineData(40, 266, 2457754.25, 1802, 266.77981629340405, 0.7465524881670087, 40.845735647594218, 0.0038357443188985129, -6.300589003748593e-05, -7.2852803138285054e-06)]
        [InlineData(40, 10, 2457570.5, 1546, 266.07843952725983, 0.75590073734583407, 40.847085558275573, 0, 0, 0)]
        [InlineData(40, 10, 2457754.25, 1546, 266.77981629374636, 0.74655248815738695, 40.845735652503834, 0, 0, 0)]
        public void HelioCentricPositions_MatchTheC(int ipl, int flags, double tjd, int expectedRc,
            double lon, double lat, double dist, double dlon, double dlat, double ddist)
        {
            using var swe = RepoEphe.Open();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(tjd, ipl, flags, x, ref serr);

            Assert.True(rc == expectedRc, $"rc {rc}, serr '{serr}'");
            CReference.Equal(lon, x[0], 1e-8, "lon");
            CReference.Equal(lat, x[1], 1e-8, "lat");
            CReference.Equal(dist, x[2], 1e-10, "dist");
            CReference.Equal(dlon, x[3], 1e-8, "dlon");
            CReference.Equal(dlat, x[4], 1e-8, "dlat");
            CReference.Equal(ddist, x[5], 1e-10, "ddist");
        }
    }
}
