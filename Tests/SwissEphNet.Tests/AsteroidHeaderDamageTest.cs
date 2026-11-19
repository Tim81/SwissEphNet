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

        private static uint Crc32(byte[] buf, int len)
        {
            uint crc = 0xffffffff;
            for (int n = 0; n < len; n++)
            {
                crc ^= (uint)buf[n] << 24;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
            }
            return ~crc;
        }

        /// <summary>
        /// se00005s.se1 with its elements line replaced by <paramref name="elements"/> + "\r\n"
        /// and the file-length field and header CRC recomputed, so that the binary header after
        /// the line stays readable and read_const gets past the byte-order, length and CRC checks.
        /// </summary>
        private static byte[] RealFileWithElementsLine(string elements)
        {
            using var ms = new MemoryStream();
            using (var src = ResourceFileHelpers.OpenResourceFile("se00005s.se1")) src.CopyTo(ms);
            byte[] d = ms.ToArray();
            int p = 0;
            for (int n = 0; n < 3; n++)
                p = Array.IndexOf(d, (byte)'\n', p) + 1;
            int e = Array.IndexOf(d, (byte)'\n', p) + 1;
            // the CRC (little endian) follows the name fields, find it in the unmodified file
            int crcPos = -1;
            for (int q = e; q < 1200 && crcPos < 0; q++)
                if (BitConverter.ToUInt32(d, q) == Crc32(d, q)) crcPos = q;
            Assert.True(crcPos > 0);
            byte[] line = Encoding.ASCII.GetBytes(elements + "\r\n");
            byte[] nd = new byte[d.Length - (e - p) + line.Length];
            Array.Copy(d, 0, nd, 0, p);
            Array.Copy(line, 0, nd, p, line.Length);
            Array.Copy(d, e, nd, p + line.Length, d.Length - e);
            // length field: the 4 bytes after the byte-order test integer
            int lenPos = p + line.Length + 4;
            Assert.Equal((uint)d.Length, BitConverter.ToUInt32(nd, lenPos));
            BitConverter.GetBytes((uint)nd.Length).CopyTo(nd, lenPos);
            int newCrcPos = crcPos + line.Length - (e - p);
            BitConverter.GetBytes(Crc32(nd, newCrcPos)).CopyTo(nd, newCrcPos);
            return nd;
        }

        /// <summary>
        /// sweph.c:4605-4742: strncpy(sastnam, s, lastnam+i) copies the fgets line with its
        /// "\r\n" and zero-pads, then sastnam[j] and sastnam+j+1 are read without a bounds
        /// check. The C does not report damage there, it carries on with an empty or short name.
        /// Expected values measured with a probe linked to the MSVC build of the pinned C
        /// (swe_calc then swe_get_planet_name, SE_AST_OFFSET+5, SEFLG_SWIEPH): rc -1, serr ending
        /// "is damaged (2)." (the segment index no longer matches once the line is shortened),
        /// and the name below. "" and "7 x" give an empty or foreign number field, so
        /// the C takes the name from the old name field of the file ("? Astraea").
        /// </summary>
        [Theory]
        [InlineData("", "? Astraea")]
        [InlineData("5", "")]
        [InlineData("7 x", "? Astraea")]
        [InlineData("5 abcd", "")]
        [InlineData("5 abcdefghij", "j\r\n")]
        public void Test_ShortElementsLineWithReadableHeader_FollowsTheNulPaddedBuffer(string elements, string expectedName)
        {
            byte[] file = RealFileWithElementsLine(elements);
            int rc = Calc(file, out string serr);
            Assert.Equal(-1, rc);
            Assert.EndsWith("is damaged (2).", serr, StringComparison.Ordinal);
            using var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                ResourceFileHelpers.GetPortableFileName(path).Equals("se00005s.se1", StringComparison.OrdinalIgnoreCase)
                    ? new MemoryStream(file, writable: false)
                    : null);
            double[] xx = new double[6];
            string serr2 = null;
            swe.swe_calc(2451545.0, SwissEph.SE_AST_OFFSET + 5, SwissEph.SEFLG_SWIEPH, xx, ref serr2);
            Assert.Equal(expectedName, swe.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 5));
        }
    }
}
