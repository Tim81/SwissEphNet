using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_get_astro_models output at v2.10.04, for the version strings "" and "SE2.10.04" (both select the
    /// current defaults: Delta T model 6, every other slot at its default) and "SE2.06" (Delta T model 5,
    /// "5,9,9,4,3,0,0,4").
    ///
    /// Reference strings: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), a fresh process per call:
    ///   char sdet[4000];
    ///   swe_get_astro_models(samod, sdet, iflag);   // iflag 4 = SEFLG_MOSEPH, or 2 = SEFLG_SWIEPH
    ///   fputs(sdet, stdout);  printf("tid=%.17g\n", swe_get_tid_acc());
    /// stdout was translated to LF. swe_get_astro_models itself calls swe_set_astro_models(samod, iflag).
    /// The tidal acceleration is -25.936 (SE_TIDAL_DE441) in all of them; "JPL eph." is 403 under
    /// SEFLG_MOSEPH and 441 (SE_DE_NUMBER) under SEFLG_SWIEPH with no planet file open.
    /// </summary>
    public class AstroModelsStringTest
    {
        private const string DefaultMoseph =
            "JPL eph. 403; tidal acc. Moon used by SE: -25.9360\n" +
            "Moshier semi-analytical approximation\n" +
            "Delta T (long-term): Morrison/Stephenson/Hohenkerk/Zawilski 2020 (before 1962)\n" +
            "Precession: Vondr\u00e1k 2011\n" +
            "Nutation: IAU 2000B (Mathews)\n" +
            "Frame bias: IAU 2006\n" +
            "Sid. time: IERS Convention 2010 + long-term extension by Astrodienst\n" +
            "swetest parameters:      D P P N B J J S\n" +
            "                    -amod0,0,0,0,0,0,0,0, -tidacc-25.936000\n" +
            "For list of all available astronomical models, add a '+' to the version string\n" +
            "(swetest parameter -amod+ or -amod0,0,0,0,0,0,0,0,+)\n";

        private const string Se21004Moseph =
            "JPL eph. 403; tidal acc. Moon used by SE: -25.9360\n" +
            "Moshier semi-analytical approximation\n" +
            "Delta T (long-term): Morrison/Stephenson/Hohenkerk/Zawilski 2020 (before 1962)\n" +
            "Precession: Vondr\u00e1k 2011\n" +
            "Nutation: IAU 2000B (Mathews)\n" +
            "Frame bias: IAU 2006\n" +
            "Sid. time: IERS Convention 2010 + long-term extension by Astrodienst\n" +
            "swetest parameters:      D P P N B J J S\n" +
            "                    -amod0,0,0,0,0,0,0,0, -tidacc-25.936000\n" +
            "For list of all available astronomical models, add a '+' to the version string\n" +
            "(swetest parameter -amodSE2.10.04+ or -amod0,0,0,0,0,0,0,0,+)\n";

        private const string Se206Moseph =
            "JPL eph. 403; tidal acc. Moon used by SE: -25.9360\n" +
            "Moshier semi-analytical approximation\n" +
            "Delta T (long-term): Stephenson/Morrison/Hohenkerk 2016 (before 1955)\n" +
            "Precession: Vondr\u00e1k 2011\n" +
            "Nutation: IAU 2000B (Mathews)\n" +
            "Frame bias: IAU 2006\n" +
            "Sid. time: IERS Convention 2010 + long-term extension by Astrodienst\n" +
            "swetest parameters:      D P P N B J J S\n" +
            "                    -amod5,0,0,0,0,0,0,0, -tidacc-25.936000\n" +
            "For list of all available astronomical models, add a '+' to the version string\n" +
            "(swetest parameter -amodSE2.06+ or -amod5,0,0,0,0,0,0,0,+)\n";

        private const string DefaultSwieph =
            "JPL eph. 441; tidal acc. Moon used by SE: -25.9360\n" +
            "Swiss Ephemeris compressed files sepl*/semo*\n" +
            "Delta T (long-term): Morrison/Stephenson/Hohenkerk/Zawilski 2020 (before 1962)\n" +
            "Precession: Vondr\u00e1k 2011\n" +
            "Nutation: IAU 2000B (Mathews)\n" +
            "Frame bias: IAU 2006\n" +
            "Sid. time: IERS Convention 2010 + long-term extension by Astrodienst\n" +
            "swetest parameters:      D P P N B J J S\n" +
            "                    -amod0,0,0,0,0,0,0,0, -tidacc-25.936000\n" +
            "For list of all available astronomical models, add a '+' to the version string\n" +
            "(swetest parameter -amod+ or -amod0,0,0,0,0,0,0,0,+)\n";

        [Theory]
        [InlineData("", SwissEph.SEFLG_MOSEPH, 0)]
        [InlineData("SE2.10.04", SwissEph.SEFLG_MOSEPH, 1)]
        [InlineData("SE2.06", SwissEph.SEFLG_MOSEPH, 2)]
        [InlineData("", SwissEph.SEFLG_SWIEPH, 3)]
        public void GetAstroModels_MatchesTheC(string samod, int iflag, int which)
        {
            string expected = which switch
            {
                0 => DefaultMoseph,
                1 => Se21004Moseph,
                2 => Se206Moseph,
                _ => DefaultSwieph
            };
            using var swe = new SwissEph();

            swe.swe_get_astro_models(samod, out string sdet, iflag);

            Assert.Equal(expected, sdet);
            Assert.Equal(-25.936, swe.swe_get_tid_acc());
        }

        // sweph.c:1257 clears swed in place, so a pointer into swed.astro_models taken before the first
        // call that initialises swed (swe_get_astro_models reads it before swe_set_astro_models runs) stays
        // valid. The port used to replace swed there, and a fresh instance's first call printed the default
        // models ("-amod0,...", model 6's Delta T text) instead of the C's "-amod5,...".
        [Fact]
        public void Se206_OnAFreshInstance_AndAgainOnTheSameInstance_MatchTheC()
        {
            using var swe = new SwissEph();

            swe.swe_get_astro_models("SE2.06", out string first, SwissEph.SEFLG_MOSEPH);
            swe.swe_get_astro_models("SE2.06", out string second, SwissEph.SEFLG_MOSEPH);

            Assert.Equal(Se206Moseph, first);
            Assert.Equal(Se206Moseph, second);
        }

        [Fact]
        public void DefaultsLeaveTheDeltaTSlotAtZero_AndSe206SetsIt()
        {
            // The "-amod" line of the swetest parameters: slot D is 0 for the default model 6, 5 for SE2.06.
            using var swe = new SwissEph();
            swe.swe_get_astro_models("", out string def, SwissEph.SEFLG_MOSEPH);
            swe.swe_get_astro_models("SE2.06", out string se206, SwissEph.SEFLG_MOSEPH);
            Assert.Contains("-amod0,0,0,0,0,0,0,0, ", def, System.StringComparison.Ordinal);
            Assert.Contains("-amod5,0,0,0,0,0,0,0, ", se206, System.StringComparison.Ordinal);
        }
    }
}
