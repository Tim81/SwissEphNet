using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_heliacal_ut with an SE_HELFLAG_AVKIND_* flag goes through heliacal_ut_arc_vis, whose
    /// xaz buffer swehel.c declared as double[2] until upstream 94621aba made it double[3]:
    /// swe_azalt writes three values into it. In C that overran the stack silently; the port's
    /// faithful new double[2] threw IndexOutOfRangeException instead, for every AVKIND flag.
    /// </summary>
    public class HeliacalArcVisTest
    {
        // Reference values: Astrodienst's C at 94621aba (external/swisseph), libswe built by
        // Tools/CReference/build-c.ps1 (MSVC, /O2 /fp:precise /MD), called with exactly these
        // inputs and printed with %.17g. On Windows x64, the platform the reference was built on,
        // the port matches all four bit-for-bit (net8.0 and net10.0), so it is asserted exactly
        // there. Elsewhere libm differs in the last ulp; the 1e-8-day tolerance (under a
        // millisecond) is a starting point, not a measurement. PTO is an iterative step search,
        // so a flipped comparison would move it by a whole step and fail loudly rather than drift.
        [Theory]
        [InlineData(SwissEph.SE_HELFLAG_AVKIND_VR, 0, 2459017.6245663292)]
        [InlineData(SwissEph.SE_HELFLAG_AVKIND_PTO, 36436, 2459017.6081972825)]
        [InlineData(SwissEph.SE_HELFLAG_AVKIND_MIN7, 0, 2459018.6109788935)]
        [InlineData(SwissEph.SE_HELFLAG_AVKIND_MIN9, 0, 2459019.5987293785)]
        public void ArcusVisionisModes_MatchTheC(int avkind, int expectedRc, double expectedJd)
        {
            using (var sweph = new SwissEph())
            {
                double[] geopos = { 8.55, 47.38, 400 };
                double[] datm = { 1013.25, 15, 40, 0 };
                double[] dobs = new double[6];
                double[] dret = new double[50];
                string serr = null;
                double tjd = sweph.swe_julday(2020, 1, 1, 0.0, SwissEph.SE_GREG_CAL);

                int rc = sweph.swe_heliacal_ut(tjd, geopos, datm, dobs, "venus", SwissEph.SE_HELIACAL_RISING,
                    avkind | SwissEph.SEFLG_MOSEPH, dret, ref serr);

                Assert.Equal(expectedRc, rc);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && RuntimeInformation.ProcessArchitecture == Architecture.X64)
                    Assert.Equal(expectedJd, dret[0]);
                else
                    Assert.Equal(expectedJd, dret[0], 1e-8);
            }
        }
    }
}
