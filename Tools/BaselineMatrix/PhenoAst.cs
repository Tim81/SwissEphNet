using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_pheno / swe_pheno_ut for the six asteroids (Chiron, Pholus, Ceres, Pallas, Juno,
/// Vesta) that Pheno.cs's <see cref="Grids.CalcPlanets"/> sweep never reaches.
///
/// CORRECTION to this change's original premise: minor planets are not actually reachable
/// under SEFLG_MOSEPH at all in this port. swe_calc's dispatch for ipl in
/// {SE_CHIRON..SE_VESTA} (Sweph.cs, the "minor planets" branch starting at line 1148)
/// routes to file-based ephemeris lookup unconditionally -- it never inspects iflag for
/// SEFLG_MOSEPH/SWIEPH/JPLEPH the way the main-planet branches do, because there never was
/// a Moshier semi-analytic series for asteroids in the first place. Confirmed directly:
/// every row here is SwissEph.ERR, either "Chiron's ephemeris is restricted to JD ..."
/// (outside CHIRON_START/CHIRON_END) or "SwissEph file 'seas_NN.se1' not found in PATH
/// '[ephe]'" (inside it, and for all jds for the other five bodies) -- swe_pheno's own
/// pla_diam[] read (SweCL.cs, "apparent diameter of disk") is never reached, because
/// swe_pheno's first swe_calc call for the body already fails and returns before that
/// line runs. So this area, as originally intended -- making 2.10.03's pla_diam[] change
/// (16 of 21 values change, Chiron and Pholus go from a literal 0.0 to a real diameter)
/// visible -- cannot work, structurally, no matter how this harness is written: the
/// change lives behind a code path this repo's no-OnLoadFile constraint can never reach.
/// That is worth recording precisely because it looked like it should have been
/// possible; see the top-level task report for the full explanation.
///
/// What this area still legitimately freezes: the exact CHIRON_START/CHIRON_END window and
/// its serr message, and the exact per-jd/per-body ephemeris filename the lookup asks for
/// (a real, jd-dependent computation, sensitive to the ephemeris file-block-naming scheme)
/// -- both confirmed to move under targeted perturbation (see the failure-injection
/// evidence in the task report). Structured like Pheno.cs (same iflag combos, same
/// topocentric pass with and without SEFLG_SPEED) purely so the call shape stays directly
/// comparable, not because that structure does anything useful here; kept as a separate
/// file and area per the no-touching-existing-areas rule, not folded into Pheno.cs.
/// </summary>
internal static class PhenoAst
{
    private static readonly int[] Asteroids =
    [
        SwissEph.SE_CHIRON, SwissEph.SE_PHOLUS, SwissEph.SE_CERES,
        SwissEph.SE_PALLAS, SwissEph.SE_JUNO, SwissEph.SE_VESTA,
    ];

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

    private static readonly (string Name, int Flag)[] TopoIflagVariants =
        [("", 0), ("_SPEED", SwissEph.SEFLG_SPEED)];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Asteroids)
        {
            foreach (var jd in Jds)
            {
                foreach (var (flagName, flag) in IflagCombos)
                {
                    rows.Add(BuildRow("PHA", ipl, jd, flagName, flag, useUt: false));
                    rows.Add(BuildRow("PHAUT", ipl, jd, flagName, flag, useUt: true));
                }
            }

            foreach (var jd in Grids.JdSpread(4))
            {
                foreach (var observer in TopoObservers)
                {
                    foreach (var (variantName, variantFlag) in TopoIflagVariants)
                    {
                        rows.Add(BuildTopoRow($"PHATOPO{variantName}", ipl, jd, observer, variantFlag, useUt: false));
                        rows.Add(BuildTopoRow($"PHAUTTOPO{variantName}", ipl, jd, observer, variantFlag, useUt: true));
                    }
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

    private static string BuildTopoRow(string prefix, int ipl, double jd, (double Lon, double Lat, double Height) observer, int extraFlag, bool useUt)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            swe.swe_set_topo(observer.Lon, observer.Lat, observer.Height);
            var iflag = SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_TOPOCTR | extraFlag;
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
