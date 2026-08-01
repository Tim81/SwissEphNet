using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Sweph.cs's read_const, right after the byte-order test integer, compares the file's own
    /// recorded length (read from the header) against the actual length seen by seeking to the
    /// end (`if (lng != flen) { smsg = "h"; goto file_damage; }`). That single comparison is the
    /// only thing standing between a truncated .se1 file and a silently wrong answer: everything
    /// that follows in read_const trusts the header at face value and never re-derives the
    /// file's true extent, and do_fread (Sweph.cs, the low-level reader used for every planetary
    /// segment) only rejects a read that returns *zero* bytes, not one that returns fewer bytes
    /// than requested -- so a segment whose Chebyshev coefficients happen to still fit inside a
    /// truncated file reads back looking completely legitimate.
    ///
    /// A file truncated so severely that the byte-order test integer or an early header field
    /// itself falls outside the file is already caught elsewhere (further up read_const, before
    /// this check ever runs) -- that is the "(0h)"/"(0n)" territory documented at Sweph.cs's
    /// neighbouring smsg branches. This test targets the gap specifically: a file trimmed by
    /// about 1% off the end, small enough that every byte this particular lookup actually reads
    /// still lies inside the truncated copy. Confirmed directly (and reverted before commit): with
    /// the "lng != flen" check patched out, this exact fixture returns swe_pheno() != ERR, an
    /// empty serr and the same attr[3] as the untruncated file -- indistinguishable from success.
    /// With the check in place, it fails as "Ephemeris file ... is damaged (0h)." This is the
    /// only test in the suite that exercises a truncated-but-still-parseable file; every other
    /// corrupt-file test in this project either truncates severely enough to hit an earlier
    /// guard, or patches the length field to match (a different failure mode, caught elsewhere).
    ///
    /// Fixture: Tests/SwissEphNet.Tests/files/seas_18.se1 (the same fixture and date
    /// PlaDiamCoverageTest.cs uses), truncated at test time rather than checked in truncated --
    /// keeping one canonical copy of the real bytes avoids a second binary fixture that could
    /// drift out of sync with the first.
    /// </summary>
    public class CorruptFileTruncationTest
    {
        [Fact]
        public void Test_OnePercentTruncatedSeas18_IsRejectedAsDamaged()
        {
            byte[] full;
            using (var resource = ResourceFileHelpers.OpenResourceFile("seas_18.se1"))
            using (var ms = new MemoryStream())
            {
                resource.CopyTo(ms);
                full = ms.ToArray();
            }

            // Trim ~1% off the end. Confirmed (see class remarks) that the Ceres/16-Aug-1974
            // Chebyshev segment this test reads lies entirely within what remains -- this is
            // deliberately NOT a truncation severe enough to trip an earlier structural guard.
            int truncatedLength = full.Length - (full.Length / 100);
            Assert.True(truncatedLength < full.Length && truncatedLength > full.Length * 9 / 10);
            byte[] truncated = new byte[truncatedLength];
            Array.Copy(full, truncated, truncatedLength);

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("seas_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(truncated, writable: false);
                    }
                    return null;
                });

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] attr = new double[20];
                string serr = null;

                int res = swe.swe_pheno(tjd, SwissEph.SE_CERES, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.NotNull(serr);
                Assert.Contains("damaged", serr, StringComparison.Ordinal);
                Assert.Contains("(0h)", serr, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Test_UntruncatedSeas18_StillSucceeds()
        {
            // Sanity companion to the test above: proves the truncation itself, not something
            // else about the fixture or the FileProvider plumbing, is what triggers the "(0h)"
            // rejection. Same fixture, same date, same body, full unmodified bytes.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("seas_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResourceFileHelpers.OpenResourceFile("seas_18.se1");
                    }
                    return null;
                });

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] attr = new double[20];
                string serr = null;

                int res = swe.swe_pheno(tjd, SwissEph.SE_CERES, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                Assert.NotEqual(SwissEph.ERR, res);
                Assert.Equal(0.00017743935901133728, attr[3], 11);
            }
        }
    }
}
