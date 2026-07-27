using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_nod_aps / swe_nod_aps_ut: osculating and mean nodes/apsides. Analytic under
/// SEFLG_MOSEPH, no ephemeris file needed, and shares the osculating-elements
/// machinery with SE_OSCU_APOG -- already the worst cross-platform SPEED-field
/// offender recorded in docs/known-issues.md -- so this is a cheap, high-value
/// addition rather than a new code family to reason about.
/// </summary>
internal static class NodAps
{
    private static readonly int[] Planets = [SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_VENUS, SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_SATURN, SwissEph.SE_URANUS, SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO];
    private static readonly double[] Jds = Grids.JdSpread(6);

    private static readonly (string Name, int Method)[] Methods =
    [
        ("MEAN", SwissEph.SE_NODBIT_MEAN),
        ("OSCU", SwissEph.SE_NODBIT_OSCU),
        ("OSCU_BAR", SwissEph.SE_NODBIT_OSCU_BAR),
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Planets)
        {
            foreach (var jd in Jds)
            {
                foreach (var (methodName, method) in Methods)
                {
                    rows.Add(BuildRow("NA", ipl, jd, methodName, method, useUt: false));
                    rows.Add(BuildRow("NAUT", ipl, jd, methodName, method, useUt: true));
                }
            }
        }
    }

    private static string BuildRow(string prefix, int ipl, double jd, string methodName, int method, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{methodName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var xnasc = new double[6];
            var xndsc = new double[6];
            var xperi = new double[6];
            var xaphe = new double[6];
            string? serr = null;
            const int iflag = SwissEph.SEFLG_MOSEPH;
            var retc = useUt
                ? swe.swe_nod_aps_ut(jd, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, ref serr)
                : swe.swe_nod_aps(jd, ipl, iflag, method, xnasc, xndsc, xperi, xaphe, ref serr);

            return
            [
                I(retc),
                D(xnasc[0]), D(xnasc[1]), D(xnasc[2]),
                D(xndsc[0]), D(xndsc[1]), D(xndsc[2]),
                D(xperi[0]), D(xperi[1]), D(xperi[2]),
                D(xaphe[0]), D(xaphe[1]), D(xaphe[2]),
                S(serr),
            ];
        });
    }
}
