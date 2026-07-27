using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_pheno / swe_pheno_ut on the Moshier path: phase, phase angle, elongation, magnitude, apparent diameter.</summary>
internal static class Pheno
{
    private static readonly double[] Jds = Grids.JdSpread(10);

    private static readonly (string Name, int Flag)[] IflagCombos =
    [
        ("MOSEPH", SwissEph.SEFLG_MOSEPH),
        ("MOSEPH_TRUEPOS", SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TRUEPOS),
        ("MOSEPH_HELCTR", SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_HELCTR),
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Grids.CalcPlanets)
        {
            foreach (var jd in Jds)
            {
                foreach (var (flagName, flag) in IflagCombos)
                {
                    rows.Add(BuildRow("PH", ipl, jd, flagName, flag, useUt: false));
                    rows.Add(BuildRow("PHUT", ipl, jd, flagName, flag, useUt: true));
                }
            }
        }
    }

    private static string BuildRow(string prefix, int ipl, double jd, string flagName, int flag, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var attr = new double[20];
            string? serr = null;
            var retc = useUt
                ? swe.swe_pheno_ut(jd, ipl, flag, attr, ref serr)
                : swe.swe_pheno(jd, ipl, flag, attr, ref serr);

            return
            [
                I(retc),
                D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]), D(attr[4]),
                S(serr),
            ];
        });
    }
}
