using System;
using System.IO;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// read_elements_file (swemplan.c:755) splits an elements line with swi_cutstr, which merges
    /// runs of commas but yields an empty field after a trailing comma (and one before a leading
    /// comma). The port used Split(',', RemoveEmptyEntries) and so counted one field fewer for
    /// "J2000,J2000,10.0,2.5,0.1,20,30,5," and rejected it with "nine elements required".
    /// Reference (C library built from external/swisseph, SEFLG_MOSEPH, tjd 2451545, ipl 40):
    /// rc 4, x[0] = 39.81694644150042.
    /// </summary>
    public class ElementsLineCutstrTest
    {
        [Fact]
        public void Test_ElementsLineWithTrailingComma_CountsTheEmptyFieldLikeTheC()
        {
            byte[] file = Encoding.ASCII.GetBytes("J2000,J2000,10.0,2.5,0.1,20,30,5,\r\n");
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                ResourceFileHelpers.GetPortableFileName(path).Equals("seorbel.txt", StringComparison.OrdinalIgnoreCase)
                    ? new MemoryStream(file, writable: false)
                    : null);
            double[] xx = new double[6];
            string serr = null;
            int rc = swe.swe_calc(2451545.0, SwissEph.SE_FICT_OFFSET, SwissEph.SEFLG_MOSEPH, xx, ref serr);
            Assert.True(rc >= 0, serr);
            Assert.Equal(39.81694644150042, xx[0], 9);
        }
    }
}
