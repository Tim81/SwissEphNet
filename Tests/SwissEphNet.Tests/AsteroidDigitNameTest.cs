using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// read_const (sweph.c:4614-4617) finds the end of the "MPC number and name" prefix of an
    /// asteroid file's element line with three steps: skip spaces, skip digits, step over one
    /// more character. The port had replaced them with a single IndexOfFirstNot(" 0123456789"),
    /// which also swallowed the digits and spaces at the start of a NAME such as "1999 AB1", so
    /// the offset i was 5 columns too large and ast_H, ast_G and the diameter were read from the
    /// wrong columns.
    ///
    /// Fixture: files/se00005s_digitname.se1 is se00005s.se1 with the name "Vesta" changed to
    /// "1999 AB1" (same length) and the header CRC recomputed. The reference value below was
    /// produced by a probe linked against the C library built from external/swisseph
    /// (swe_pheno, SEFLG_SWIEPH, tjd 2451545.5, ipl SE_AST_OFFSET+5), %.17g.
    /// </summary>
    public class AsteroidDigitNameTest
    {
        [Fact]
        public void Test_AsteroidNameStartingWithDigits_ReadsHAndGFromTheRightColumns()
        {
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                string fn = ResourceFileHelpers.GetPortableFileName(path);
                if (fn.Equals("se00005s.se1", StringComparison.OrdinalIgnoreCase))
                {
                    return ResourceFileHelpers.OpenResourceFile("se00005s_digitname.se1");
                }
                return null;
            });

            double[] attr = new double[20];
            string serr = null;
            int rc = swe.swe_pheno(2451545.5, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, attr, ref serr);
            Assert.True(rc >= 0, serr);

            Assert.Equal("1999 AB1", swe.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 5));
            // C: 11.704163330815701. With the old column offset the magnitude came out near 4.3.
            Assert.Equal(11.704163330815701, attr[4], 9);
        }
    }
}
