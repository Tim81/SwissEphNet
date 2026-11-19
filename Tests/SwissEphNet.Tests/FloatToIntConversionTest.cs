using System;
using System.Threading.Tasks;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// The C library converts double to int with a truncating conversion. On the reference platform
    /// (MSVC x64, cvttsd2si) a NaN, an infinity or any value outside the int range becomes
    /// 0x80000000 (INT_MIN), and 0x8000000000000000 for a 64-bit target. .NET 8 on x64 does the
    /// same; .NET 9 made the conversion saturating (NaN gives 0, large values give int.MaxValue or
    /// int.MinValue), so a bare C# cast departs from the C, and from the port's own net8.0 build,
    /// for such inputs. The port routes every double-to-integer cast through C.ToInt32 and
    /// C.ToInt64, which emulate the C result on every runtime. These tests run on net8.0 and
    /// net10.0 and would fail on net10.0 with plain casts.
    /// </summary>
    public class FloatToIntConversionTest
    {
        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(1.9, 1)]
        [InlineData(-1.9, -1)]
        [InlineData(-0.5, 0)]
        [InlineData(2147483647.0, int.MaxValue)]
        [InlineData(2147483647.9, int.MaxValue)]
        [InlineData(-2147483648.0, int.MinValue)]
        [InlineData(-2147483648.9, int.MinValue)]
        public void ToInt32_InRange_Truncates(double d, int expected)
        {
            Assert.Equal(expected, C.ToInt32(d));
        }

        [Theory]
        [InlineData(2147483648.0)]
        [InlineData(-2147483649.0)]
        [InlineData(1e12)]
        [InlineData(-1e12)]
        [InlineData(1e300)]
        [InlineData(double.MaxValue)]
        [InlineData(double.MinValue)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        public void ToInt32_OutOfRange_IsIntMinValue(double d)
        {
            Assert.Equal(int.MinValue, C.ToInt32(d));
        }

        [Theory]
        [InlineData(0.0, 0L)]
        [InlineData(-1.9, -1L)]
        [InlineData(1e12, 1000000000000L)]
        [InlineData(-9223372036854775808.0, long.MinValue)]
        [InlineData(9223372036854774784.0, 9223372036854774784L)]
        public void ToInt64_InRange_Truncates(double d, long expected)
        {
            Assert.Equal(expected, C.ToInt64(d));
        }

        [Theory]
        [InlineData(9223372036854775808.0)]
        [InlineData(-1e300)]
        [InlineData(1e300)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        public void ToInt64_OutOfRange_IsLongMinValue(double d)
        {
            Assert.Equal(long.MinValue, C.ToInt64(d));
        }

        /// <summary>
        /// swetest -j1e12 -p0 -fJT -head -emos, built from the pinned C and run on MSVC x64, prints
        /// "20.11.-2147483648 12:00:00 TT": swe_revjul's year is INT_MIN. Plain casts on net10.0 give
        /// int.MaxValue.
        /// </summary>
        [Fact]
        public void swe_revjul_HugeJd_MatchesC()
        {
            using (var swe = new SwissEph())
            {
                int y = 0, m = 0, d = 0;
                double h = 0;
                swe.swe_revjul(1e12, SwissEph.SE_GREG_CAL, ref y, ref m, ref d, ref h);
                Assert.Equal(int.MinValue, y);
                Assert.Equal(11, m);
                Assert.Equal(20, d);
                Assert.Equal(12.0, h);
            }
        }

        /// <summary>
        /// The C returns ERR for a NaN start date (the segment index becomes INT_MIN and the
        /// ephemeris file is reported damaged); on net10.0 the saturating cast made the port search
        /// forever. Needs the real sepl_18.se1 and semo_18.se1, which are not embedded in this
        /// project (they are 1.8 MB together), so it looks for them in the external/swisseph
        /// submodule, or SWISSEPH_CONFORMANCE_EPHE, and does nothing when neither is present. Run
        /// under a timeout so a regression fails instead of hanging the run.
        /// </summary>
        [Fact]
        public void swe_lun_occult_when_glob_NaN_ReturnsErrorPromptly()
        {
            string ephe = FindEpheDir();
            if (ephe == null)
                return;
            RunWithTimeout(() =>
            {
                using (var swe = new SwissEph())
                {
                    swe.swe_set_ephe_path(ephe);
                    var tret = new double[10];
                    string serr = null;
                    return swe.swe_lun_occult_when_glob(double.NaN, SwissEph.SE_MARS, null,
                        SwissEph.SEFLG_SWIEPH, 0, tret, 0, ref serr);
                }
            }, rc => Assert.True(rc < 0, "expected an error return, got " + rc));
        }

        static string FindEpheDir()
        {
            string env = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_EPHE");
            if (!string.IsNullOrEmpty(env))
                return System.IO.File.Exists(System.IO.Path.Combine(env, "sepl_18.se1")) ? env : null;
            for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "external", "swisseph", "ephe");
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "sepl_18.se1"))
                    && System.IO.File.Exists(System.IO.Path.Combine(candidate, "semo_18.se1")))
                    return candidate;
            }
            return null;
        }

        [Fact]
        public void swe_sol_eclipse_when_glob_NaN_ReturnsErr()
        {
            RunWithTimeout(() =>
            {
                using (var swe = new SwissEph())
                {
                    var tret = new double[10];
                    string serr = null;
                    return swe.swe_sol_eclipse_when_glob(double.NaN, SwissEph.SEFLG_MOSEPH, 0, tret, false, ref serr);
                }
            }, rc => Assert.Equal(SwissEph.ERR, rc));
        }

        [Fact]
        public void swe_lun_eclipse_when_NaN_ReturnsErr()
        {
            RunWithTimeout(() =>
            {
                using (var swe = new SwissEph())
                {
                    var tret = new double[10];
                    string serr = null;
                    return swe.swe_lun_eclipse_when(double.NaN, SwissEph.SEFLG_MOSEPH, 0, tret, false, ref serr);
                }
            }, rc => Assert.Equal(SwissEph.ERR, rc));
        }

        /// <summary>
        /// swe_calc(NaN, SE_MEAN_NODE, MOSEPH): the C indexes the correction table with an INT_MIN
        /// subscript and segfaults (see docs/known-issues.md). The port's net8.0 build throws
        /// IndexOutOfRangeException from SwemMoon; net10.0 used to return NaN silently because the
        /// saturating cast gave index 0. Both runtimes now throw.
        /// </summary>
        [Fact]
        public void swe_calc_NaN_MeanNode_ThrowsIndexOutOfRange_OnEveryRuntime()
        {
            using (var swe = new SwissEph())
            {
                var xx = new double[6];
                string serr = null;
                Assert.Throws<IndexOutOfRangeException>(() =>
                    swe.swe_calc(double.NaN, SwissEph.SE_MEAN_NODE, SwissEph.SEFLG_MOSEPH, xx, ref serr));
            }
        }

        static void RunWithTimeout(Func<int> body, Action<int> check)
        {
            var task = Task.Run(body);
            Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "call did not return within 30 s");
            check(task.Result);
        }
    }
}
