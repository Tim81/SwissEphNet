using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_set_astro_models across the SEMOD_PREC_*, SEMOD_NUT_*, SEMOD_DELTAT_* and
/// SEMOD_JPLHOR*/SEMOD_JPLHORA* families, swe_get_astro_models round-tripping the values
/// back, swe_set_interpolate_nut, and the separate "SE&lt;version&gt;" string form that
/// swe_set_astro_models also accepts. None of this is exercised anywhere else in the
/// matrix -- swe_set_astro_models is never called, so in swi_epsiln alone ten of eleven
/// model branches are dead to the gate (see SwephLib.cs). The digit-string form sets
/// swed.astro_models[SE_MODEL_*] directly, one index at a time here, leaving every other
/// index at 0 (= "use the compiled-in default" throughout SwephLib.cs); the
/// "SE&lt;version&gt;" form instead looks up a whole fixed bundle of models per historical
/// SE version (SwephLib.cs's AMODELS_SE_* constants) and is a genuinely different code
/// path, covered separately here.
///
/// A fresh SwissEph instance per row, as everywhere else in this matrix -- astro_models is
/// itself per-instance state, so reusing one would make later rows depend on earlier ones.
/// The recompute slice is deliberately small (two bodies, two Julian days) per row: this
/// area exists to prove the model-selection mechanism moves observable output at all, not
/// to re-sweep swe_calc's own grid (already covered in Calc.cs).
/// </summary>
internal static class AstroModels
{
    private const int MOSEPH = SwissEph.SEFLG_MOSEPH;

    private static readonly int[] CalcBodies = [SwissEph.SE_SUN, SwissEph.SE_MOON];
    private static readonly double[] CalcJds = Grids.JdSpread(2);

    // (dimension name, SE_MODEL_* index, values to try -- 0 always means "default")
    private static readonly (string Name, int ModelIndex, int[] Values)[] Dimensions =
    [
        ("DELTAT", SwissEph.SE_MODEL_DELTAT, [0, 1, 2, 3, 4, 5]),
        ("PREC", SwissEph.SE_MODEL_PREC_LONGTERM, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
        ("NUT", SwissEph.SE_MODEL_NUT, [0, 1, 2, 3, 4]),
        ("JPLHOR", SwissEph.SE_MODEL_JPLHOR_MODE, [0, 1, 2]),
        ("JPLHORA", SwissEph.SE_MODEL_JPLHORA_MODE, [0, 1, 2, 3]),
    ];

    // Exercises the "SE<version>" bundle-lookup branch of swe_set_astro_models
    // (SwephLib.cs's dversion parsing), including the "remove second '.'" and
    // "remove trailing 'b'" string massaging it does before calling C.atof.
    private static readonly string[] VersionStrings =
        ["", "SE1.00", "SE1.64", "SE1.70", "SE1.72", "SE1.77", "SE1.78", "SE1.80", "SE2.00", "SE2.06", "SE2.10", "SE2.05.01", "SE2.05.02b04"];

    public static void AddRows(List<string> rows)
    {
        foreach (var (dimName, modelIndex, values) in Dimensions)
        {
            foreach (var value in values)
            {
                var samod = BuildSamod(modelIndex, value);
                AddDimensionRows(rows, dimName, value, samod);
            }
        }

        AddVersionStringRows(rows);
        AddInterpolateNutRows(rows);
        AddListAllModelsRow(rows);
    }

    private static string BuildSamod(int modelIndex, int value)
    {
        var parts = new int[SwissEph.NSE_MODELS];
        parts[modelIndex] = value;
        return string.Join(',', parts);
    }

    private static void AddDimensionRows(List<string> rows, string dimName, int value, string samod)
    {
        rows.Add(SafeRow($"AMSDET|{dimName}|{I(value)}", () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_astro_models(samod, 0);
            swe.swe_get_astro_models(null!, out var sdet, MOSEPH);
            return [S(sdet)];
        }));

        foreach (var jd in CalcJds)
        {
            rows.Add(SafeRow($"AMSAYA|{dimName}|{I(value)}|{D(jd)}", () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_astro_models(samod, 0);
                string? serr = null;
                var retc = swe.swe_get_ayanamsa_ex(jd, MOSEPH, out var daya, ref serr);
                return [I(retc), D(daya), S(serr)];
            }));

            rows.Add(SafeRow($"AMSDT|{dimName}|{I(value)}|{D(jd)}", () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_astro_models(samod, 0);
                string? serr = null;
                var dt = swe.swe_deltat_ex(jd, MOSEPH, ref serr);
                return [D(dt), S(serr)];
            }));

            foreach (var ipl in CalcBodies)
            {
                rows.Add(SafeRow($"AMSCALC|{dimName}|{I(value)}|{I(ipl)}|{D(jd)}", () =>
                {
                    using var swe = new SwissEph();
                    swe.swe_set_astro_models(samod, 0);
                    var xx = new double[6];
                    string? serr = null;
                    var retc = swe.swe_calc(jd, ipl, MOSEPH, xx, ref serr);
                    return [I(retc), D(xx[0]), D(xx[1]), D(xx[2]), D(xx[3]), D(xx[4]), D(xx[5]), S(serr)];
                }));
            }
        }
    }

    private static void AddVersionStringRows(List<string> rows)
    {
        foreach (var version in VersionStrings)
        {
            var label = version.Length == 0 ? "EMPTY" : version;

            rows.Add(SafeRow($"AMVDET|{label}", () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_astro_models(version, 0);
                swe.swe_get_astro_models(null!, out var sdet, MOSEPH);
                return [S(sdet)];
            }));

            var jd = CalcJds[0];
            rows.Add(SafeRow($"AMVCALC|{label}", () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_astro_models(version, 0);
                var xx = new double[6];
                string? serr = null;
                var retc = swe.swe_calc(jd, SwissEph.SE_SUN, MOSEPH, xx, ref serr);
                return [I(retc), D(xx[0]), D(xx[1]), D(xx[2]), S(serr)];
            }));
        }
    }

    private static void AddInterpolateNutRows(List<string> rows)
    {
        foreach (var doInterpolate in new[] { false, true })
        {
            foreach (var jd in Grids.JdSpread(3))
            {
                foreach (var ipl in CalcBodies)
                {
                    var caseId = $"AMNUT|{B(doInterpolate)}|{D(jd)}|{I(ipl)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        swe.swe_set_interpolate_nut(doInterpolate);
                        var xx = new double[6];
                        string? serr = null;
                        var retc = swe.swe_calc(jd, ipl, MOSEPH, xx, ref serr);
                        return [I(retc), D(xx[0]), D(xx[1]), D(xx[2]), S(serr)];
                    }));
                }
            }
        }
    }

    // The '+' in samod makes swe_get_astro_models list every available model for every
    // dimension in sdet, a branch none of the rows above (which pass samod=null to the
    // getter) ever reaches.
    private static void AddListAllModelsRow(List<string> rows)
    {
        rows.Add(SafeRow("AMLISTALL|1", () =>
        {
            using var swe = new SwissEph();
            swe.swe_get_astro_models("+", out var sdet, MOSEPH);
            return [S(sdet)];
        }));
    }
}
