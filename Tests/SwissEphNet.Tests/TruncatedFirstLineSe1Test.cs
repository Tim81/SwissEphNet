using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// read_const (Sweph.cs) reads three header lines before it ever touches file contents: the
    /// version line, the file-name line, and the copyright line. All three sit behind a
    /// null/empty check in the real C (sweph.c:4535-4536, :4553-4554, :4586-4587 -- each guards
    /// its own fgets() return before dereferencing it) and, before this fix, in the port too
    /// (Sweph.cs:5277-5279 and :5320-5325 both null-check fp.ReadLine() first). The file-name
    /// line at Sweph.cs:5297 was the one exception: it called .Trim() on fp.ReadLine()'s result
    /// unconditionally.
    ///
    /// A .se1 file truncated to exactly its first line puts fp.ReadLine() at true EOF on the
    /// second call (the file-name line), where it returns null rather than a partial string --
    /// so the unguarded .Trim() threw NullReferenceException there, escaping swe_calc,
    /// swe_calc_ut and swe_set_ephe_path (which calls swe_calc internally to prime the file)
    /// instead of the documented "-1 with serr, 'is damaged'" contract every other truncation
    /// length in this file already gets.
    ///
    /// Measured across seven truncation lengths against external/swisseph/ephe/sepl_18.se1 (44
    /// bytes is the shortest that still contains a complete first line with its trailing \r\n):
    /// truncating to 12 or 13 bytes -- both inside or exactly at the end of that first line --
    /// hit this NullReferenceException before the fix; 24, 110 and 200 bytes already returned
    /// ERR/"damaged" because they still leave the second ReadLine something non-null (if
    /// partial) to read. This test fixes the fixture at 13 bytes: the shortest length that
    /// reliably reproduces true EOF on the *second* ReadLine on every .NET line-ending
    /// interpretation (a file this short has no meaningful third line either way, but 13 bytes
    /// is exactly "SWISSEPH  3\r\n", so the first ReadLine always succeeds and the second always
    /// hits EOF).
    /// </summary>
    public class TruncatedFirstLineSe1Test
    {
        [Fact]
        public void Test_Se1TruncatedToFirstLine_IsRejectedAsDamagedInsteadOfThrowing()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("sepl_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        // "SWISSEPH  3\r\n" -- the complete first (version) line and nothing else.
                        byte[] truncated = System.Text.Encoding.ASCII.GetBytes("SWISSEPH  3\r\n");
                        return new MemoryStream(truncated, writable: false);
                    }
                    return null;
                });

                double[] xx = new double[6];
                string serr = null;

                int res = swe.swe_calc(2451544.5, SwissEph.SE_SUN, SwissEph.SEFLG_SWIEPH, xx, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.NotNull(serr);
                Assert.Contains("damaged", serr, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Test_Se1TruncatedToFirstLine_swe_calc_ut_IsRejectedAsDamagedInsteadOfThrowing()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("sepl_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] truncated = System.Text.Encoding.ASCII.GetBytes("SWISSEPH  3\r\n");
                        return new MemoryStream(truncated, writable: false);
                    }
                    return null;
                });

                double[] xx = new double[6];
                string serr = null;

                int res = swe.swe_calc_ut(2451544.5, SwissEph.SE_SUN, SwissEph.SEFLG_SWIEPH, xx, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.NotNull(serr);
                Assert.Contains("damaged", serr, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Test_Se1TruncatedToFirstLine_swe_set_ephe_path_DoesNotThrow()
        {
            // swe_set_ephe_path (Sweph.cs:1603) calls swe_calc internally to prime the newly
            // configured path -- the same crash reached swe_set_ephe_path through that internal
            // call, not just direct swe_calc/swe_calc_ut callers.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (fn.Equals("sepl_18.se1", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] truncated = System.Text.Encoding.ASCII.GetBytes("SWISSEPH  3\r\n");
                        return new MemoryStream(truncated, writable: false);
                    }
                    return null;
                });

                var ex = Record.Exception(() => swe.swe_set_ephe_path("[ephe]"));

                Assert.Null(ex);
            }
        }
    }
}
