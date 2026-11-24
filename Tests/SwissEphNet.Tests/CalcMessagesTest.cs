using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Warnings and range errors returned in serr by swe_calc at v2.10.04: the JPL Horizons warning of
    /// plaus_iflag, and the messages that now carry both a Julian day and its calendar date (jd_and_date,
    /// sweph.c:2237: "JD 1967600.5 = 31.12.0674j", "j" for Julian-calendar dates, " h:mm:ss TT" when the
    /// time is not 0h).
    ///
    /// Reference strings: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), %.17g:
    ///   swe_set_ephe_path("C:\nonexistent");   // or the submodule's ephe directory, same serr
    ///   char serr[256] = "";  rc = swe_calc(tjd, ipl, iflag, x, serr);
    /// </summary>
    public class CalcMessagesTest
    {
        private const string JplHorWarning =
            "JPL Horizons mode (SEFLG_JPLHOR, SEFLG_JPLHOR_APPROX) only with the JPL file; ignored";

        // swe_calc(2451545.0, SE_SUN, SEFLG_MOSEPH | SEFLG_JPLHOR, x, serr)
        //   -> rc 4 (the JPLHOR bit is stripped from the returned flags; the warning does not make it an error),
        //      x = 280.36816655827806 0.00023233181223791661 0.98332765025481661 0 0 0
        [Fact]
        public void JplHorWithMoshier_WarnsAndIgnoresTheFlag()
        {
            using var swe = new SwissEph();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(2451545.0, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_JPLHOR, x, ref serr);

            Assert.Equal(SwissEph.SEFLG_MOSEPH, rc);
            Assert.Equal(JplHorWarning, serr);
            CReference.Equal(280.36816655827806, x[0], 1e-8, "lon");
            CReference.Equal(0.00023233181223791661, x[1], 1e-8, "lat");
            CReference.Equal(0.98332765025481661, x[2], 1e-10, "dist");
        }

        // swe_set_ephe_path(<ephe>);
        // swe_calc(2451545.0, SE_SUN, SEFLG_SWIEPH | SEFLG_JPLHOR, x, serr)  -- twice
        //   1st call: rc 2, serr "" (opening sepl_18.se1 clears serr, sweph.c:2393 "during the search, error
        //             messages may have been built, delete them": the warning set by plaus_iflag is lost)
        //   2nd call: rc 2, serr = the warning,
        //             x = 280.36816540312418 0.00022740445204394392 0.98332763187801853 0 0 0
        [EpheFact]
        public void JplHorWithSwissEphemeris_WarnsOnceTheFilesAreOpen()
        {
            using var swe = RepoEphe.Open();
            var x = new double[6];
            string serr = null;
            int flags = SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_JPLHOR;

            int rc1 = swe.swe_calc(2451545.0, SwissEph.SE_SUN, flags, x, ref serr);
            Assert.Equal(SwissEph.SEFLG_SWIEPH, rc1);
            Assert.True(string.IsNullOrEmpty(serr), "first call: " + serr);

            string serr2 = null;
            int rc2 = swe.swe_calc(2451545.0, SwissEph.SE_SUN, flags, x, ref serr2);
            Assert.Equal(SwissEph.SEFLG_SWIEPH, rc2);
            Assert.Equal(JplHorWarning, serr2);
            CReference.Equal(280.36816540312418, x[0], 1e-8, "lon");
            CReference.Equal(0.00022740445204394392, x[1], 1e-8, "lat");
            CReference.Equal(0.98332763187801853, x[2], 1e-10, "dist");
        }

        // swe_calc(tjd, SE_CHIRON, SEFLG_SWIEPH, x, serr) -> rc -1, x all 0, serr as below; checked before any
        // file is opened, so the message is the same with and without ephemeris files.
        [Theory]
        [InlineData(1967600.5,
            "Chiron not available at JD 1967600.5 = 31.12.0674j; restricted to JD 1967601.5 = 01.01.0675j - JD 3419437.5 = 01.01.4650")]
        [InlineData(3419438.0,
            "Chiron not available at JD 3419438.0 = 01.01.4650 12:00:00 TT; restricted to JD 1967601.5 = 01.01.0675j - JD 3419437.5 = 01.01.4650")]
        public void ChironOutsideItsRange_NamesJulianDayAndDate(double tjd, string expected)
        {
            using var swe = new SwissEph();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(tjd, SwissEph.SE_CHIRON, SwissEph.SEFLG_SWIEPH, x, ref serr);

            Assert.Equal(SwissEph.ERR, rc);
            Assert.Equal(expected, serr);
            Assert.All(x, v => Assert.Equal(0.0, v));
        }

        // swe_calc(tjd, ipl, SEFLG_MOSEPH, x, serr) with ipl = SE_INTP_APOG (21) or SE_INTP_PERG (22)
        //   -> rc -1, x all 0, serr as below (MOSHLUEPH_START/END = 625000.5 / 2818000.5).
        [Theory]
        [InlineData(SwissEph.SE_INTP_APOG, -225001.5,
            "interpolated apsides not available at JD -225001.5 = 25.12.-5329j; restricted to JD 625000.5 = 28.02.-3001j - JD 2818000.5 = 29.04.3003")]
        [InlineData(SwissEph.SE_INTP_PERG, -225001.5,
            "interpolated apsides not available at JD -225001.5 = 25.12.-5329j; restricted to JD 625000.5 = 28.02.-3001j - JD 2818000.5 = 29.04.3003")]
        [InlineData(SwissEph.SE_INTP_APOG, 3600001.5,
            "interpolated apsides not available at JD 3600001.5 = 15.05.5144; restricted to JD 625000.5 = 28.02.-3001j - JD 2818000.5 = 29.04.3003")]
        [InlineData(SwissEph.SE_INTP_APOG, 625000.0,
            "interpolated apsides not available at JD 625000.0 = 27.02.-3001j 12:00:00 TT; restricted to JD 625000.5 = 28.02.-3001j - JD 2818000.5 = 29.04.3003")]
        [InlineData(SwissEph.SE_INTP_PERG, 2818001.0,
            "interpolated apsides not available at JD 2818001.0 = 29.04.3003 12:00:00 TT; restricted to JD 625000.5 = 28.02.-3001j - JD 2818000.5 = 29.04.3003")]
        public void InterpolatedApsidesOutsideTheirRange_NameJulianDayAndDate(int ipl, double tjd, string expected)
        {
            using var swe = new SwissEph();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(tjd, ipl, SwissEph.SEFLG_MOSEPH, x, ref serr);

            Assert.Equal(SwissEph.ERR, rc);
            Assert.Equal(expected, serr);
            Assert.All(x, v => Assert.Equal(0.0, v));
        }
    }
}
