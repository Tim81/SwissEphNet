using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_get_current_file_data (Sweph.cs, sweph.c:8285-8306) had no coverage anywhere in the
    /// suite: no unit test called it, the conformance dispatcher never reaches it (no suite
    /// exercises it against t.exp), and it feeds no baseline generator. Stubbing it to
    /// unconditionally "return null" -- which every caller's null-check treats as "no file
    /// loaded", the same outcome as ifno being out of range or no file having been opened yet --
    /// produced no failures anywhere. This closes that gap by asserting the real, non-null data
    /// swe_get_current_file_data reports once a real ephemeris file has actually been loaded,
    /// which a permanently-null stub cannot produce.
    /// </summary>
    public class GetCurrentFileDataCoverageTest
    {
        [Fact]
        public void TestGetCurrentFileData_OutOfRangeIfno_ReturnsNull()
        {
            using (var swe = new SwissEph())
            {
                double tfstart = -1, tfend = -1;
                int denum = -1;

                Assert.Null(swe.swe_get_current_file_data(-1, ref tfstart, ref tfend, ref denum));
                Assert.Null(swe.swe_get_current_file_data(5, ref tfstart, ref tfend, ref denum));
            }
        }

        [Fact]
        public void TestGetCurrentFileData_NoFileLoadedYet_ReturnsNull()
        {
            using (var swe = new SwissEph())
            {
                double tfstart = -1, tfend = -1;
                int denum = -1;

                // ifno = 2: main asteroid file slot, untouched since construction.
                Assert.Null(swe.swe_get_current_file_data(2, ref tfstart, ref tfend, ref denum));
            }
        }

        [Fact]
        public void TestGetCurrentFileData_AfterLoadingAsteroidFile_ReportsRealFileData()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (File.Exists(path))
                    {
                        return new FileStream(path, FileMode.Open, FileAccess.Read);
                    }
                    return ResourceFileHelpers.OpenResourceFile(fn);
                });

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6];
                string serr = null;

                // Loads Tests/SwissEphNet.Tests/files/seas_18.se1 into swed.fidat[2] (main
                // asteroid file), the same fixture and flag PlaDiamCoverageTest uses for
                // Ceres: swe_calc's minor-planet dispatch always requests seas_18.se1
                // regardless of the ephemeris flag, and SEFLG_MOSEPH computes Earth and the
                // Sun analytically, so seas_18.se1 is the only file this needs.
                int rc = swe.swe_calc(tjd, SwissEph.SE_CERES, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.False(rc == SwissEph.ERR, serr);

                double tfstart = 0, tfend = 0;
                int denum = -1;
                string fnam = swe.swe_get_current_file_data(2, ref tfstart, ref tfend, ref denum);

                Assert.NotNull(fnam);
                Assert.Contains("seas_18", fnam, StringComparison.Ordinal);
                Assert.True(tfstart < tfend, $"tfstart={tfstart} should be before tfend={tfend}");
                Assert.NotEqual(0, tfstart);
                Assert.NotEqual(0, tfend);

                // Untouched slots stay null: only ifno = 2 was ever loaded.
                Assert.Null(swe.swe_get_current_file_data(0, ref tfstart, ref tfend, ref denum));
                Assert.Null(swe.swe_get_current_file_data(1, ref tfstart, ref tfend, ref denum));
            }
        }
    }
}
