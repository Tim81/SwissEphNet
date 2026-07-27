using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{

    public class CFileTest
    {

        static Stream BuildStream(byte[] content) {
            var result = new MemoryStream();
            result.Write(content, 0, content.Length);
            result.Seek(0, SeekOrigin.Begin);
            return result;
        }

        static Stream BuildStream(String content, Encoding enc = null) {
            enc = enc ?? System.Text.Encoding.UTF8;
            var bs = enc.GetBytes(content);
            return BuildStream(enc.GetBytes(content));
        }

        [Fact]
        public void TestCreate() {
            using (var stream = new System.IO.MemoryStream())
            using (var cfile = new CFile(stream))
            {
                // Every TFM this project targets is a modern .NET runtime, none of
                // which has Windows-1252 available without registering
                // System.Text.Encoding.CodePages (which SwissEphNet does not do --
                // see docs/known-issues.md and CFile's constructor), so CFile always
                // falls back to UTF-8 here.
                Assert.Equal("utf-8", cfile.Encoding.WebName);
                Assert.Equal(0, cfile.Length);
                Assert.Equal(0, cfile.Position);
                Assert.False(cfile.EOF);
                Assert.Equal(-1, cfile.Read());
                Assert.True(cfile.EOF);
            }
            using (var stream = new System.IO.MemoryStream())
            using (var cfile = new CFile(stream, Encoding.UTF8))
            {
                Assert.Equal("utf-8", cfile.Encoding.WebName);
                Assert.Equal(0, cfile.Length);
                Assert.Equal(0, cfile.Position);
                Assert.False(cfile.EOF);
                Assert.Equal(-1, cfile.Read());
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestReadLine() {
            using (var cfile = new CFile(BuildStream(""))) {
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(" "))) {
                Assert.Equal(" ", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r3"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Equal("3", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r3\n\r"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Equal("3", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r3\n\r4"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Equal("3", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("4", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r3\n\r4\r\n"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Equal("3", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("4", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n2\r3\n\r4\r\n5"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("2", cfile.ReadLine());
                Assert.Equal("3", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("4", cfile.ReadLine());
                Assert.Equal("5", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("1\n\n\n\r\r\r\n5"))) {
                Assert.Equal("1", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("", cfile.ReadLine());
                Assert.Equal("5", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestDefaultEncodingDoesNotRoundTripWindows1252Bytes() {
            // Ephemeris text files are historically Windows-1252-encoded (see
            // TestCreate and docs/known-issues.md), but no TFM this project
            // targets can decode Windows-1252 (Encoding.GetEncoding("Windows-1252")
            // throws without registering System.Text.Encoding.CodePages, which
            // SwissEphNet does not do), so CFile's constructor falls back to
            // UTF-8. Genuine Windows-1252 bytes >= 0x80 therefore do not
            // round-trip through a default-encoding CFile today: this pins that
            // failure down explicitly and precisely, rather than just omitting
            // Windows-1252 coverage, so that whenever a future change (PR1, per
            // docs/known-issues.md) makes real Windows-1252 decoding available
            // again, this test fails visibly and has to be looked at, instead of
            // the gap silently closing unnoticed.
            //
            // "èaà\nüî" encoded as Windows-1252 -- which, for these particular
            // accented characters, has the same single-byte values as Latin-1 --
            // would be the 6 bytes { 0xE8, 0x61, 0xE0, 0x0A, 0xFC, 0xEE }
            // (è, a, à, \n, ü, î). Decoded as UTF-8 instead: 0xE8/0xE0/0xFC/0xEE
            // all have a multi-byte UTF-8 lead-byte shape with no valid
            // continuation bytes following, so the decoder does not read back
            // 'è','a','à','\n','ü','î' at all -- it substitutes U+FFFD
            // (the Unicode replacement character), consuming the invalid lead
            // byte together with whatever follows it (even a plain ASCII byte
            // like the 'a' or '\n' in this input) as part of the same failed
            // sequence.
            byte[] windows1252Bytes = { 0xE8, 0x61, 0xE0, 0x0A, 0xFC, 0xEE };
            using (var cfile = new CFile(BuildStream(windows1252Bytes))) {
                Assert.Equal("utf-8", cfile.Encoding.WebName);
                Assert.Equal(0xFFFD, cfile.ReadChar());
                Assert.Equal(0xFFFD, cfile.ReadChar());
                Assert.Equal(0xFFFD, cfile.ReadChar());
                Assert.Equal(0xFFFD, cfile.ReadChar());
                Assert.Equal(0, cfile.ReadChar());
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestReadLineEncoded() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal("èaà", cfile.ReadLine());
                Assert.Equal("üî", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal("èaà", cfile.ReadLine());
                Assert.Equal("üî", cfile.ReadLine());
                Assert.Null(cfile.ReadLine());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestRead() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(195, cfile.Read());
                Assert.Equal(168, cfile.Read());
                Assert.Equal(97, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(160, cfile.Read());
                Assert.Equal(10, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(188, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(174, cfile.Read());
                Assert.Equal(-1, cfile.Read());
                Assert.Equal(-1, cfile.Read());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal(195, cfile.Read());
                Assert.Equal(168, cfile.Read());
                Assert.Equal(97, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(160, cfile.Read());
                Assert.Equal(10, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(188, cfile.Read());
                Assert.Equal(195, cfile.Read());
                Assert.Equal(174, cfile.Read());
                Assert.Equal(-1, cfile.Read());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadChar() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(232, cfile.ReadChar());
                Assert.Equal(97, cfile.ReadChar());
                Assert.Equal(224, cfile.ReadChar());
                Assert.Equal(10, cfile.ReadChar());
                Assert.Equal(252, cfile.ReadChar());
                Assert.Equal(238, cfile.ReadChar());
                Assert.Equal(0, cfile.ReadChar());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal('è', cfile.ReadChar());
                Assert.Equal('a', cfile.ReadChar());
                Assert.Equal('à', cfile.ReadChar());
                Assert.Equal('\n', cfile.ReadChar());
                Assert.Equal('ü', cfile.ReadChar());
                Assert.Equal('î', cfile.ReadChar());
                Assert.Equal(0, cfile.ReadChar());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 195 }), Encoding.UTF8)) {
                Assert.Equal(65533, cfile.ReadChar());
                Assert.Equal(0, cfile.ReadChar());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadChars() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(new char[] { 'è', 'a', 'à' }, cfile.ReadChars(3));
                Assert.Equal(new char[] { '\n', 'ü', 'î' }, cfile.ReadChars(3));
                Assert.Null(cfile.ReadChars(3));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal(new char[] { 'è', 'a', 'à' }, cfile.ReadChars(3));
                Assert.Equal(new char[] { '\n', 'ü', 'î' }, cfile.ReadChars(3));
                Assert.Null(cfile.ReadChars(3));
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadString() {
            String str = null;
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                str = "$$$";
                Assert.True(cfile.ReadString(ref str, 3));
                Assert.Equal("èaà", str);

                str = "$$$";
                Assert.True(cfile.ReadString(ref str, 3));
                Assert.Equal("\nüî", str);

                str = "$$$";
                Assert.False(cfile.ReadString(ref str, 3));
                Assert.Null(str);

                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                str = "$$$";
                Assert.True(cfile.ReadString(ref str, 3));
                Assert.Equal("èaà", str);

                str = "$$$";
                Assert.True(cfile.ReadString(ref str, 3));
                Assert.Equal("\nüî", str);

                str = "$$$";
                Assert.False(cfile.ReadString(ref str, 3));
                Assert.Null(str);

                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadByte() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(195, cfile.ReadByte());
                Assert.Equal(168, cfile.ReadByte());
                Assert.Equal(97, cfile.ReadByte());
                Assert.Equal(195, cfile.ReadByte());
                Assert.Equal(160, cfile.ReadByte());
                Assert.Equal(10, cfile.ReadByte());
                Assert.Equal(195, cfile.ReadByte());
                Assert.Equal(188, cfile.ReadByte());
                Assert.Equal(195, cfile.ReadByte());
                Assert.Equal(174, cfile.ReadByte());
                Assert.Equal(0, cfile.ReadByte());
                Assert.Equal(0, cfile.ReadByte());
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestReadBytes() {
            byte[] buff = new byte[3];
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 168, 97 }, buff);
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 160, 10}, buff);
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 188, 195 }, buff);
                Assert.Equal(1, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 174, 188, 195 }, buff);
                Assert.Equal(0, cfile.Read(buff, 0, 3));
                Assert.Equal(0, cfile.Read(buff, 0, 3));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 168, 97 }, buff);
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 160, 10 }, buff);
                Assert.Equal(3, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 195, 188, 195 }, buff);
                Assert.Equal(1, cfile.Read(buff, 0, 3));
                Assert.Equal(new byte[] { 174, 188, 195 }, buff);
                Assert.Equal(0, cfile.Read(buff, 0, 3));
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadSByte() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-88, cfile.ReadSByte());
                Assert.Equal(97, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-96, cfile.ReadSByte());
                Assert.Equal(10, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-68, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-82, cfile.ReadSByte());
                Assert.Equal(0, cfile.ReadSByte());
                Assert.Equal(0, cfile.ReadSByte());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-88, cfile.ReadSByte());
                Assert.Equal(97, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-96, cfile.ReadSByte());
                Assert.Equal(10, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-68, cfile.ReadSByte());
                Assert.Equal(-61, cfile.ReadSByte());
                Assert.Equal(-82, cfile.ReadSByte());
                Assert.Equal(0, cfile.ReadSByte());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0, 0x01, 0x10, 0x80, 0xF0, 0xFF }))) {
                Assert.Equal(0, cfile.ReadSByte());
                Assert.Equal(1, cfile.ReadSByte());
                Assert.Equal(16, cfile.ReadSByte());
                Assert.Equal(-128, cfile.ReadSByte());
                Assert.Equal(-16, cfile.ReadSByte());
                Assert.Equal(-1, cfile.ReadSByte());
                Assert.Equal(0, cfile.ReadSByte());
                Assert.Equal(0, cfile.ReadSByte());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadSBytes() {
            using (var cfile = new CFile(BuildStream("èaà\nüî"))) {
                Assert.Equal(new sbyte[] { -61, -88, 97 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -61, -96, 10 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -61, -68, -61 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -82 }, cfile.ReadSBytes(3));
                Assert.Null(cfile.ReadSBytes(3));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream("èaà\nüî"), Encoding.UTF8)) {
                Assert.Equal(new sbyte[] { -61, -88, 97 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -61, -96, 10 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -61, -68, -61 }, cfile.ReadSBytes(3));
                Assert.Equal(new sbyte[] { -82 }, cfile.ReadSBytes(3));
                Assert.Null(cfile.ReadSBytes(3));
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadInt32() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal((int)0x78563412, cfile.ReadInt32());
                Assert.Equal(0x98BADCFE, (uint)cfile.ReadInt32());
                Assert.Equal(0, cfile.ReadInt32());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA }))) {
                Assert.Equal((int)0x78563412, cfile.ReadInt32());
                Assert.Equal(0, cfile.ReadInt32());
                Assert.Equal(0, cfile.ReadInt32());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 97, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal('a', cfile.ReadChar());
                Assert.Equal((int)0x78563412, cfile.ReadInt32());
                Assert.Equal(0x98BADCFE, (uint)cfile.ReadInt32());
                Assert.Equal(0, cfile.ReadInt32());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadUInt32() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal((uint)0x78563412, cfile.ReadUInt32());
                Assert.Equal(0x98BADCFE, cfile.ReadUInt32());
                Assert.Equal((uint)0, cfile.ReadUInt32());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA }))) {
                Assert.Equal((uint)0x78563412, cfile.ReadUInt32());
                Assert.Equal((uint)0, cfile.ReadUInt32());
                Assert.Equal((uint)0, cfile.ReadUInt32());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 97, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal('a', cfile.ReadChar());
                Assert.Equal((uint)0x78563412, cfile.ReadUInt32());
                Assert.Equal(0x98BADCFE, cfile.ReadUInt32());
                Assert.Equal((uint)0, cfile.ReadUInt32());
                Assert.True(cfile.EOF);
            }

        }

        [Fact]
        public void TestReadDouble() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal("4.6919753605233776E+271", cfile.ReadDouble().ToString(CultureInfo.InvariantCulture));
                Assert.Equal(-1.50730608775746E-189, cfile.ReadDouble(), 15);
                Assert.Equal(0, cfile.ReadDouble());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA}))) {
                Assert.Equal("4.6919753605233776E+271", cfile.ReadDouble().ToString(CultureInfo.InvariantCulture));
                Assert.Equal(0, cfile.ReadDouble());
                Assert.Equal(0, cfile.ReadDouble());
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] {97,  0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal('a', cfile.ReadChar());
                Assert.Equal("4.6919753605233776E+271", cfile.ReadDouble().ToString(CultureInfo.InvariantCulture));
                Assert.Equal(-1.50730608775746E-189, cfile.ReadDouble(), 15);
                Assert.Equal(0, cfile.ReadInt32());
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestReadInt32s() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                var vals = cfile.ReadInt32s(4);
                Assert.Equal(2, vals.Length);
                Assert.Equal((int)0x78563412, vals[0]);
                Assert.Equal(0x98BADCFE, (uint)vals[1]);
                Assert.Null(cfile.ReadInt32s(4));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA }))) {
                var vals = cfile.ReadInt32s(4);
                Assert.Single(vals);
                Assert.Equal((int)0x78563412, vals[0]);
                Assert.Null(cfile.ReadInt32s(4));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 97, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal('a', cfile.ReadChar());
                var vals = cfile.ReadInt32s(4);
                Assert.Equal(2, vals.Length);
                Assert.Equal((int)0x78563412, vals[0]);
                Assert.Equal(0x98BADCFE, (uint)vals[1]);
                Assert.Null(cfile.ReadInt32s(4));
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestReadDoubles() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                var vals = cfile.ReadDoubles(4);
                Assert.Equal(2, vals.Length);
                Assert.Equal("4.6919753605233776E+271", vals[0].ToString(CultureInfo.InvariantCulture));
                Assert.Equal(-1.50730608775746E-189, vals[1], 15);
                Assert.Null(cfile.ReadDoubles(4));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA }))) {
                var vals = cfile.ReadDoubles(4);
                Assert.Single(vals);
                Assert.Equal("4.6919753605233776E+271", vals[0].ToString(CultureInfo.InvariantCulture));
                Assert.Null(cfile.ReadDoubles(4));
                Assert.True(cfile.EOF);
            }

            using (var cfile = new CFile(BuildStream(new byte[] { 97, 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal('a', cfile.ReadChar());
                var vals = cfile.ReadDoubles(4);
                Assert.Equal(2, vals.Length);
                Assert.Equal("4.6919753605233776E+271", vals[0].ToString(CultureInfo.InvariantCulture));
                Assert.Equal(-1.50730608775746E-189, vals[1], 15);
                Assert.Null(cfile.ReadDoubles(4));
                Assert.True(cfile.EOF);
            }
        }

        [Fact]
        public void TestSeek() {
            using (var cfile = new CFile(BuildStream(new byte[] { 0x12, 0x34, 0x56, 0x78, 0xFE, 0xDC, 0xBA, 0x98 }))) {
                Assert.Equal(0, cfile.Seek(4, SeekOrigin.Current));
                Assert.Equal(0x98BADCFE, (uint)cfile.ReadInt32());
                Assert.Equal(0, cfile.Seek(0, SeekOrigin.Begin));
                Assert.Equal((int)0x78563412, cfile.ReadInt32());
            }

            using (var cfile = new CFile(null)) {
                Assert.Equal(-1, cfile.Seek(4, SeekOrigin.Current));
            }

        }

    }
}
