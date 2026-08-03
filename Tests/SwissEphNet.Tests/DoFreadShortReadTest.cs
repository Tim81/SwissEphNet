using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Sweph.cs's do_fread (the low-level reader every planetary/asteroid segment in an ephemeris
    /// file goes through) used to test <c>fp.Read(trg, 0, totsize) == 0</c>, which rejects only a
    /// read that returned zero bytes. <c>external/swisseph/sweph.c</c>:4915 and :4926 use
    /// <c>fread(targ, totsize, 1, fp) == 0</c> -- fread's third argument is an item count of 1
    /// (item size totsize), so fread returns 0 items on ANY short read, not only a zero-byte one.
    /// <c>CFile.Read</c> loops and returns the actual byte count read, so a partial read (fewer
    /// than totsize bytes, but more than zero) returned non-zero and was silently accepted as
    /// success by the old check -- the rest of the target buffer kept whatever it already held
    /// (zero, from the fresh allocation in the size != corrsize branch, or stale content in the
    /// size == corrsize branch), and the caller went on to use that partially-filled data as if it
    /// were a real value read from the file.
    ///
    /// The obvious way to test this -- truncate a real .se1 file -- cannot reach do_fread's own
    /// check at all: read_const's file-length guard (Sweph.cs, `if (lng != flen) { smsg = "h"; ...
    /// }`, right after the first do_fread call) compares the file's declared length against the
    /// actual length seen by seeking to the end, and fires on any plain truncation before a second
    /// do_fread call ever runs (see <see cref="CorruptFileTruncationTest"/>, which exercises
    /// exactly that guard). Reaching do_fread's own check with a genuine short read therefore needs
    /// a file whose declared length still matches its truncated length -- which means patching the
    /// 4-byte length field the guard reads, which sits inside the region <c>swi_crc32</c> checksums
    /// a few bytes later in the same header, which in turn means recomputing that checksum. Both
    /// are done here, on a truncated copy of a real fixture, rather than on a hand-built file: the
    /// goal is a short read that still parses as a structurally valid ephemeris file, not one that
    /// merely happens to be short.
    ///
    /// A truncation landing on some earlier do_fread call is not enough either: <c>CFile.Read</c>
    /// sets its own EOF flag on any read where fewer bytes came back than were asked for, and
    /// short-circuits every later call to zero once that flag is set (`if (buff == null || EOF)
    /// return 0`). A short-but-non-zero read followed by any further do_fread call, anywhere,
    /// therefore degrades to a later *zero*-byte read -- which the pre-existing, unfixed check
    /// already rejected -- and the externally observable outcome (an "is damaged" error) would be
    /// identical whether or not this fix is applied. This is why the truncation point below is not
    /// merely "inside some do_fread call": it is inside the *very last* do_fread call the whole
    /// computation makes for this specific query (the last, non-zero-size packed-coefficient block
    /// of the third coordinate, in <c>get_new_segment</c>) -- nothing else reads from the file
    /// afterward, so there is no later call available to (re-)fail on a genuine zero-byte read and
    /// mask the difference.
    ///
    /// Confirmed to discriminate: with the fix in <c>do_fread</c> reverted (`== 0` restored), this
    /// exact fixture makes <c>swe_pheno</c> return success with a wrong <c>attr[3]</c> (computed
    /// from Chebyshev coefficients whose tail is silently zero-filled) instead of an error -- the
    /// "indistinguishable from success" failure mode <see cref="CorruptFileTruncationTest"/>'s own
    /// remarks describe for the file-length guard, reproduced here for do_fread's own check.
    ///
    /// Fixture derivation (all offsets and the truncation point below were located empirically
    /// against the real, checked-in <c>seas_18.se1</c> bytes and are pinned to that exact file --
    /// see this class's own construction code, not a separate generator script, since the whole
    /// point is that this is one specific, reproducible byte layout, not a general-purpose tool):
    ///   - byte offset 120: the 4-byte file-length field <c>read_const</c>'s guard compares against
    ///     the actual (truncated) length. Patched to the new, truncated length.
    ///   - byte offset 158: the 4-byte CRC-32 of bytes [0, 158) (<c>swi_crc32</c>, Rob Warnock's
    ///     public-domain big-endian-bit-order CRC-32, reproduced locally below since
    ///     <c>SwephLib</c> is internal to <c>SwissEphNet</c> and not visible to this test project).
    ///     Patched to match bytes [0, 158) after the length-field patch above.
    ///   - byte offset 742: Chiron's (SEI index 0, <c>SE_CHIRON</c>) 3-byte segment-index pointer
    ///     table starts here (<c>pdp.lndx0</c>); segment 0's pointer (bytes 742-745, unpatched)
    ///     resolves to file offset 5377, where that segment's packed Chebyshev coefficients for the
    ///     third coordinate begin. The third coordinate's own last non-zero-size block is 11 bytes,
    ///     at [5465, 5476).
    ///   - Truncated to 5470 bytes total: the third coordinate's last block gets exactly 5 of its
    ///     11 requested bytes -- a genuine short (non-zero) read, and (per the two paragraphs
    ///     above) the very last file read <c>swe_pheno(SE_CHIRON, ...)</c> for this date performs.
    /// </summary>
    public class DoFreadShortReadTest
    {
        [Fact]
        public void Test_ShortReadOnLastSegmentBlock_IsRejectedAsDamaged()
        {
            byte[] crafted = BuildCraftedFixture();

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("seas_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(crafted, writable: false);
                    }
                    return null;
                });

                // Chiron's tfstart in this fixture is JD 2378496.5 (TT); +0.5 sits inside segment 0
                // (dseg = 1000 days), the segment the truncation above targets.
                double tjd = 2378496.5 + 0.5;
                double[] attr = new double[20];
                string serr = null;

                int res = swe.swe_pheno(tjd, SwissEph.SE_CHIRON, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.NotNull(serr);
                Assert.Contains("damaged", serr, StringComparison.Ordinal);
                // read_const's own "(0%s)" smsg codes (see CorruptFileTruncationTest.cs's "(0h)")
                // are a completely different, unrelated format string from do_fread's -- "Ephemeris
                // file %s is damaged (2)." / "(4).", Sweph.cs:5741/5754. This truncation lands
                // inside the "quarter byte packing" do_fread call (Sweph.cs's get_new_segment,
                // i == 5 branch): its size argument is 1 and its corrsize argument is 4, so
                // size != corrsize always sends it through do_fread's "(4)" branch, never "(2)".
                // "Contains(\"damaged\")" alone cannot tell this do_fread-level rejection apart from
                // any of read_const's own thirteen distinct "(0<letter>)" codes -- pinning the exact
                // sub-code is what actually proves this specific do_fread check fired.
                Assert.Contains("is damaged (4).", serr, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Test_UntruncatedFixture_StillSucceeds()
        {
            // Sanity companion: proves the crafted fixture is otherwise well-formed (correct
            // length field, correct CRC, a real Chiron segment at the targeted date) and that the
            // failure above comes specifically from the truncation, not from some other mistake in
            // BuildCraftedFixture's header patching. Goes through BuildCraftedFixture itself
            // (passing the untruncated length, so the length-field patch and CRC recompute are
            // exercised but land back on the original byte values) rather than opening the
            // pristine resource directly -- the latter would pass even if BuildCraftedFixture's
            // header patching were wrong, since it would never run.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("seas_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(BuildCraftedFixture(truncatedLength: null), writable: false);
                    }
                    return null;
                });

                double tjd = 2378496.5 + 0.5;
                double[] attr = new double[20];
                string serr = null;

                int res = swe.swe_pheno(tjd, SwissEph.SE_CHIRON, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                Assert.NotEqual(SwissEph.ERR, res);
            }
        }

        /// <summary>
        /// Patches seas_18.se1's declared length and header CRC to match
        /// <paramref name="truncatedLength"/> (defaulting to the fixed short-read truncation point
        /// this class targets), then truncates to that length. Passing the resource's own full
        /// length (as <see cref="Test_UntruncatedFixture_StillSucceeds"/> does) still runs the
        /// patch and CRC recompute, but reproduces the original bytes exactly, since nothing in the
        /// patched header region actually changed.
        /// </summary>
        private static byte[] BuildCraftedFixture(int? truncatedLength = 5470)
        {
            byte[] full;
            using (var resource = ResourceFileHelpers.OpenResourceFile("seas_18.se1"))
            using (var ms = new MemoryStream())
            {
                resource.CopyTo(ms);
                full = ms.ToArray();
            }

            int length = truncatedLength ?? full.Length;
            const int lengthFieldOffset = 120;
            const int crcFieldOffset = 158;
            const int crcRegionLength = 158;

            byte[] data = new byte[full.Length];
            Array.Copy(full, data, full.Length);

            byte[] lengthBytes = BitConverter.GetBytes(length);
            Array.Copy(lengthBytes, 0, data, lengthFieldOffset, 4);

            uint crc = SwiCrc32(data, crcRegionLength);
            byte[] crcBytes = BitConverter.GetBytes(crc);
            Array.Copy(crcBytes, 0, data, crcFieldOffset, 4);

            byte[] truncated = new byte[length];
            Array.Copy(data, truncated, length);
            return truncated;
        }

        // Reproduced from SwephLib.cs's init_crc32/swi_crc32 (Rob Warnock's public-domain CRC-32,
        // BigEndian/BigEndian byte/bit order): SwephLib is internal to SwissEphNet, so this test
        // project cannot call the port's own implementation directly. Verified to reproduce
        // seas_18.se1's own stored CRC-32 (bytes [158, 162) over bytes [0, 158)) before being used
        // here to compute a new one.
        private static uint SwiCrc32(byte[] buf, int len)
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
