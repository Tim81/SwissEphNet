using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_pheno / swe_pheno_ut on the Moshier path: phase angle, phase, elongation,
/// apparent diameter, magnitude, and (Moon only, topocentric only) horizontal
/// parallax in attr[5].
///
/// swe_pheno masks iflag down to SEFLG_EPHMASK | TRUEPOS | J2000 | NONUT | NOGDEFL |
/// NOABERR | TOPOCTR before doing anything with it -- SEFLG_HELCTR is not in that
/// mask, so a "MOSEPH_HELCTR" combo would silently collapse to plain MOSEPH and add
/// nothing. NOABERR survives the mask and visibly perturbs phase/elongation, so it
/// replaces HELCTR here.
/// </summary>
internal static class Pheno
{
    private static readonly double[] Jds = Grids.JdSpread(10);

    private static readonly (string Name, int Flag)[] IflagCombos =
    [
        ("MOSEPH", SwissEph.SEFLG_MOSEPH),
        ("MOSEPH_TRUEPOS", SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TRUEPOS),
        ("MOSEPH_NOABERR", SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_NOABERR),
    ];

    private static readonly (double Lon, double Lat, double Height)[] TopoObservers =
    [
        (0, 51.5, 0),
        (-118.24, 34.05, 100),
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

            foreach (var jd in Grids.JdSpread(4))
            {
                foreach (var observer in TopoObservers)
                {
                    rows.Add(BuildTopoRow("PHTOPO", ipl, jd, observer, useUt: false));
                    rows.Add(BuildTopoRow("PHUTTOPO", ipl, jd, observer, useUt: true));
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
            return Fields(retc, attr, serr);
        });
    }

    private static string BuildTopoRow(string prefix, int ipl, double jd, (double Lon, double Lat, double Height) observer, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_topo(observer.Lon, observer.Lat, observer.Height);
            var iflag = SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TOPOCTR;
            var attr = new double[20];
            string? serr = null;
            var retc = useUt
                ? swe.swe_pheno_ut(jd, ipl, iflag, attr, ref serr)
                : swe.swe_pheno(jd, ipl, iflag, attr, ref serr);
            return Fields(retc, attr, serr);
        });
    }

    private static string[] Fields(int retc, double[] attr, string? serr) =>
    [
        I(retc),
        D(attr[0]), D(attr[1]), D(attr[2]), D(attr[3]), D(attr[4]), D(attr[5]),
        S(serr),
    ];
}
