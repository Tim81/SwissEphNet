using System;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{
    // Newcomb precession (SEMOD_PREC_NEWCOMB) and Woolard nutation
    // (SEMOD_NUT_WOOLARD) are new in the 2.10.03 delta to swephlib.c and are
    // not selected by any default model, so nothing else exercises them.
    // These call the internal SwephLib methods through reflection (no
    // InternalsVisibleTo is declared for this assembly -- see
    // SwissEphTest.FileNaming.cs's GenFileName for the established pattern),
    // and check the result against a formula independently re-typed from
    // external/swisseph/swephlib.c (not against the C# source), so a
    // transliteration mistake in either the branch dispatch or the formula
    // itself would be caught.
    //
    // Each expected value was computed with a short standalone Python
    // reimplementation of the same C formula, not by reading back the C#
    // under test. If the SEMOD_PREC_NEWCOMB/SEMOD_NUT_WOOLARD branches were
    // absent (as before this port), swi_epsiln and precess_1 would fall
    // through to the next model in the if/else chain (IAU 2006) and
    // calc_nutation would fall through to no additional adjustment at all --
    // both give materially different numbers from what is asserted here, so
    // these tests fail without the new branches, not just with a wrong one.
    public class SwephLibAstroModelsTest
    {
        static object GetSwephLib(SwissEph swe)
        {
            var prop = typeof(SwissEph).GetProperty("SwephLib", BindingFlags.NonPublic | BindingFlags.Instance);
            return prop.GetValue(swe);
        }

        // "0,11,0,0,0,0,0,0" sets SE_MODEL_PREC_LONGTERM (index 1) to
        // SEMOD_PREC_NEWCOMB (11); every other slot is left at its default
        // (0 -> "use the compiled-in default for this slot").
        const string SamodPrecNewcombOnly = "0,11,0,0,0,0,0,0";

        // "0,0,0,5,0,0,0,0" sets SE_MODEL_NUT (index 3) to SEMOD_NUT_WOOLARD (5).
        const string SamodNutWoolardOnly = "0,0,0,5,0,0,0,0";

        [Fact]
        public void EpsilnNewcombMatchesIndependentFormula()
        {
            // swephlib.c:925-926: eps = (0.0017*Tn^3 - 0.0085*Tn^2 - 46.837*Tn
            // + 84451.68) * DEGTORAD/3600, Tn = (J - 2396758.0)/36525.0.
            // J = J1900 (2415020.0). Reference computed independently in
            // Python: eps = 0.40931975631180173 rad.
            using var swe = new SwissEph();
            var swephLib = GetSwephLib(swe);
            swephLib.GetType().GetMethod("swe_set_astro_models")
                .Invoke(swephLib, new object[] { SamodPrecNewcombOnly, 0 });

            var eps = (double)swephLib.GetType().GetMethod("swi_epsiln")
                .Invoke(swephLib, new object[] { 2415020.0 /* J1900 */, 0 });

            Assert.Equal(0.40931975631180173, eps, 14);
        }

        [Fact]
        public void PrecessNewcombRotatesUnitVectorToIndependentlyComputedResult()
        {
            // swephlib.c:1100-1116 (the #if 1 "Kinoshita 1975" branch, inside the
            // wider 1033-1135 Newcomb region whose other variants are #if 0): applied
            // to R = [1, 0, 0] at J = J1900, direction = -1 (From J2000.0 to
            // J). Reference Z/z/TH and the resulting rotation were computed
            // independently in Python from the same formula:
            // [0.9997030435728291, -0.02234772350314769, -0.009716168249322534].
            using var swe = new SwissEph();
            var swephLib = GetSwephLib(swe);
            swephLib.GetType().GetMethod("swe_set_astro_models")
                .Invoke(swephLib, new object[] { SamodPrecNewcombOnly, 0 });

            var r = new double[] { 1.0, 0.0, 0.0 };
            var cr = new CPointer<double>(r);

            var precessMethod = swephLib.GetType().GetMethod("swi_precess");
            precessMethod.Invoke(swephLib, new object[] { cr, 2415020.0 /* J1900 */, 0, -1 });

            Assert.Equal(0.9997030435728291, r[0], 12);
            Assert.Equal(-0.02234772350314769, r[1], 12);
            Assert.Equal(-0.009716168249322534, r[2], 12);
        }

        [Fact]
        public void NutationWoolardMatchesIndependentFormula()
        {
            // swephlib.c:1947-2002, calc_nutation_woolard. J = J2000
            // (2451545.0). Reference computed independently in Python from the
            // same formula: dpsi = -6.770664474640127e-05 rad,
            // deps = -2.7937263692685556e-05 rad.
            using var swe = new SwissEph();
            var swephLib = GetSwephLib(swe);
            swephLib.GetType().GetMethod("swe_set_astro_models")
                .Invoke(swephLib, new object[] { SamodNutWoolardOnly, 0 });

            var nutlo = new double[] { 0.0, 0.0 };
            var cnutlo = new CPointer<double>(nutlo);

            var calcNutation = swephLib.GetType().GetMethod("calc_nutation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            calcNutation.Invoke(swephLib, new object[] { 2451545.0 /* J2000 */, 0, cnutlo });

            Assert.Equal(-6.770664474640127e-05, nutlo[0], 14);
            Assert.Equal(-2.7937263692685556e-05, nutlo[1], 14);
        }

        [Fact]
        public void GetAstroModelsDescribesNewcombAndWoolardByName()
        {
            // swephlib.c:4281-4289 and :4328-4336 add the human-readable
            // names for the two new models to get_precession_model/
            // get_nutation_model, surfaced through swe_get_astro_models.
            using var swe = new SwissEph();
            var swephLib = GetSwephLib(swe);

            // A fresh SwissEph() has not run swi_init_swed_if_start() yet.
            // swe_get_astro_models captures a CPointer<int> onto swed.astro_models
            // before its own call to swe_set_astro_models -- which is what
            // actually applies samod -- runs swi_init_swed_if_start() and (on
            // this first call only) replaces swed wholesale, leaving that
            // already-captured pointer stale. This is a pre-existing quirk of
            // swe_get_astro_models/swi_init_swed_if_start (not part of this
            // port), so route around it here by initializing swed with a
            // throwaway call first, the same way any real caller's prior
            // swe_calc would have.
            swephLib.GetType().GetMethod("swe_set_astro_models")
                .Invoke(swephLib, new object[] { SamodPrecNewcombOnly, 0 });

            var args = new object[] { "0,11,0,5,0,0,0,0", null, 0 };
            swephLib.GetType().GetMethod("swe_get_astro_models")
                .Invoke(swephLib, args);
            var sdet = (string)args[1];

            Assert.Contains("Newcomb 1895", sdet, StringComparison.Ordinal);
            Assert.Contains("Woolard 1953", sdet, StringComparison.Ordinal);
        }
    }
}
