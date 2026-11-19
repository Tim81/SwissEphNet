using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// A FileProvider may hand back a forward-only stream (GZipStream, a network stream).
    /// CFile needs Seek and Position, so OpenBinary buffers such a stream into a MemoryStream.
    /// Each test serves every file through a GZipStream over the shipped resource and requires
    /// results bit-identical to a plain-provider instance.
    /// </summary>
    public class NonSeekableProviderTest
    {
        private static Stream Resource(string path)
        {
            return ResourceFileHelpers.OpenResourceFile(ResourceFileHelpers.GetPortableFileName(path));
        }

        private static Stream Gzipped(Stream raw)
        {
            if (raw == null) return null;
            var packed = new MemoryStream();
            using (raw)
            using (var gz = new GZipStream(packed, CompressionMode.Compress, leaveOpen: true))
                raw.CopyTo(gz);
            packed.Position = 0;
            var result = new GZipStream(packed, CompressionMode.Decompress);
            Assert.False(result.CanSeek);
            return result;
        }

        private static SwissEph Open(bool gzip)
        {
            var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                var s = Resource(path);
                return gzip ? Gzipped(s) : s;
            });
            return swe;
        }

        private static void AssertSame(double[] a, double[] b, string ctx)
        {
            for (int k = 0; k < a.Length; k++)
                Assert.True(BitConverter.DoubleToInt64Bits(a[k]) == BitConverter.DoubleToInt64Bits(b[k]),
                    ctx + " field " + k + ": " + a[k].ToString("R", CultureInfo.InvariantCulture) + " vs " + b[k].ToString("R", CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData(SwissEph.SE_CHIRON)]
        [InlineData(SwissEph.SE_AST_OFFSET + 5)]
        public void Test_swe_calc_ut_Swieph_NonSeekableStream_MatchesPlain(int body)
        {
            using (var plain = Open(false))
            using (var gz = Open(true))
            {
                double tjd = plain.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                var xp = new double[6]; var xg = new double[6];
                string sp = null, sg = null;
                int rp = plain.swe_calc_ut(tjd, body, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, xp, ref sp);
                int rg = gz.swe_calc_ut(tjd, body, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, xg, ref sg);
                Assert.True(rp >= 0, sp);
                Assert.Equal(rp, rg);
                Assert.Equal(sp, sg);
                AssertSame(xp, xg, "body " + body);
            }
        }

        [Fact]
        public void Test_swe_fixstar2_ut_NonSeekableStream_MatchesPlain()
        {
            using (var plain = Open(false))
            using (var gz = Open(true))
            {
                double tjd = plain.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                var xp = new double[6]; var xg = new double[6];
                string starP = "aldebaran", starG = "aldebaran", sp = null, sg = null;
                int rp = plain.swe_fixstar2_ut(ref starP, tjd, SwissEph.SEFLG_SWIEPH, xp, ref sp);
                int rg = gz.swe_fixstar2_ut(ref starG, tjd, SwissEph.SEFLG_SWIEPH, xg, ref sg);
                Assert.True(rp >= 0, sp);
                Assert.Equal(rp, rg);
                Assert.Equal(starP, starG);
                Assert.Equal(sp, sg);
                AssertSame(xp, xg, "aldebaran");
            }
        }
    }
}
