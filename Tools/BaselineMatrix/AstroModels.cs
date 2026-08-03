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

    // AMSDT's own dedicated JD list. CalcJds (JD 1,000,000 and 2,600,000, used for
    // AMSAYA/AMSCALC too) never lands inside SwephLib.cs:2756's "deltat_model ==
    // SEMOD_DELTAT_STEPHENSON_MORRISON_1984 && Y < 1620" window -- that branch is
    // gated on a conjunction of model *and* year, and no other sweep in this matrix
    // varies deltat_model at all, so it stayed dead regardless of which model value
    // the DELTAT dimension swept. JD 2,159,345.0 is year ~1200 by calc_deltat's own
    // Y formula (Y = 2000 + (tjd - J2000) / 365.25, J2000 = 2451545.0), comfortably
    // inside [948, 1620). Scoped to AMSDT only (not AMSAYA/AMSCALC) to keep this
    // additive: every existing AMSAYA/AMSCALC row is untouched.
    private static readonly double[] AmsdtJds = [.. CalcJds, 2_159_345.0];

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

        CheckInterpolateNutReached(rows);
        CheckDeltaTWindowReached(rows);
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

        foreach (var jd in AmsdtJds)
        {
            rows.Add(SafeRow($"AMSDT|{dimName}|{I(value)}|{D(jd)}", () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_astro_models(samod, 0);
                string? serr = null;
                var dt = swe.swe_deltat_ex(jd, MOSEPH, ref serr);
                return [D(dt), S(serr)];
            }));
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

    // Hoisted out of AddInterpolateNutRows so CheckInterpolateNutReached below can name
    // the same Julian days the sweep emits rows for, instead of re-deriving them a
    // second way and quietly checking a different set if one of the two ever changes.
    private static readonly double[] NutJds = Grids.JdSpread(3);

    private static void AddInterpolateNutRows(List<string> rows)
    {
        foreach (var doInterpolate in new[] { false, true })
        {
            foreach (var jd in NutJds)
            {
                foreach (var ipl in CalcBodies)
                {
                    var caseId = $"AMNUT|{B(doInterpolate)}|{D(jd)}|{I(ipl)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();

                        // Warm-up call, result discarded, at a jd far outside the
                        // one-day window the two recorded calls below live in:
                        // swi_init_swed_if_start (sweph.c:1181-1192, mirrored in
                        // Sweph.cs) does an unconditional memset(&swed, 0, ...)
                        // the *first* time anything triggers it, which wipes
                        // do_interpolate_nut straight back to false if it was set
                        // beforehand -- this is genuine reference behavior, not a
                        // port quirk. Calling swe_set_interpolate_nut before any
                        // swe_calc (the ordering this sweep used to use) therefore
                        // had no effect at all: AMNUT|True and AMNUT|False were
                        // byte-identical for every jd/ipl before this fix, not
                        // just the quadratic_intp sub-branch within the "true"
                        // path -- the outer do_interpolate_nut branch itself was
                        // never reachable either. This warm-up call forces that
                        // one-time reset to happen before we set the flag. It
                        // must land on a different jd than the first recorded
                        // call below: swe_calc caches the last-computed position
                        // per instance and returns it without ever reaching
                        // swi_nutation again for a repeated identical jd, which
                        // would otherwise silently prevent the first recorded
                        // call from ever (re-)seeding interpol.tjd_nut0/tjd_nut2.
                        var warmupXx = new double[6];
                        string? warmupSerr = null;
                        swe.swe_calc(jd - 5000.0, ipl, MOSEPH, warmupXx, ref warmupSerr);

                        swe.swe_set_interpolate_nut(doInterpolate);

                        var xx = new double[6];
                        string? serr = null;
                        var retc = swe.swe_calc(jd, ipl, MOSEPH, xx, ref serr);

                        // A second call within one day of the first, on the SAME
                        // instance and with do_interpolate_nut still set, is what
                        // makes swi_nutation's quadratic_intp branch
                        // (SwephLib.cs:2202-2205) reachable at all: it interpolates
                        // from the immediately preceding call's cached nutation
                        // data points rather than recomputing. Its result (xx2),
                        // not the first call's (xx), is what a hardcoded-zero
                        // stand-in for that branch would get wrong.
                        var xx2 = new double[6];
                        string? serr2 = null;
                        var retc2 = swe.swe_calc(jd + 0.5, ipl, MOSEPH, xx2, ref serr2);
                        return [I(retc), D(xx[0]), D(xx[1]), D(xx[2]), S(serr),
                                I(retc2), D(xx2[0]), D(xx2[1]), D(xx2[2]), S(serr2)];
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

    // AMNUT|True and AMNUT|False were byte-identical for every jd/ipl while the sweep
    // set the flag on a fresh instance before any swe_calc: swi_init_swed_if_start's
    // one-time reset wiped do_interpolate_nut before it could ever be read, so the
    // whole interpolated-nutation path was dead and the "False" rows and the "True"
    // rows described the same computation. Nothing about the emitted rows said so --
    // there were still twelve of them, still with the two distinct case-id prefixes.
    //
    // Their being identical is therefore the symptom itself, and their differing is
    // the evidence the path is reached. Any future edit that drops the warm-up call,
    // reverts to a fresh instance per recorded call, or drops the second call inside
    // the one-day interpolation window collapses the pair back together and fails
    // here, before any of this area's rows reach a file.
    private static void CheckInterpolateNutReached(List<string> rows)
    {
        var index = Reachability.IndexPayloads(rows);
        foreach (var jd in NutJds)
        {
            foreach (var ipl in CalcBodies)
            {
                Reachability.RequireDistinctPayloads(
                    index,
                    "AMNUT",
                    "swi_nutation's interpolated-nutation path (SwephLib.cs:2202-2205's quadratic_intp)",
                    "Reaching it requires swe_set_interpolate_nut(true) to be called AFTER a warm-up swe_calc has " +
                    "absorbed swi_init_swed_if_start's one-time reset, and a second swe_calc within one day of the " +
                    "first on the SAME instance.",
                    $"AMNUT|{B(false)}|{D(jd)}|{I(ipl)}",
                    $"AMNUT|{B(true)}|{D(jd)}|{I(ipl)}");
            }
        }
    }

    // calc_deltat's own year formula, SwephLib.cs:2665.
    private const double J2000 = 2451545.0;

    // SwephLib.cs:2756 gates the SEMOD_DELTAT_STEPHENSON_MORRISON_1984 model on
    // Y < TABSTART (1620, SwephLib.cs:2494); SwephLib.cs:2758 then splits that at
    // Y >= 948 into Stephenson & Morrison's own formula (stated domain 948 to 1600)
    // and Borkowski's fallback below it. CalcJds alone reached only the fallback
    // (JD 1,000,000 is year ~-1974) and the post-1620 tables (JD 2,600,000 is year
    // ~2406), so the model's own formula was never evaluated by anything here.
    private const double Sm1984YearFloor = 948.0;
    private const double Sm1984YearCeiling = 1620.0;

    private static double DeltaTYear(double tjd) => 2000.0 + (tjd - J2000) / 365.25;

    // Two things have to hold at once for that branch to be reached, because it is
    // gated on a conjunction: the DELTAT dimension must sweep the model, and some
    // AMSDT Julian day must land inside the year window. Straddling the window emits
    // every AMSDT row exactly as landing inside it does, which is how this stayed
    // unreached; so assert the input lands where it must, then confirm the model is
    // observable there by requiring its row to differ from every other DELTAT value's
    // row at the same Julian day.
    private static void CheckDeltaTWindowReached(List<string> rows)
    {
        const int sm1984 = SwissEph.SEMOD_DELTAT_STEPHENSON_MORRISON_1984;

        var deltat = Array.Find(Dimensions, dimension => string.Equals(dimension.Name, "DELTAT", StringComparison.Ordinal));
        if (deltat.Values is null || !deltat.Values.Contains(sm1984))
        {
            throw new InvalidOperationException(
                "AMSDT sweep is no longer reaching SwephLib.cs:2756's SEMOD_DELTAT_STEPHENSON_MORRISON_1984 branch: " +
                $"the DELTAT dimension does not sweep model value {I(sm1984)} at all. No other sweep in this matrix " +
                "varies deltat_model, so nothing else can reach it either.");
        }

        var inWindow = Array.FindAll(AmsdtJds, jd => DeltaTYear(jd) >= Sm1984YearFloor && DeltaTYear(jd) < Sm1984YearCeiling);
        if (inWindow.Length == 0)
        {
            throw new InvalidOperationException(
                "AMSDT sweep is no longer reaching SwephLib.cs:2756-2764's SEMOD_DELTAT_STEPHENSON_MORRISON_1984 " +
                $"formula: none of its Julian days lands inside the [{D(Sm1984YearFloor)}, {D(Sm1984YearCeiling)}) " +
                "year window that branch is gated on. Julian days swept, with the year calc_deltat derives from each " +
                "(Y = 2000 + (tjd - J2000) / 365.25): " +
                string.Join("; ", AmsdtJds.Select(jd => $"{D(jd)} -> {D(DeltaTYear(jd))}")) + ". " +
                "Days that straddle the window without landing in it emit every AMSDT row unchanged and take some " +
                "other deltat branch entirely, which is how this sweep was dead while its gate stayed green.");
        }

        var index = Reachability.IndexPayloads(rows);
        foreach (var jd in inWindow)
        {
            foreach (var other in deltat.Values)
            {
                if (other == sm1984)
                {
                    continue;
                }

                Reachability.RequireDistinctPayloads(
                    index,
                    "AMSDT",
                    "SwephLib.cs:2756-2764's SEMOD_DELTAT_STEPHENSON_MORRISON_1984 formula at year " +
                    D(DeltaTYear(jd)),
                    $"Reaching it requires both halves of that branch's conjunction: deltat_model = {I(sm1984)} and a " +
                    $"Julian day inside [{D(Sm1984YearFloor)}, {D(Sm1984YearCeiling)}).",
                    $"AMSDT|DELTAT|{I(sm1984)}|{D(jd)}",
                    $"AMSDT|DELTAT|{I(other)}|{D(jd)}");
            }
        }
    }
}
