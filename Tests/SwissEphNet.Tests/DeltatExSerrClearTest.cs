using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_deltat_ex clears serr before calc_deltat (swephlib.c:2733-2734 at e308765d:
    /// "if (serr != NULL) *serr = '\0';"). With SEFLG_TOPOCTR, swi_get_observer calls it, so a "file not found,
    /// using Moshier" warning built earlier in the same swe_calc is gone from serr in the C.
    ///
    /// Reference values: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), %.17g:
    ///   swe_set_ephe_path("C:/nonexistent_ephe_dir"); swe_set_topo(8.55, 47.37, 400);
    ///   char serr[256] = "";  rc = swe_calc_ut(2451545.0, SE_SUN, flags, x, serr);
    ///   SEFLG_SWIEPH|SEFLG_SPEED|SEFLG_TOPOCTR (33026): rc 33028, serr "",
    ///        x = 280.36857197667911 -0.0020782968730785954 0.9833134780820767
    ///   SEFLG_SWIEPH|SEFLG_SPEED (258), control: rc 260, serr "SwissEph file 'sepl_18.se1' not found in PATH ..."
    ///   strcpy(serr, "stale"); swe_deltat_ex(2451545.0, SEFLG_MOSEPH, serr) -> 0.000738760589, serr ""
    /// </summary>
    public class DeltatExSerrClearTest
    {
        [Fact]
        public void TopocentricCallDropsTheEarlierFileNotFoundWarning()
        {
            using var swe = new SwissEph();
            swe.swe_set_ephe_path("C:/nonexistent_ephe_dir");
            swe.swe_set_topo(8.55, 47.37, 400);
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc_ut(2451545.0, SwissEph.SE_SUN,
                SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED | SwissEph.SEFLG_TOPOCTR, x, ref serr);

            Assert.Equal(33028, rc);
            Assert.True(string.IsNullOrEmpty(serr), serr);
            CReference.Equal(280.36857197667911, x[0], 1e-8, "lon");
            CReference.Equal(-0.0020782968730785954, x[1], 1e-8, "lat");
            CReference.Equal(0.9833134780820767, x[2], 1e-10, "dist");
        }

        [Fact]
        public void WithoutTopocentricTheSameWarningIsKept()
        {
            using var swe = new SwissEph();
            swe.swe_set_ephe_path("C:/nonexistent_ephe_dir");
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc_ut(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, x, ref serr);

            Assert.Equal(260, rc);
            Assert.StartsWith("SwissEph file 'sepl_18.se1' not found in PATH", serr, System.StringComparison.Ordinal);
            Assert.EndsWith("using Moshier eph.; ", serr, System.StringComparison.Ordinal);
        }

        [Fact]
        public void DirectCallClearsAStaleMessage()
        {
            using var swe = new SwissEph();
            string serr = "stale";

            double d = swe.swe_deltat_ex(2451545.0, SwissEph.SEFLG_MOSEPH, ref serr);

            Assert.True(string.IsNullOrEmpty(serr), serr);
            Assert.Equal(0.000738760589, d, 12);
        }
    }
}
