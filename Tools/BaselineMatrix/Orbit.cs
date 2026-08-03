using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_get_orbital_elements and swe_orbit_max_min_true_distance, reachable under
/// SEFLG_MOSEPH with no ephemeris file. Both are direct computations (no search loop) and
/// cheap -- timed directly against this branch at well under a millisecond and a couple of
/// milliseconds respectively -- so the sweep reuses <see cref="Grids.CalcPlanets"/> as its
/// base body list (which already includes the Sun and the lunar nodes/apsides, all of
/// which swe_get_orbital_elements explicitly rejects -- see its ipl guard in SweCL.cs --
/// giving free coverage of that error path) plus the six asteroids PhenoAst.cs covers.
///
/// The six asteroid rows are all SwissEph.ERR too, and for the same structural reason
/// documented in detail in PhenoAst.cs: swe_get_orbital_elements calls swe_calc for the
/// body first, and swe_calc's minor-planet dispatch requires a file regardless of iflag.
/// They are kept anyway because the exact serr text (the CHIRON_START/END window, the
/// per-jd ephemeris filename) is itself real, jd-dependent, perturbable behavior -- see the
/// failure-injection evidence in the task report -- not because they exercise real orbital
/// elements math the way the Grids.CalcPlanets-derived rows do.
/// </summary>
internal static class Orbit
{
    private const int MOSEPH = SwissEph.SEFLG_MOSEPH;

    private static readonly int[] Bodies =
    [
        .. Grids.CalcPlanets,
        SwissEph.SE_CHIRON, SwissEph.SE_PHOLUS, SwissEph.SE_CERES,
        SwissEph.SE_PALLAS, SwissEph.SE_JUNO, SwissEph.SE_VESTA,
    ];

    private static readonly double[] Jds = Grids.JdSpread(6);

    private static readonly (string Name, int Flag)[] IflagCombos =
    [
        ("MOSEPH", MOSEPH),
        ("MOSEPH_HELCTR", MOSEPH | SwissEph.SEFLG_HELCTR),
        ("MOSEPH_BARYCTR", MOSEPH | SwissEph.SEFLG_BARYCTR),
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Bodies)
        {
            foreach (var jd in Jds)
            {
                foreach (var (flagName, flag) in IflagCombos)
                {
                    rows.Add(BuildOrbitalElementsRow(ipl, jd, flagName, flag));
                    rows.Add(BuildMaxMinTrueDistanceRow(ipl, jd, flagName, flag));
                }
            }
        }
    }

    private static string BuildOrbitalElementsRow(int ipl, double jd, string flagName, int flag)
    {
        var caseId = $"OE|{I(ipl)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var dret = new double[50];
            string? serr = null;
            var retc = swe.swe_get_orbital_elements(jd, ipl, flag, dret, ref serr);
            return
            [
                I(retc),
                D(dret[0]), D(dret[1]), D(dret[2]), D(dret[3]), D(dret[4]),
                D(dret[5]), D(dret[6]), D(dret[7]), D(dret[8]), D(dret[9]),
                D(dret[10]), D(dret[11]), D(dret[12]), D(dret[13]), D(dret[14]),
                D(dret[15]), D(dret[16]),
                S(serr),
            ];
        });
    }

    private static string BuildMaxMinTrueDistanceRow(int ipl, double jd, string flagName, int flag)
    {
        var caseId = $"OMM|{I(ipl)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            double dmax = 0, dmin = 0, dtrue = 0;
            string? serr = null;
            var retc = swe.swe_orbit_max_min_true_distance(jd, ipl, flag, ref dmax, ref dmin, ref dtrue, ref serr);
            return [I(retc), D(dmax), D(dmin), D(dtrue), S(serr)];
        });
    }
}
