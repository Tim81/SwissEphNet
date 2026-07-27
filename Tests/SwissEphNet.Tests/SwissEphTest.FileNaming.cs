using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SwissEphNet.Tests
{
    partial class SwissEphTest
    {
        // swi_gen_filename lives on the internal SwephLib class (no
        // InternalsVisibleTo is declared for the Tests assembly), so it is
        // called through reflection here.
        static string GenFileName(SwissEph swe, double tjd, int ipli)
        {
            var swephLibProp = typeof(SwissEph).GetProperty("SwephLib", BindingFlags.NonPublic | BindingFlags.Instance);
            var swephLib = swephLibProp.GetValue(swe);
            var method = swephLib.GetType().GetMethod("swi_gen_filename");
            object[] callArgs = { tjd, ipli, null };
            method.Invoke(swephLib, callArgs);
            return (string)callArgs[2];
        }

        [Fact]
        public void TestAsteroidFileNameStillUsesBackslashNotForwardSlash()
        {
            // DIR_GLUE (SwissEphNet/SwissEph.sweodef.h.cs) is NOT '/' despite
            // that being the originally proposed PR1 fix for this exact
            // cross-platform problem: swi_gen_filename (CPort/SwephLib.cs)
            // embeds DIR_GLUE into numbered asteroid file names as
            // "ast<thousands><DIR_GLUE>se<number>.se1", e.g.
            // "ast4\se04179.se1". A backslash is not a path separator on
            // Linux/macOS/Android/iOS/WASM, so an OnLoadFile handler that
            // does Path.Combine or a resource-name lookup on that generated
            // name can never find the file except on Windows -- that part of
            // the bug is real and confirmed.
            //
            // But changing DIR_GLUE to '/' is not a safe fix in isolation:
            // CPort/Sweph.cs's own "correct file name?" validation
            // (swi_fixstar-adjacent code, ~line 4922) strips a directory
            // prefix off the *loaded* file's recorded name by searching for
            // DIR_GLUE, while that prefix was actually joined with a
            // hard-coded '\\' elsewhere in CPort (Sweph.cs ~line 2634,
            // swi_fopen's ephepath+filename join) -- not with DIR_GLUE. The
            // two only agree because DIR_GLUE has always equaled '\\'.
            // Setting DIR_GLUE to '/' breaks that coincidental agreement and
            // makes the validation reject every successfully-loaded
            // ephemeris file, on every platform, confirmed by
            // Issue18Test.LoadAsteroidData regressing on Windows the moment
            // DIR_GLUE was changed (a real numbered-asteroid file, loaded
            // successfully via OnLoadFile, gets rejected with "Ephemeris
            // file name ... wrong; rename ..."). See docs/known-issues.md
            // for the full write-up. A real fix needs a CPort edit (either
            // the hard-coded join or the DIR_GLUE-based stripping), which is
            // out of scope for a CPort-byte-identical PR.
            //
            // This test pins the CURRENT (still-backslash, still
            // platform-limited) behavior precisely, the same way
            // PR0's TestDefaultEncodingDoesNotRoundTripWindows1252Bytes
            // pinned a known, not-yet-fixed bug: so that if DIR_GLUE is ever
            // changed again, this test fails visibly and has to be looked
            // at, rather than the regression risk silently reappearing.
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);

                // 4179 = Toutatis, a real numbered asteroid outside both the
                // small set of named main-belt asteroids swi_gen_filename
                // special-cases and the >99999 range that gets a different
                // ("ast<thousands><glue>s<number>.se1", no "se") short-file
                // naming convention.
                var fname = GenFileName(swe, tjd, SwissEph.SE_AST_OFFSET + 4179);

                Assert.Equal("ast4\\se04179.se1", fname);
            }
        }

        [Fact]
        public void TestPlanetaryMoonFileNameHasNoDirectoryGlue()
        {
            // SEI_MOON's generated file name (e.g. "semo_18.se1") never
            // embeds a subdirectory, so it is unaffected by DIR_GLUE either
            // way -- unlike the asteroid case above, this one is not blocked
            // on a CPort fix.
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);

                var fname = GenFileName(swe, tjd, 1 /* SEI_MOON */);

                Assert.Matches(new Regex(@"^semo[_m]\d\d\.se1$"), fname);
                Assert.DoesNotContain("\\", fname);
                Assert.DoesNotContain("/", fname);
            }
        }

    }
}
