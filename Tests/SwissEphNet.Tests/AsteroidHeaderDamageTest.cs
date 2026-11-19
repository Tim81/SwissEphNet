using System;
using System.IO;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// read_const (sweph.c:4605-4612) reads the elements line of an asteroid file with fgets and
    /// rejects the file as damaged (0d) unless the line contains "\r\n". A planet file opened
    /// as an asteroid file, e.g. by swe_calc(2451545, SE_MARS, 0x32003E), has binary data in
    /// that place; the C ends with "... is damaged (0d)". The port's ReadLine strips the
    /// terminator and the check was only for an empty line, so it went on to Substring(35 + i)
    /// and threw ArgumentOutOfRangeException. A short but properly terminated line makes the C
    /// read past the string inside its buffer; the port reads the empty string there.
    /// </summary>
    public class AsteroidHeaderDamageTest
    {
        private static int Calc(byte[] file, out string serr)
        {
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                ResourceFileHelpers.GetPortableFileName(path).Equals("se00005s.se1", StringComparison.OrdinalIgnoreCase)
                    ? new MemoryStream(file, writable: false)
                    : null);
            double[] xx = new double[6];
            serr = null;
            return swe.swe_calc(2451545.0, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, xx, ref serr);
        }

        private static byte[] File(string elementsTerminator)
        {
            var ms = new MemoryStream();
            byte[] head = Encoding.ASCII.GetBytes("SWISSEPH 3\r\nse00005s.se1\r\nCopyright\r\nshort" + elementsTerminator);
            ms.Write(head, 0, head.Length);
            for (int i = 0; i < 700; i++) ms.WriteByte((byte)(i % 200 + 1 == '\n' || i % 200 + 1 == '\r' ? 'x' : i % 200 + 1));
            return ms.ToArray();
        }

        [Fact]
        public void Test_ElementsLineWithoutCrLf_IsDamaged_0d()
        {
            int rc = Calc(File("\n"), out string serr);
            Assert.True(rc < 0);
            Assert.Contains("(0d)", serr, StringComparison.Ordinal);
        }

        [Fact]
        public void Test_ShortElementsLineWithCrLf_DoesNotThrow()
        {
            int rc = Calc(File("\r\n"), out string serr);
            Assert.True(rc < 0);
            Assert.NotNull(serr);
        }
    }
}
