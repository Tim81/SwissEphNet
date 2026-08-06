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

    // A small set of center bodies, not the full CalcPlanets list crossed against itself --
    // swe_calc_pctr exists to characterize the planetocentric-recentering mechanism itself
    // (already exercised, single-body, by AddRows above), not to re-sweep every pair.
    private static readonly int[] PctrCenters = [SwissEph.SE_SUN, SwissEph.SE_EARTH, SwissEph.SE_MOON];

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

#if !USE_REFERENCE_PACKAGE
            // swe_calc_pctr does not exist in the reference package (SwissEphNet 2.8.0.2,
            // pre-2.10.03) at all -- reference mode's own "compile-only regression guard"
            // build has no such method to call, so this sweep is local-mode only, the same
            // way Areas.cs already excludes OnLoadFile-dependent setup under this flag.
            foreach (var iplctr in PctrCenters)
            {
                foreach (var jd in Grids.JdSpread(5))
                {
                    foreach (var (flagName, flag) in Grids.CalcIflagCombos)
                    {
                        var iflag = SwissEph.SEFLG_MOSEPH | flag;
                        rows.Add(BuildPctrRow(ipl, iplctr, jd, flagName, iflag));
                    }
                }
            }
#endif
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

    /// <summary>
    /// swe_calc_pctr: planetocentric position of ipl as seen from iplctr instead of the
    /// geocenter. Previously uncovered anywhere in this matrix (docs/known-issues.md, "31 of
    /// 107 public swe_* entry points have no matrix coverage") -- same sweep shape as
    /// swe_calc's own BuildRow above, minus SEFLG_TOPOCTR (which is not meaningful here: the
    /// observer is iplctr, not swe_set_topo's ground station), with iplctr==ipl included
    /// deliberately to exercise the degenerate self-centered case.
    /// </summary>
#if !USE_REFERENCE_PACKAGE
    private static string BuildPctrRow(int ipl, int iplctr, double jd, string flagName, int iflag)
    {
        var caseId = $"PCTR|{I(ipl)}|{I(iplctr)}|{D(jd)}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var xx = new double[6];
            string? serr = null;
            var retc = swe.swe_calc_pctr(jd, ipl, iplctr, iflag, xx, ref serr);

            return
            [
                I(retc),
                D(xx[0]), D(xx[1]), D(xx[2]), D(xx[3]), D(xx[4]), D(xx[5]),
                S(serr),
            ];
        });
    }
#endif

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
