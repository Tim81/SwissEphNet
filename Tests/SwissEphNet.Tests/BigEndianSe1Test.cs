using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// read_const (<c>external/swisseph/sweph.c</c>:4644-4652) decides the byte order of an
    /// ephemeris file by looking at the first byte of <c>testendian</c>, which was read raw with
    /// fread and so sits in memory exactly as the file stores it. <c>fendian</c> is therefore the
    /// FILE's byte order. The port had replaced that test with <c>BitConverter.IsLittleEndian</c>,
    /// the HOST's byte order. The two differ only for a file whose byte order is not the host's,
    /// and then do_fread (sweph.c:4946) places the bytes of every read whose size differs from its
    /// corrsize wrongly: the 3-byte segment pointers and the 1-byte iflg/ncoe fields.
    ///
    /// Every .se1 file in the repository and upstream is little-endian, so nothing exercised this.
    /// These tests build a big-endian copy of each fixture (<see cref="ToBigEndian"/>), serve it
    /// to one instance and the original to another, and require bit-identical results. The same
    /// converter was used to write big-endian files that the reference C swetest read back
    /// identically to the little-endian originals, so the synthetic file is valid to the real C.
    /// </summary>
    public class BigEndianSe1Test
    {
        private static byte[] Resource(string name)
        {
            using (var s = ResourceFileHelpers.OpenResourceFile(name))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static SwissEph Open(byte[] se1Bytes, string se1Name)
        {
            var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                string fn = ResourceFileHelpers.GetPortableFileName(path);
                if (fn.Equals(se1Name, StringComparison.OrdinalIgnoreCase))
                    return new MemoryStream(se1Bytes, writable: false);
                return typeof(BigEndianSe1Test).GetAssembly().GetManifestResourceStream(
                    path.Replace("[ephe]", "SwissEphNet.Tests.files", StringComparison.Ordinal).Replace("/", ".", StringComparison.Ordinal).Replace("\\", ".", StringComparison.Ordinal));
            });
            return swe;
        }

        private static void Compare(byte[] le, string name, bool ast, int[] bodies, double[] tjds)
        {
            byte[] be = ToBigEndian(le, ast);
            Assert.NotEqual(le, be);
            using (var a = Open(le, name))
            using (var b = Open(be, name))
            {
                foreach (int body in bodies)
                    foreach (double tjd in tjds)
                    {
                        var xa = new double[6]; var xb = new double[6];
                        string sa = null, sb = null;
                        int ra = a.swe_calc_ut(tjd, body, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, xa, ref sa);
                        int rb = b.swe_calc_ut(tjd, body, SwissEph.SEFLG_SWIEPH | SwissEph.SEFLG_SPEED, xb, ref sb);
                        string ctx = "body " + body + " tjd " + tjd;
                        Assert.True(ra >= 0, "LE " + ctx + ": " + sa);
                        Assert.True(rb >= 0, "BE " + ctx + ": " + sb);
                        Assert.Equal(ra, rb);
                        // The Sun's sepl_18.se1 is not a test resource, so ret may carry a Moshier
                        // fallback warning for the Sun on BOTH sides; that is not the file under test.
                        // The BE file must produce the same message as the LE one, and never one that
                        // names it as missing or damaged.
                        Assert.Equal(sa, sb);
                        Assert.True(string.IsNullOrEmpty(sb) || (!sb.Contains("damaged", StringComparison.Ordinal) && !sb.Contains(name, StringComparison.OrdinalIgnoreCase)), "BE " + ctx + ": " + sb);
                        for (int k = 0; k < 6; k++)
                            Assert.True(BitConverter.DoubleToInt64Bits(xa[k]) == BitConverter.DoubleToInt64Bits(xb[k]),
                                ctx + " field " + k + ": LE " + xa[k].ToString("R", CultureInfo.InvariantCulture) + " BE " + xb[k].ToString("R", CultureInfo.InvariantCulture));
                    }
            }
        }

        [Fact]
        public void Test_Converter_ProducesExpectedLayout()
        {
            byte[] le1 = Resource("seas_18.se1");
            byte[] be1 = ToBigEndian(le1, false);
            Assert.Equal(116, TestEndianOffset);
            Assert.Equal(6, Planets);
            Assert.Equal(742, Lndx0[0]);
            Assert.Equal(221, Nndx[0]);
            Assert.Equal(new byte[] { 0x00, 0x61, 0x62, 0x63 }, new[] { be1[116], be1[117], be1[118], be1[119] });
            Assert.NotEqual(le1, be1);

            byte[] le2 = Resource("se00005s.se1");
            byte[] be2 = ToBigEndian(le2, true);
            Assert.Equal(410, TestEndianOffset);
            Assert.Equal(1, Planets);
            Assert.Equal(606, Lndx0[0]);
            Assert.Equal(220, Nndx[0]);
            Assert.Equal(new byte[] { 0x00, 0x61, 0x62, 0x63 }, new[] { be2[410], be2[411], be2[412], be2[413] });
            Assert.NotEqual(le2, be2);
        }

        [Fact]
        public void Test_SeasFile_BigEndianCopy_MatchesLittleEndian()
        {
            byte[] le = Resource("seas_18.se1");
            // Chiron: tfstart JD 2378496.5, dseg 1000 days. Dates spread across the file's range
            // so several segments are read; all stay inside every body's range.
            var tjds = new double[] { 2379000.5, 2395000.5, 2410000.5, 2425000.5, 2440000.5,
                2450000.5, 2455000.5, 2458000.5, 2460000.5, 2462000.5, 2465000.5, 2470000.5 };
            var bodies = new int[] { SwissEph.SE_CHIRON, SwissEph.SE_PHOLUS, SwissEph.SE_CERES,
                SwissEph.SE_PALLAS, SwissEph.SE_JUNO, SwissEph.SE_VESTA };
            Compare(le, "seas_18.se1", false, bodies, tjds);
        }

        [Fact]
        public void Test_AsteroidFile_BigEndianCopy_MatchesLittleEndian()
        {
            byte[] le = Resource("se00005s.se1");
            var tjds = new List<double>();
            double start;
            using (var s = new SwissEph()) start = s.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
            for (int i = -6; i <= 6; i++) tjds.Add(start + i * 4000.0 + (i * 37));
            Compare(le, "se00005s.se1", true, new[] { SwissEph.SE_AST_OFFSET + 5 }, tjds.ToArray());
        }

        // Builds a big-endian copy of a little-endian .se1 file by reversing every multi-byte
        // field in place. The layout mirrors read_const (sweph.c:4524-4873) and get_new_segment
        // (sweph.c:4370-4496). Bytes that are never visited (header text, the 30-byte asteroid
        // name, single-byte fields, the packed 1-byte and nibble blocks) are left as they are.
        // Neither fixture exercises SEI_FLG_ELLIPSE (iflg & 4); that branch is written from the C
        // but is not covered by a fixture.
        internal static byte[] ToBigEndian(byte[] le, bool asteroidFile)
        {
            byte[] o = (byte[])le.Clone();
            int p = 0;
            for (int line = 0; line < (asteroidFile ? 4 : 3); line++)
            {
                while (p < o.Length && o[p] != (byte)'\n') p++;
                p++;
            }
            TestEndianOffset = p;
            Rev(o, p, 4); p += 4;          // testendian
            Rev(o, p, 4); p += 4;          // file length
            Rev(o, p, 4); p += 4;          // DE number
            Rev(o, p, 8); p += 8;          // tfstart
            Rev(o, p, 8); p += 8;          // tfend
            int nplanRaw = BinaryPrimitives.ReadInt16LittleEndian(Span(le, p, 2));
            Rev(o, p, 2); p += 2;
            int ipls = 2;
            int npl = nplanRaw;
            if (npl > 256) { ipls = 4; npl %= 256; }
            for (int i = 0; i < npl; i++) { Rev(o, p, ipls); p += ipls; }
            if (asteroidFile) p += 30;     // asteroid name, verbatim
            int crcpos = p;
            p += 4;                        // CRC, filled in last
            for (int i = 0; i < 5; i++) { Rev(o, p, 8); p += 8; }
            var lndx0 = new int[npl];
            var nndx = new int[npl];
            for (int i = 0; i < npl; i++)
            {
                lndx0[i] = BinaryPrimitives.ReadInt32LittleEndian(Span(le, p, 4));
                Rev(o, p, 4); p += 4;
                int iflg = le[Chk(le, p)]; p += 1;
                int ncoe = le[Chk(le, p)]; p += 1;
                Rev(o, p, 4); p += 4;      // rmax
                Chk(le, p + 23);
                double d0 = BitConverter.ToDouble(le, p);
                double d1 = BitConverter.ToDouble(le, p + 8);
                double d2 = BitConverter.ToDouble(le, p + 16);
                for (int k = 0; k < 10; k++) { Rev(o, p, 8); p += 8; }
                nndx[i] = (int)((d1 - d0 + 0.1) / d2);
                if ((iflg & 4) != 0)
                    for (int k = 0; k < 2 * ncoe; k++) { Rev(o, p, 8); p += 8; }
            }
            Planets = npl; Lndx0 = lndx0; Nndx = nndx;
            var seen = new HashSet<int>();
            for (int i = 0; i < npl; i++)
            {
                for (int s = 0; s < nndx[i]; s++)
                {
                    int ip = lndx0[i] + 3 * s;
                    Chk(le, ip + 2);
                    int ptr = le[ip] | (le[ip + 1] << 8) | (le[ip + 2] << 16);
                    Rev(o, ip, 3);
                    if (!seen.Add(ptr)) continue;
                    int q = ptr;
                    for (int c = 0; c < 3; c++)
                    {
                        int c0 = le[Chk(le, q)], c1 = le[Chk(le, q + 1)];
                        q += 2;
                        var ns = new int[6];
                        if ((c0 & 128) != 0)
                        {
                            int c2 = le[Chk(le, q)], c3 = le[Chk(le, q + 1)];
                            q += 2;
                            ns[0] = c1 / 16; ns[1] = c1 % 16; ns[2] = c2 / 16; ns[3] = c2 % 16;
                            ns[4] = c3 / 16; ns[5] = c3 % 16;
                        }
                        else
                        {
                            ns[0] = c0 / 16; ns[1] = c0 % 16; ns[2] = c1 / 16; ns[3] = c1 % 16;
                        }
                        for (int k = 0; k < 4; k++)
                            for (int m = 0; m < ns[k]; m++) { Rev(o, q, 4 - k); q += 4 - k; }
                        q += (ns[4] + 1) / 2;
                        q += (ns[5] + 3) / 4;
                    }
                    Chk(le, q - 1);
                }
            }
            uint crc = SwiCrc32(o, crcpos);
            BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(o, crcpos, 4), crc);
            return o;
        }

        internal static int TestEndianOffset, Planets;
        internal static int[] Lndx0, Nndx;

        private static ReadOnlySpan<byte> Span(byte[] b, int off, int len)
        {
            Chk(b, off + len - 1);
            return new ReadOnlySpan<byte>(b, off, len);
        }

        private static int Chk(byte[] b, int idx)
        {
            if (idx < 0 || idx >= b.Length)
                throw new InvalidOperationException("ToBigEndian: offset " + idx + " is outside the " + b.Length + "-byte file");
            return idx;
        }

        private static void Rev(byte[] b, int off, int len)
        {
            Chk(b, off); Chk(b, off + len - 1);
            Array.Reverse(b, off, len);
        }

        // Reproduced from SwephLib.cs's init_crc32/swi_crc32 (SwephLib is internal to SwissEphNet).
        internal static uint SwiCrc32(byte[] buf, int len)
        {
            const uint poly = 0x04c11db7;
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; ++i)
            {
                uint c = i << 24;
                for (int j = 8; j > 0; --j)
                    c = (c & 0x80000000) != 0 ? (c << 1) ^ poly : (c << 1);
                table[i] = c;
            }
            uint crc = 0xffffffff;
            for (int p = 0; p < len; ++p)
                crc = (crc << 8) ^ table[((crc >> 24) ^ buf[p]) & 0xff];
            return ~crc;
        }
    }
}
