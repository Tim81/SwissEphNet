using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swephexp.h declares swe_lun_occult_when_glob's and swe_lun_occult_when_loc's last data
    /// parameter before serr as <c>int32 backward</c>, a bitfield: bit 0 selects search direction
    /// and OR-ing in <see cref="SwissEph.SE_ECL_ONE_TRY"/> (32768; swecl.c:1539, documented at
    /// swecl.c:1535-1545) limits the global-occultation search to a single lunar cycle instead of
    /// continuing until an occultation is found, which for some (starname, tjd_start) pairs can
    /// run for years. Both port entry points (SwissEph.swephexp.h.cs) and the underlying
    /// implementation (SwissEphNet/CPort/SweCL.cs's own swe_lun_occult_when_glob and
    /// swe_lun_occult_when_loc, which called occult_when_loc) took <c>bool backward</c> instead --
    /// `(bool ? 1 : 0) &amp; 32768` is provably always 0, so SE_ECL_ONE_TRY could never reach the
    /// mask at swecl.c:1593/:2436 through the old signature. Programs/SweTest/Program.cs's own
    /// call sites (swetest.c:3525, :3617 both OR it in unconditionally) carried the OR commented
    /// out for exactly this reason.
    ///
    /// Measured directly against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (a small
    /// C driver calling swe_lun_occult_when_glob(2451604.5, 0, "aldebaran", SEFLG_MOSEPH, 0, tret,
    /// backward, serr) -- Moshier so the comparison needs only the already-embedded sefstars.txt
    /// fixture, not a wide-range .se1 segment file): with backward=0 the C searches until it
    /// finds the next occultation, 5,447.72 days (~15 years) later, ret=6. With backward=32768
    /// (SE_ECL_ONE_TRY) it gives up after one lunar cycle, ret=0, "no solar eclipse at tjd =
    /// 2451615.848510". The .NET port, once its backward parameters actually carry Int32, is bit-
    /// identical to the C on both calls, including the C-formatted error string.
    ///
    /// swe_lun_occult_when_loc took the identical defect and got the identical Int32 fix (commit
    /// 1a8dae4), but had no test of its own until the three _loc-suffixed tests below were added
    /// alongside these -- see their own remarks for the matching C-reference measurements.
    /// </summary>
    public class EclOneTryTest
    {
        const double Tjd_start = 2451604.5;
        const string Star = "aldebaran";

        [Fact]
        public void Test_swe_lun_occult_when_glob_WithoutOneTry_SearchesUntilFound()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] tret = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_glob(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, 0, tret, 0, ref serr);

                Assert.Equal(6, ret);
                Assert.Equal(2457052.22102853, tret[0], 6);
                Assert.Null(serr);
            }
        }

        [Fact]
        public void Test_swe_lun_occult_when_glob_WithOneTry_GivesUpAfterOneLunarCycle()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] tret = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_glob(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, 0, tret, SwissEph.SE_ECL_ONE_TRY, ref serr);

                Assert.Equal(0, ret);
                Assert.Equal(2451615.84777088, tret[0], 6);
                Assert.Equal("no solar eclipse at tjd = 2451615.848510", serr);
            }
        }

        [Fact]
        public void Test_swe_lun_occult_when_glob_BoolOverload_CannotRequestOneTry()
        {
            // Source-compatibility overload: bool can only ever contribute 0 or 1, so it produces
            // the same "search until found" result as backward=0 above, regardless of which bool
            // value is passed -- there is no way to request SE_ECL_ONE_TRY through this overload,
            // which is exactly the defect the Int32 overload above exists to fix.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] tret = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_glob(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, 0, tret, false, ref serr);

                Assert.Equal(6, ret);
                Assert.Equal(2457052.22102853, tret[0], 6);
            }
        }

        // swe_lun_occult_when_loc took the same bool-narrowing defect as swe_lun_occult_when_glob
        // above (commit 1a8dae4 gave its public entry point Int32 backward, matching the private
        // occult_when_loc it already called through -- see SweCL.cs:2113's remarks), but no test
        // exercised it: the three tests above cover only _glob. These three mirror them for _loc,
        // adding a geographic location (London: lon=0, lat=51.5, alt=0) since a local occultation
        // needs one. Measured directly against external/.c-reference/build-2.10.03/libswe-2.10.03.lib
        // (a small C driver calling swe_lun_occult_when_loc(2451604.5, 0, "aldebaran", SEFLG_MOSEPH,
        // geopos, tret, attr, backward, serr) against the same embedded sefstars.txt fixture): with
        // backward=0 the C searches until it finds the next local occultation, ret=24452, tret[0] =
        // 2457270.72891109, serr empty. With backward=32768 (SE_ECL_ONE_TRY) it gives up after one
        // lunar cycle, ret=0, tret[0] = 2451616.83247977, serr also empty (occult_when_loc's give-up
        // path does not always set serr the way occult_when_glob's does -- verified against the C,
        // not assumed). The .NET port, once its backward parameter actually carries Int32, matches.
        [Fact]
        public void Test_swe_lun_occult_when_loc_WithoutOneTry_SearchesUntilFound()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] geopos = { 0, 51.5, 0 };
                double[] tret = new double[30];
                double[] attr = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_loc(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, geopos, tret, attr, 0, ref serr);

                Assert.Equal(24452, ret);
                Assert.Equal(2457270.72891109, tret[0], 6);
                Assert.Null(serr);
            }
        }

        [Fact]
        public void Test_swe_lun_occult_when_loc_WithOneTry_GivesUpAfterOneLunarCycle()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] geopos = { 0, 51.5, 0 };
                double[] tret = new double[30];
                double[] attr = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_loc(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, geopos, tret, attr, SwissEph.SE_ECL_ONE_TRY, ref serr);

                Assert.Equal(0, ret);
                Assert.Equal(2451616.83247977, tret[0], 6);
                Assert.Null(serr);
            }
        }

        [Fact]
        public void Test_swe_lun_occult_when_loc_BoolOverload_CannotRequestOneTry()
        {
            // Source-compatibility overload: bool can only ever contribute 0 or 1, so it produces
            // the same "search until found" result as backward=0 above, regardless of which bool
            // value is passed -- there is no way to request SE_ECL_ONE_TRY through this overload,
            // which is exactly the defect the Int32 overload above exists to fix.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    return null;
                });

                double[] geopos = { 0, 51.5, 0 };
                double[] tret = new double[30];
                double[] attr = new double[30];
                string serr = null;
                string star = Star;

                int ret = swe.swe_lun_occult_when_loc(Tjd_start, 0, star, SwissEph.SEFLG_MOSEPH, geopos, tret, attr, false, ref serr);

                Assert.Equal(24452, ret);
                Assert.Equal(2457270.72891109, tret[0], 6);
            }
        }
    }
}
