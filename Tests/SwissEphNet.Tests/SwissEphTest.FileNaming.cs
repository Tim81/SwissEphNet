using System;
using System.Collections.Generic;
using System.Linq;
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
        public void TestAsteroidFileNameUsesForwardSlashNotBackslash()
        {
            // DIR_GLUE (SwissEphNet/SwissEph.sweodef.h.cs) is '/'.
            // swi_gen_filename (CPort/SwephLib.cs) embeds it into numbered
            // asteroid file names as "ast<thousands><DIR_GLUE>se<number>.se1",
            // e.g. "ast4/se04179.se1". A backslash is not a path separator on
            // Linux/macOS/Android/iOS/WASM, so an OnLoadFile handler that does
            // Path.Combine or a resource-name lookup on that generated name
            // could never find the file except on Windows with the old '\\'
            // value.
            //
            // This required (and got) a CPort edit: CPort/Sweph.cs:2634
            // (swi_fopen's ephepath+filename join) had been hard-coded to
            // '\\' instead of using DIR_GLUE -- a mis-transliteration, not a
            // deliberate platform choice, since the parallel site in
            // swe_set_ephe_path (Sweph.cs:1514-1515) already used DIR_GLUE
            // correctly for the identical C pattern. See docs/known-issues.md
            // for the full analysis, including why the fix regressed
            // Issue18Test.LoadAsteroidData until 2634 itself was corrected
            // (fixing DIR_GLUE alone, without fixing 2634, breaks the
            // "correct file name?" validation for every successfully-loaded
            // file).
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);

                // 4179 = Toutatis, a real numbered asteroid outside both the
                // small set of named main-belt asteroids swi_gen_filename
                // special-cases and the >99999 range that gets a different
                // ("ast<thousands><glue>s<number>.se1", no "se") short-file
                // naming convention.
                var fname = GenFileName(swe, tjd, SwissEph.SE_AST_OFFSET + 4179);

                Assert.Equal("ast4/se04179.se1", fname);
                Assert.DoesNotContain("\\", fname, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TestPlanetaryMoonFileNameHasNoDirectoryGlue()
        {
            // SEI_MOON's generated file name (e.g. "semo_18.se1") never
            // embeds a subdirectory, so it is unaffected by DIR_GLUE either
            // way -- unlike the asteroid case above, this one was never
            // blocked on a CPort fix.
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);

                var fname = GenFileName(swe, tjd, 1 /* SEI_MOON */);

                Assert.Matches(new Regex(@"^semo[_m]\d\d\.se1$"), fname);
                Assert.DoesNotContain("\\", fname, StringComparison.Ordinal);
                Assert.DoesNotContain("/", fname, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TestAsteroidFileNameReachesOnLoadFileWithForwardSlash()
        {
            // End-to-end version of TestAsteroidFileNameUsesForwardSlashNotBackslash:
            // confirms the forward slash actually reaches an
            // IEphemerisFileProvider consumer through swe_calc/swi_fopen, not
            // just swi_gen_filename in isolation.
            using (var swe = new SwissEph())
            {
                var capturedFileNames = new List<string>();
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    capturedFileNames.Add(path);
                    return null; // force "not found": we only need the requested name
                });

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6];
                string serr = null;

                swe.swe_calc(tjd, SwissEph.SE_AST_OFFSET + 4179, SwissEph.SEFLG_SWIEPH, xx, ref serr);

                var asteroidFileName = capturedFileNames.FirstOrDefault(f => f.Contains("ast4", StringComparison.Ordinal));
                Assert.NotNull(asteroidFileName);
                Assert.Contains("ast4/se04179.se1", asteroidFileName, StringComparison.Ordinal);
                Assert.DoesNotContain("ast4\\se04179", asteroidFileName, StringComparison.Ordinal);
            }
        }

    }
}
