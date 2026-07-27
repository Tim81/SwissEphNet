using SwissEphNet;
using static BaselineGen.Format;

namespace BaselineGen;

/// <summary>swe_calc / swe_calc_ut with SEFLG_MOSEPH, across planets, Julian days and iflag combos.</summary>
internal static class Calc
{
    private static readonly double[] Jds = Grids.JdSpread(20);

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
}
