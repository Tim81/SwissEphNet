using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Regression tests for five transliteration-fidelity defects found by an
    /// audit comparing every string operation and array allocation in
    /// SwissEphNet/CPort against the C it was ported from (Defects 1, 2, 3,
    /// 3b and 4 below; a sixth test, TestHousesArmc_SunshineSystem, covers a
    /// separate Tier 2 culture-dispatch bug found the same way, not counted
    /// among these five). Each defect is documented at its fix site with the
    /// C file/line it diverged from; see also docs/known-issues.md.
    /// </summary>
    public class TransliterationFidelityTest
    {
        private static void HookStarFile(SwissEph swe)
        {
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                string fn = ResourceFileHelpers.GetPortableFileName(path);
                if (File.Exists(path))
                {
                    return new FileStream(path, FileMode.Open, FileAccess.Read);
                }
                return ResourceFileHelpers.OpenResourceFile(fn);
            });
        }

        // --- Defect 2: Sweph.cs fixstar_format_search_name off-by-one (sweph.c:5996-5997) ---
        //
        // fixstar_format_search_name lowercased sstar.Substring(0, p - 1) instead
        // of sstar.Substring(0, p), silently dropping the character immediately
        // before the comma when the search string is already in "Name,Bayer"
        // form. swe_fixstar rewrites its ref string parameter to that form on
        // return, so an ordinary call-again-with-the-same-variable loop feeds
        // the comma form straight back in. The shortened search key then only
        // needs to match a PREFIX of a star name, so an earlier, unrelated star
        // sharing that prefix silently wins with rc=OK instead of the star that
        // was actually asked for -- confirmed empirically for all four rows
        // below (e.g. "Caph,beCas" -> "Capella,alAur").
        public static IEnumerable<object[]> RoundTripStarNames()
        {
            yield return new object[] { "Caph,beCas", "Caph" };
            yield return new object[] { "Sadr,gaCyg", "Sadr" };
            yield return new object[] { "Altais,deDra", "Altais" };
            yield return new object[] { "Menkar,alCet", "Menkar" };
        }

        [Theory]
        [MemberData(nameof(RoundTripStarNames))]
        public void TestFixstar_RoundTrippedCommaForm_ReturnsStarThatWasAsked(string search, string expectedNamePrefix)
        {
            using (var swe = new SwissEph())
            {
                HookStarFile(swe);
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6];
                string star = search;
                string serr = null;

                int rc = swe.swe_fixstar(ref star, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.True(rc != SwissEph.ERR, $"expected a match, got rc={rc} serr={serr}");
                Assert.StartsWith(expectedNamePrefix, star, StringComparison.Ordinal);
            }
        }

        // --- Defect 1: Sweph.cs swi_fixstar_load_record Trim(' ') vs strip-all-spaces (sweph.c:7386-7387) ---
        //
        // The commented C block directly above the fix removes ALL internal
        // spaces from the candidate star name read from the file
        // (`while ((sp = strchr(fstar, ' ')) != NULL) swi_strcpy(sp, sp+1);`).
        // Trim(' ') only stripped the ends, leaving multi-word names with their
        // internal spaces intact while the *search* key (a few lines above, in
        // fixstar_format_search_name) has all spaces removed -- so the two
        // sides could never match for any multi-word name.
        [Fact]
        public void TestFixstar_MultiWordName_GalacticCenter_IsFound()
        {
            using (var swe = new SwissEph())
            {
                HookStarFile(swe);
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6];
                string star = "Galactic Center";
                string serr = null;

                int rc = swe.swe_fixstar(ref star, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.True(rc != SwissEph.ERR, $"expected 'Galactic Center' to be found, got rc={rc} serr={serr}");
                Assert.StartsWith("Galactic Center", star, StringComparison.Ordinal);
            }
        }

        // --- Defect 3: SweHel.cs tolower_string_star does not mutate its ref parameter (swehel.c:1443-1449) ---
        //
        // The C function mutates the caller's buffer in place. The ported
        // version computed a lower-cased value but never assigned it back to
        // the `ref string str` parameter, so callers passing a capitalized
        // object name (e.g. swe_get_planet_name(SE_MOON) => "Moon") never
        // actually got a lower-cased string. swe_vis_limit_mag relies on
        // ObjectName.StartsWith("moon") (SweHel.cs) to special-case the Moon
        // (no separate moonlight contribution, since the object being
        // evaluated for visibility IS the Moon): with the mutation missing,
        // "Moon".StartsWith("moon") is false, so it falls through to computing
        // the Moon's own real topocentric position as if it were a distinct
        // "moon interference" object -- silently wrong, not merely inert.
        //
        // Reproduced empirically: with the pre-fix code (Substring(0, p - 1),
        // and no assignment to str), the below case returns dret[5]==AltO and
        // dret[6"] matching the Moon's real azimuth (~76.4 degrees) instead of
        // the sentinel -90/0 the "moon" branch is supposed to produce. Date,
        // time and location were chosen (empirically, by probing) so the Moon
        // is above the local horizon, since swe_vis_limit_mag returns early
        // with rc=-2 for an object below the horizon, before ever reaching the
        // moon-branch check.
        [Fact]
        public void TestVisLimitMag_CapitalizedMoon_TakesMoonBranch()
        {
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(2000, 6, 1, 4, SwissEph.SE_GREG_CAL);
                double[] dgeo = { 5.333889, 47.853333, 468 }; // lon, lat, alt (m)
                double[] datm = new double[4];
                double[] dobs = new double[6];
                double[] dret = new double[8];
                string serr = null;

                int rc = swe.swe_vis_limit_mag(tjd, dgeo, datm, dobs, "Moon", SwissEph.SEFLG_MOSEPH, dret, ref serr);

                Assert.True(rc != -2, "test epoch/location must keep the Moon above the horizon");
                Assert.True(rc != SwissEph.ERR, $"unexpected error: {serr}");
                Assert.Equal(-90, dret[5], 9);  // AltM: moon branch sentinel, not the Moon's real altitude
                Assert.Equal(0, dret[6], 9);    // AziM: moon branch sentinel, not the Moon's real azimuth
            }
        }

        // --- Defect 3b: same function, missing p > 0 guard (swehel.c:1443-1449) ---
        //
        // Without the guard, a Bayer-designation-shaped string with the comma
        // in the first position (p == 0) hit Substring(0, p - 1) ==
        // Substring(0, -1), throwing ArgumentOutOfRangeException instead of
        // leaving the string untouched (the correct behavior: C's loop
        // condition `*sp != ','` is false immediately at position 0, so no
        // characters get lower-cased).
        //
        // Exercised directly via reflection rather than through
        // swe_vis_limit_mag's public ObjectName parameter: DeterObject
        // (SweHel.cs) falls through to a bare int.Parse for any name it does
        // not recognize as a planet, which throws FormatException for any
        // non-numeric name (fixed star or not, comma-prefixed or not) --  a
        // separate, pre-existing transliteration issue (C's atoi silently
        // returns 0 for non-numeric input) that is not one of the four
        // defects this PR fixes, and going through it would make this test
        // depend on a second, unrelated bug.
        [Fact]
        public void TestTolowerStringStar_CommaFirstCharacter_DoesNotThrow()
        {
            var method = typeof(SwissEph).Assembly
                .GetType("SwissEphNet.CPort.SweHel")
                .GetMethod("tolower_string_star", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            object[] args = { ",alTau" };
            var ex = Record.Exception(() => method.Invoke(null, args));

            Assert.Null(ex);
            // C's loop condition (*sp != ',') is false at position 0, so no
            // characters are lower-cased: the string passes through unchanged.
            Assert.Equal(",alTau", (string)args[0]);
        }

        // --- Defect 4: SwephLib.cs swe_set_astro_models Substring(0, 20) throw + "s + 2" string concat (swephlib.c:4052, 4058) ---
        //
        // strncpy(s, samod, 20) in C copies up to 20 bytes and null-pads if
        // samod is shorter; Substring(0, 20) instead threw whenever samod was
        // under 20 characters (including "" and null, both of which the C
        // explicitly handles via `*samod == '\0'`). Separately, "s + 2" is
        // pointer arithmetic in C (skip 2 bytes) but string concatenation in
        // C# ("SE2.05.01" + 2 -> "SE2.05.012"), so C.atof saw the leading 'S'
        // and always returned 0, silently selecting the current version
        // instead of the one actually requested.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SE1")]
        public void TestSetAstroModels_ShortEmptyOrNullInput_DoesNotThrow(string samod)
        {
            using (var swe = new SwissEph())
            {
                var ex = Record.Exception(() => swe.swe_set_astro_models(samod, 0));
                Assert.Null(ex);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TestSetAstroModels_EmptyOrNull_SelectsCurrentLibraryVersion(string samod)
        {
            // SE_VERSION is "2.08" (Sweph.h.cs), which is >= 2.06, so both ""
            // and null should resolve to AMODELS_SE_2_06 = "5,9,9,4,3,0,0,4" --
            // the same model set as explicitly requesting it via the
            // digit-list branch, which does not go through the buggy code path.
            string expected;
            using (var reference = new SwissEph())
            {
                reference.swe_set_astro_models("5,9,9,4,3,0,0,4", 0);
                reference.swe_get_astro_models(null, out expected, 0);
            }

            using (var swe = new SwissEph())
            {
                swe.swe_set_astro_models(samod, 0);
                swe.swe_get_astro_models(null, out var actual, 0);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TestSetAstroModels_ShortVersionString_SelectsHistoricalModels()
        {
            // "SE1" -> dversion 1.0 -> falls through every version check down
            // to the final else -> AMODELS_SE_1_00 = "1,3,1,1,1,0,0,1" plus
            // swe_set_tid_acc(-25.7376). Before the fix this could not even be
            // reached (Substring(0, 20) throws on a 3-character input); after
            // the fix, it must resolve to the *correct* historical model set,
            // not merely avoid throwing. The reference path also sets tidal
            // acceleration explicitly, since swe_get_astro_models's output
            // includes it and the digit-list branch of swe_set_astro_models
            // (unlike the "SE..." branch) never touches it on its own.
            string expected;
            using (var reference = new SwissEph())
            {
                reference.swe_set_astro_models("1,3,1,1,1,0,0,1", 0);
                reference.swe_set_tid_acc(-25.7376);
                reference.swe_get_astro_models(null, out expected, 0);
            }

            using (var swe = new SwissEph())
            {
                swe.swe_set_astro_models("SE1", 0);
                swe.swe_get_astro_models(null, out var actual, 0);
                Assert.Equal(expected, actual);
            }
        }

        // --- Tier 2 example: SweHouse.cs char.ToUpper(hsys) == 'I' vs C toupper (swephlib house-system dispatch) ---
        //
        // Under tr-TR/az-Latn-AZ, char.ToUpper('i') is 'İ' (U+0130), not 'I', so
        // the culture-sensitive comparison silently fails to recognize house
        // system 'i' (Sunshine) and skips assigning the sun's declination into
        // the houses_calc struct, leaving it at its zero default instead of the
        // real value passed in via ascmc[9] -- wrong cusps, no error. Using
        // swe_houses_armc directly (not swe_houses) isolates exactly the fixed
        // comparison, with no ephemeris/file dependency (matches the "pure
        // computation" note in Tools/BaselineMatrix/Houses.cs, which documents
        // the same saved_sundec-per-instance hazard this test also avoids by
        // using a fresh SwissEph per call).
        [Fact]
        public void TestHousesArmc_SunshineSystem_IdenticalUnderTurkishCulture()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            const double sunDeclination = 15.5;

            double[] cuspInvariant = ComputeSunshineCusps(CultureInfo.InvariantCulture, armc, geolat, eps, sunDeclination);
            double[] cuspTurkish = ComputeSunshineCusps(new CultureInfo("tr-TR"), armc, geolat, eps, sunDeclination);

            Assert.Equal(cuspInvariant, cuspTurkish);
        }

        private static double[] ComputeSunshineCusps(CultureInfo culture, double armc, double geolat, double eps, double sunDeclination)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                using (var swe = new SwissEph())
                {
                    var cusp = new double[40];
                    var ascmc = new double[10];
                    ascmc[9] = sunDeclination;
                    swe.swe_houses_armc(armc, geolat, eps, 'i', cusp, ascmc);
                    return cusp;
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
