using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_refrac, swe_refrac_extended (both SE_TRUE_TO_APP and SE_APP_TO_TRUE), and
/// swe_set_lapse_rate. Pure atmospheric math with no ephemeris dependency at all, so the
/// sweep here is wide -- each call is sub-millisecond (timed directly against this branch).
///
/// One nuance worth recording: swe_set_lapse_rate does NOT affect a direct
/// swe_refrac_extended call, because swe_refrac_extended takes lapse_rate as an explicit
/// parameter and calc_dip uses that parameter only (SweCL.cs's calc_dip, called from
/// swe_refrac_extended, never reads the const_lapse_rate field swe_set_lapse_rate writes).
/// const_lapse_rate is only consulted by *other* functions that call swe_refrac_extended
/// internally without exposing lapse_rate to their own caller -- swe_azalt (already covered
/// by the existing, frozen CoordHelpers.cs area) and swe_rise_trans_true_hor (see
/// RiseTrans.cs). AddLapseRateEffectRows below characterizes that real, internal-state
/// effect through swe_rise_trans_true_hor rather than pretending a direct
/// swe_refrac_extended call would show it.
/// </summary>
internal static class Atmo
{
    // Deliberately spans the low-altitude region where both refraction formulas switch
    // branches (swe_refrac branches at 15 and -5 degrees; swe_refrac_extended iterates
    // near the horizon), plus values above 90 to exercise the inalt>90 fold-back.
    private static readonly double[] InAlts =
        [-10, -5, -2, -1, -0.5, -0.34, 0, 0.5, 1, 2, 5, 10, 20, 45, 90, 95];

    private static readonly double[] AtPresses = [0, 800, 1013.25, 1050];
    private static readonly double[] AtTemps = [-20, 0, 15, 40];

    private static readonly (string Name, int Flag)[] CalcFlags =
        [("TRUE_TO_APP", SwissEph.SE_TRUE_TO_APP), ("APP_TO_TRUE", SwissEph.SE_APP_TO_TRUE)];

    private static readonly double[] GeoAlts = [0, 1000, 3000];
    private static readonly double[] LapseRates = [0.0065, 0, 0.01];

    public static void AddRows(List<string> rows)
    {
        AddRefracRows(rows);
        AddRefracExtendedRows(rows);
        AddLapseRateEffectRows(rows);
    }

    private static void AddRefracRows(List<string> rows)
    {
        foreach (var inalt in InAlts)
        {
            foreach (var atpress in AtPresses)
            {
                foreach (var attemp in AtTemps)
                {
                    foreach (var (flagName, flag) in CalcFlags)
                    {
                        var caseId = $"REFR|{D(inalt)}|{D(atpress)}|{D(attemp)}|{flagName}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var result = swe.swe_refrac(inalt, atpress, attemp, flag);
                            return [D(result)];
                        }));
                    }
                }
            }
        }
    }

    private static void AddRefracExtendedRows(List<string> rows)
    {
        foreach (var inalt in InAlts)
        {
            foreach (var geoalt in GeoAlts)
            {
                foreach (var atpress in new[] { 0.0, 1013.25 })
                {
                    foreach (var attemp in new[] { -10.0, 15.0 })
                    {
                        foreach (var lapseRate in LapseRates)
                        {
                            foreach (var (flagName, flag) in CalcFlags)
                            {
                                var caseId = $"REFX|{D(inalt)}|{D(geoalt)}|{D(atpress)}|{D(attemp)}|{D(lapseRate)}|{flagName}";
                                rows.Add(SafeRow(caseId, () =>
                                {
                                    using var swe = new SwissEph();
                                    var dret = new double[20];
                                    var result = swe.swe_refrac_extended(inalt, geoalt, atpress, attemp, lapseRate, flag, dret);
                                    return [D(result), D(dret[0]), D(dret[1]), D(dret[2]), D(dret[3])];
                                }));
                            }
                        }
                    }
                }
            }
        }
    }

    // swe_set_lapse_rate followed by the exact same lapse rate passed explicitly to
    // swe_refrac_extended -- a no-op by construction (see the class doc comment), but
    // worth freezing as the idiom itself: if a future version made swe_refrac_extended
    // consult the stored rate instead of (or in addition to) its own parameter, this
    // would be the row that would move.
    private static void AddLapseRateEffectRows(List<string> rows)
    {
        foreach (var lapseRate in LapseRates)
        {
            var caseId = $"LAPSEDIRECT|{D(lapseRate)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                swe.swe_set_lapse_rate(lapseRate);
                var dret = new double[20];
                var result = swe.swe_refrac_extended(1, 0, 1013.25, 15, lapseRate, SwissEph.SE_TRUE_TO_APP, dret);
                return [D(result), D(dret[0]), D(dret[1]), D(dret[2]), D(dret[3])];
            }));
        }

        // The real, internal-state-dependent effect: swe_rise_trans_true_hor calls
        // swe_refrac_extended itself using whatever swe_set_lapse_rate last set, without
        // the caller ever passing a lapse rate directly (SweCL.cs, the true_hor
        // low-altitude refraction correction). A fresh instance per row, with
        // swe_set_lapse_rate called before the single swe_rise_trans_true_hor call.
        double[] jds = Grids.JdSpread(3);
        foreach (var jd in jds)
        {
            foreach (var lapseRate in LapseRates)
            {
                var caseId = $"LAPSERISE|{D(jd)}|{D(lapseRate)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    swe.swe_set_lapse_rate(lapseRate);
                    var geopos = new double[] { 0, 51.5, 0 };
                    double tret = 0;
                    string? serr = null;
                    var retc = swe.swe_rise_trans_true_hor(jd, SwissEph.SE_SUN, null, SwissEph.SEFLG_MOSEPH, SwissEph.SE_CALC_RISE, geopos, 0, 0, 0, ref tret, ref serr);
                    return [I(retc), D(tret), S(serr)];
                }));
            }
        }
    }
}
