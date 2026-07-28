using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_calc / swe_calc_ut with iflag combinations that do NOT force SEFLG_MOSEPH, exercising
/// plaus_iflag's ephemeris-defaulting branch (CPort/Sweph.cs:6698-6699: epheflag == 0 ->
/// SEFLG_DEFAULTEPH, i.e. SwissEph.SEFLG_SWIEPH) -- the one branch Calc.cs's own sweep never
/// reaches. Grids.CalcIflagCombos includes ("0", 0), a nominal no-flags case meant to exercise
/// exactly this default, but Calc.cs ORs SwissEph.SEFLG_MOSEPH into every combination
/// unconditionally (see its BuildRow/BuildTopoRow), so calc's "0" row is really SEFLG_MOSEPH
/// under a misleading name and the defaulting branch is dead code as far as the baseline is
/// concerned. Confirmed: a red-team change to plaus_iflag's SEFLG_DEFAULTEPH from SEFLG_SWIEPH
/// to SEFLG_JPLEPH left scripts/verify-baseline.ps1 100% EXACT on the (existing) calc area.
///
/// With no OnLoadFile handler ever subscribed (see Tools/BaselineGen/Program.cs), SEFLG_SWIEPH
/// cannot find its ephemeris files and falls back to Moshier internally, emitting a diagnostic
/// in serr -- that fallback path, and the exact serr text it produces, is itself worth
/// freezing: a change to the default ephemeris flag, or to how the missing-file fallback is
/// reported, would otherwise pass unnoticed.
///
/// Deliberately a new area, not an addition to Calc.cs: the eleven original golden files
/// (houses-armc, houses, house-pos, calc, pheno, nodaps, ayanamsa, datetime, coord, format,
/// misc) are package-seeded from the published 2.8.0.2 assembly and must stay byte-identical
/// -- see Tests/baseline/baseline-2.8.0.2.env.txt's "Area provenance" table. Follows the
/// pattern of the seven "local-(short commit sha)" areas added in fa34326.
/// </summary>
internal static class CalcDefaultEph
{
    private static readonly double[] Jds = Grids.JdSpread(5);

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Grids.CalcPlanets)
        {
            foreach (var jd in Jds)
            {
                foreach (var (flagName, flag) in Grids.CalcIflagCombos)
                {
                    rows.Add(BuildRow("CDEF", ipl, jd, flagName, flag, useUt: false));
                    rows.Add(BuildRow("CUDEF", ipl, jd, flagName, flag, useUt: true));
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
