using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_calc / swe_calc_ut with SEFLG_MOSEPH, across planets, Julian days and iflag
/// combos. Includes two Julian days outside the Moshier window (625000.5 to
/// 2818000.5) to exercise the range check, ERR return and serr message, and a
/// separate topocentric pass since SEFLG_TOPOCTR only means something after
/// swe_set_topo has been called.
/// </summary>
internal static class Calc
{
    private static readonly double[] Jds = [.. Grids.JdSpread(20), 500_000, 3_000_000];

    private static readonly (double Lon, double Lat, double Height)[] TopoObservers =
    [
        (0, 51.5, 0),
        (-118.24, 34.05, 100),
    ];

    // The topocentric pass originally covered only plain SEFLG_TOPOCTR, leaving
    // xx[3..5] (speed) at zero for all 160 CTOPO/CUTOPO rows -- the largest single
    // dead-column block in the file. Topocentric speed runs through
    // swi_get_observer plus numerical differentiation, both named as churn areas,
    // and SPEED fields are already the worst cross-platform offenders (see
    // docs/known-issues.md), so this is specifically worth freezing.
    private static readonly (string Name, int Flag)[] TopoIflagVariants =
    [
        ("", 0),
        ("_SPEED", SwissEph.SEFLG_SPEED),
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Grids.CalcPlanets)
        {
            foreach (var jd in Jds)
            {
                foreach (var (flagName, flag) in Grids.CalcIflagCombos)
                {
                    var iflag = SwissEph.SEFLG_MOSEPH | flag;
                    rows.Add(BuildRow("C", ipl, jd, flagName, iflag, useUt: false));
                    rows.Add(BuildRow("CU", ipl, jd, flagName, iflag, useUt: true));
                }
            }

            foreach (var jd in Grids.JdSpread(5))
            {
                foreach (var observer in TopoObservers)
                {
                    foreach (var (variantName, variantFlag) in TopoIflagVariants)
                    {
                        rows.Add(BuildTopoRow($"CTOPO{variantName}", ipl, jd, observer, variantFlag, useUt: false));
                        rows.Add(BuildTopoRow($"CUTOPO{variantName}", ipl, jd, observer, variantFlag, useUt: true));
                    }
                }
            }
        }
    }

    private static string BuildRow(string prefix, int ipl, double jd, string flagName, int iflag, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var xx = new double[6];
            string? serr = null;
            var retc = useUt
                ? swe.swe_calc_ut(jd, ipl, iflag, xx, ref serr)
                : swe.swe_calc(jd, ipl, iflag, xx, ref serr);

            return
            [
                I(retc),
                D(xx[0]), D(xx[1]), D(xx[2]), D(xx[3]), D(xx[4]), D(xx[5]),
                S(serr),
            ];
        });
    }

    private static string BuildTopoRow(string prefix, int ipl, double jd, (double Lon, double Lat, double Height) observer, int extraFlag, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_topo(observer.Lon, observer.Lat, observer.Height);
            var iflag = SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TOPOCTR | extraFlag;
            var xx = new double[6];
            string? serr = null;
            var retc = useUt
                ? swe.swe_calc_ut(jd, ipl, iflag, xx, ref serr)
                : swe.swe_calc(jd, ipl, iflag, xx, ref serr);

            return
            [
                I(retc),
                D(xx[0]), D(xx[1]), D(xx[2]), D(xx[3]), D(xx[4]), D(xx[5]),
                S(serr),
            ];
        });
    }
}
