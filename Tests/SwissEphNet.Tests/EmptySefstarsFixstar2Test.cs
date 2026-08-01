using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_fixstar2 reaches load_all_fixed_stars (Sweph.cs, sweph.c:6459) unconditionally on
    /// every call. When sefstars.txt exists but contributes no records -- e.g. it is empty, or a
    /// partially-downloaded ephemeris set left a zero-byte file in its place -- load_all_fixed_stars
    /// finishes with nrecs == 0 and still calls C.qsort(swed.fixed_stars.GetPointer(), 0, ...)
    /// unconditionally (Sweph.cs:7515).
    ///
    /// C.qsort used to call array.ToArray().Take(n) before checking n; CPointer&lt;T&gt;.ToArray()
    /// returns null when the pointer has no backing array (Tools/CPointer.cs), which is exactly
    /// swed.fixed_stars' state here (nothing was ever allocated because save_star_in_struct never
    /// ran). Take(n) on that null threw ArgumentNullException, escaping swe_fixstar2 as an
    /// unhandled exception instead of the documented "-1 with serr" contract every other
    /// not-found path in this function honours (see Issue41Test's TestDataFixstar2, e.g. the
    /// "10000" and "aldeb" cases).
    ///
    /// The real C qsort(3) does nothing when nmemb is 0, without ever dereferencing base, so the
    /// equivalent C call is harmless. Fixed by guarding C.qsort the same way C.bsearch already
    /// was (see that function's own comment, Tools/C.cs).
    /// </summary>
    public class EmptySefstarsFixstar2Test
    {
        [Fact]
        public void Test_swe_fixstar2_WithEmptySefstars_ReturnsErrorInsteadOfThrowing()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(Array.Empty<byte>(), writable: false);
                    }
                    return null;
                });

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6];
                string serr = null;
                string star = "aldebaran";

                int res = swe.swe_fixstar2(ref star, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.Equal("error, swe_fixstar(): could not find star name aldebaran", serr);
            }
        }

        [Fact]
        public void Test_swe_fixstar2_mag_WithEmptySefstars_ReturnsErrorInsteadOfThrowing()
        {
            // Same load_all_fixed_stars/qsort path, reached through the sibling entry point
            // (sweph.c:7003, swe_fixstar2_mag) rather than swe_fixstar2 itself.
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(Array.Empty<byte>(), writable: false);
                    }
                    return null;
                });

                double mag = 0;
                string serr = null;
                string star = "aldebaran";

                int res = swe.swe_fixstar2_mag(ref star, ref mag, ref serr);

                Assert.Equal(SwissEph.ERR, res);
                Assert.Equal("error, swe_fixstar(): could not find star name aldebaran", serr);
            }
        }
    }
}
