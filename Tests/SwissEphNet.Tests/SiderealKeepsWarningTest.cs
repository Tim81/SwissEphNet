using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// plaus_iflag (sweph.c:6078) takes <c>char *serr</c> and writes it only in its JPLHOR/eop
    /// branches. The port's version was <c>out string serr</c> and assigned null on entry, so
    /// swi_get_ayanamsa_ex (sweph.c:3015, reached under SEFLG_SIDEREAL) erased any warning
    /// already collected in the same swe_calc call, e.g. "SwissEph file 'sepl_18.se1' not found
    /// ... using Moshier eph.". Without SEFLG_SIDEREAL the warning survived.
    /// </summary>
    public class SiderealKeepsWarningTest
    {
        private static string Warning(int extraFlags)
        {
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path => null);
            double[] xx = new double[6];
            string serr = null;
            int rc = swe.swe_calc(2451545.0, SwissEph.SE_MARS, SwissEph.SEFLG_SWIEPH | extraFlags, xx, ref serr);
            Assert.True(rc >= 0, serr);
            return serr;
        }

        [Fact]
        public void Test_MissingPlanetFile_TropicalKeepsMoshierWarning()
        {
            Assert.Contains("using Moshier eph.", Warning(0), StringComparison.Ordinal);
        }

        [Fact]
        public void Test_MissingPlanetFile_SiderealKeepsMoshierWarning()
        {
            Assert.Contains("using Moshier eph.", Warning(SwissEph.SEFLG_SIDEREAL), StringComparison.Ordinal);
        }
    }
}
