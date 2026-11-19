using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// get_new_segment at tjd == tfend (sweph.c:4389 at upstream 43c9fe54). The range check lets
    /// the file's last date through, but the segment index computed for it is nndx, the index
    /// entry after the last segment, so the coefficients read were garbage: Astraea from
    /// se00005s.se1 jumped from 66.94 to 126.01 degrees of longitude (latitude -22.18) at
    /// JD 2488932.5, in the C before 43c9fe54 and in the port alike. The fix uses the last
    /// segment, which ends at tfend.
    /// </summary>
    public class AsteroidFileUpperLimitTest
    {
        private static SwissEph Open()
        {
            var swe = new SwissEph();
            // Resolve by bare file name: the asteroid lookup asks for ast0/se00005s.se1 first, and the
            // test resources are flat.
            swe.FileProvider = new DelegateFileProvider(path =>
                typeof(AsteroidFileUpperLimitTest).GetAssembly().GetManifestResourceStream(
                    "SwissEphNet.Tests.files." + ResourceFileHelpers.GetPortableFileName(path)));
            return swe;
        }

        // Reference values: Astrodienst's C at 43c9fe54 (external/swisseph), libswe built by
        // Tools/CReference/build-c.ps1 (MSVC, /O2 /fp:precise /MD), the same se00005s.se1, called
        // with exactly these inputs (SEFLG_SWIEPH | SEFLG_SPEED; no planet file, so the Earth comes
        // from Moshier and rc is 260) and printed with %.17g. Asserted exactly on Windows x64, the
        // platform the reference was built on; elsewhere libm may differ in the last ulp.
        [Theory]
        [InlineData(2488932.5, 66.943742215090566, -3.5490403403330388, 3.2881418706580301)]
        [InlineData(2488932.4999000002, 66.943693603270688, -3.5490414513134976, 3.2881417524493304)]
        public void AstraeaAtAndJustBeforeTheFileUpperLimit_MatchesTheC(double tjd, double lon, double lat, double dist)
        {
            using var swe = Open();
            var x = new double[6];
            string serr = null;

            int rc = swe.swe_calc(tjd, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, x, ref serr);

            Assert.Equal(260, rc);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                Assert.Equal(lon, x[0]);
                Assert.Equal(lat, x[1]);
                Assert.Equal(dist, x[2]);
            }
            else
            {
                Assert.Equal(lon, x[0], 1e-9);
                Assert.Equal(lat, x[1], 1e-9);
                Assert.Equal(dist, x[2], 1e-12);
            }
        }

        [Fact]
        public void PositionIsContinuousIntoTheLastDate()
        {
            using var swe = Open();
            var a = new double[6];
            var b = new double[6];
            string serr = null;

            // tfend first: evaluating an earlier date first would cache the last segment, whose end is
            // tfend, and the second call would then never reach get_new_segment.
            Assert.True(swe.swe_calc(2488932.5, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, b, ref serr) >= 0, serr);
            Assert.True(swe.swe_calc(2488932.5 - 1e-4, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, a, ref serr) >= 0, serr);

            // 1e-4 day at about 0.49 deg/day is 5e-5 deg; before the fix the step was 59 degrees.
            Assert.True(Math.Abs(b[0] - a[0]) < 1e-3, $"longitude stepped by {b[0] - a[0]} deg into tfend");
            Assert.True(Math.Abs(b[1] - a[1]) < 1e-3, $"latitude stepped by {b[1] - a[1]} deg into tfend");
        }
    }
}
