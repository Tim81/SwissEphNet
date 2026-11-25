using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swecalc does not clear serr (sweph.c:613-631 at e308765d: "//if (serr != NULL) *serr = '\0';  // is done in
    /// calling function"), so the three swecalc calls of swecalc_1's SEFLG_SPEED3 branch (sweph.c:523-527)
    /// accumulate into one buffer. A warning from the first call (the Moon's JPL-to-Moshier fallback,
    /// sweph.c:753-764) must survive the later ones.
    ///
    /// Reference strings: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), %.17g, DE406 (de406.eph):
    ///   swe_set_ephe_path("&lt;dir of de406.eph&gt;"); swe_set_jpl_file("de406.eph");
    ///   char serr[256] = "";  rc = swe_calc(625360.5 + off, SE_MOON, SEFLG_JPLEPH | SEFLG_SPEED3, x, serr);
    /// DE406 starts at JD 625360.5; tjd - 5e-5 (MOON_SPEED_INTV) lies before it, tjd and tjd + 5e-5 inside.
    /// The tests need de406.eph, which CI does not fetch, so they are skipped without it.
    /// </summary>
    public class SwecalcSerrAccumulationTest
    {
        private static string FindDe406Dir()
        {
            string over = Environment.GetEnvironmentVariable("SWISSEPH_JPL_DIR");
            if (!string.IsNullOrEmpty(over))
                return File.Exists(Path.Combine(over, "de406.eph")) ? over : null;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cand = Path.Combine(home, "swisseph-data");
            return File.Exists(Path.Combine(cand, "de406.eph")) ? cand : null;
        }

        private static readonly string Jpl = FindDe406Dir();

        private static string SkipReason =>
            Jpl == null ? "needs the JPL file de406.eph, which CI does not fetch; set SWISSEPH_JPL_DIR to the directory holding it (or put it in ~/swisseph-data) to run" : null;

        private sealed class JplFactAttribute : FactAttribute
        {
            public JplFactAttribute() { Skip = SkipReason; }
        }

        private sealed class JplTheoryAttribute : TheoryAttribute
        {
            public JplTheoryAttribute() { Skip = SkipReason; }
        }

        private static SwissEph OpenJpl()
        {
            var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
            swe.swe_set_ephe_path(Jpl);
            swe.swe_set_jpl_file("de406.eph");
            return swe;
        }

        // The message of the first of the three calls names tjd - 5e-5.
        [JplTheory]
        [InlineData(1.5e-5, "jd 625360.499965 outside JPL eph. range 625360.50 .. 2816848.50; \nusing Moshier Eph; ")]
        [InlineData(2e-5, "jd 625360.499970 outside JPL eph. range 625360.50 .. 2816848.50; \nusing Moshier Eph; ")]
        [InlineData(3e-5, "jd 625360.499980 outside JPL eph. range 625360.50 .. 2816848.50; \nusing Moshier Eph; ")]
        [InlineData(5e-5, "jd 625360.499985 outside JPL eph. range 625360.50 .. 2816848.50; \nusing Moshier Eph; ")]
        public void Speed3MoonFallbackWarningSurvivesTheLaterCalls(double off, string expected)
        {
            using var swe = OpenJpl();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(625360.5 + off, SwissEph.SE_MOON, SwissEph.SEFLG_JPLEPH | SwissEph.SEFLG_SPEED3, x, ref serr);

            Assert.Equal(129, rc);
            Assert.Equal(expected, serr);
            if (off == 1.5e-5)
            {
                // C: x = 87.613258567650163 5.1195833543199019 0.0025410227735844712
                CReference.Equal(87.613258567650163, x[0], 1e-8, "lon");
                CReference.Equal(5.1195833543199019, x[1], 1e-8, "lat");
                CReference.Equal(0.0025410227735844712, x[2], 1e-10, "dist");
            }
        }

        // Control: the same date with SEFLG_SPEED (one swecalc call) gives rc 257 and serr "" in the C.
        [JplFact]
        public void SingleSpeedCallStaysSilent()
        {
            using var swe = OpenJpl();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(625360.5 + 1.5e-5, SwissEph.SE_MOON, SwissEph.SEFLG_JPLEPH | SwissEph.SEFLG_SPEED, x, ref serr);

            Assert.Equal(257, rc);
            Assert.True(string.IsNullOrEmpty(serr), serr);
        }
    }
}
