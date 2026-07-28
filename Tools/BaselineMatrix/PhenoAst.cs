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
/// possible.
///
/// COLLAPSED (this change): the original sweep was 6 asteroids x 10 jds x 3 iflag combos
/// x 2 (ET/UT) x 4 topocentric jds x 2 observers x 2 (SPEED/no-SPEED) x 2 (ET/UT) = 552
/// rows, of which only 10 payloads were ever distinct (measured): nine ephemeris-file
/// "not found" messages -- one per 600-year file era the jd sweep crossed -- and one
/// fixed Chiron restricted-range message. Every other axis was pure duplication, because
/// the call always fails at file-open, before iflag, ET/UT conversion, topocentric state,
/// or which of the five non-Chiron bodies is asked, is ever consulted: PHA and PHAUT gave
/// byte-identical rows for the same body/jd; all three iflag combos gave byte-identical
/// rows; PHATOPO and PHATOPO_SPEED gave byte-identical rows (confirmed on all 48 matching
/// cases -- topocentric correction never runs, because the failure happens first).
///
/// What survives, collapsed to exactly what it pins:
/// - <see cref="FilenameBucketJds"/>: one jd per distinct file era (nine, one row each),
///   run against a single representative non-Chiron body (Pholus) with a single iflag and
///   ET (not UT) -- the per-jd/per-era ephemeris filename derivation, exactly where the
///   DIR_GLUE bug lived.
/// - <see cref="ChironBoundaryJds"/>: brackets both edges of the CHIRON_START/CHIRON_END
///   guard (Sweph.cs ~1186) with a day on each side, using Chiron itself. The original
///   jd range (1,000,000-2,600,000) only ever crossed the CHIRON_START edge, by accident,
///   never CHIRON_END (3,419,437.5, past the top of that range); bracketing both edges
///   here characterizes the whole guard deliberately, not just the half the old range
///   happened to reach. The two "restricted" rows (start-1, end+1) are byte-identical
///   duplicates of each other -- the message text carries only the fixed CHIRON_START/
///   CHIRON_END constants, never the queried jd -- and that is unavoidable, not
///   redundancy left in by accident: fewer points would stop bracketing an edge, not
///   remove any further duplication.
///
///   Measured surprise at the CHIRON_END side: CHIRON_END-1 and CHIRON_END themselves
///   (3,419,436.5 / 3,419,437.5) do NOT reach the asteroid file lookup at all. swe_pheno's
///   do_asteroid path calls main_planet(SEI_EARTH) first (Sweph.cs ~1201, "earth and sun
///   are also needed"), and under SEFLG_MOSEPH that fails earlier with "outside Moshier
///   planet range 625000.50 .. 2818000.50" (MOSHPLEPH_START/END, Sweph.h.cs) -- both jds
///   are past 2,818,000.5. So CHIRON_END sits beyond the Moshier Earth-position range this
///   harness can actually reach: the Chiron-specific guard is real and passes at that jd
///   (tjd not greater than CHIRON_END), but nothing downstream of it can succeed under
///   SEFLG_MOSEPH regardless. Only CHIRON_START-1/CHIRON_START/CHIRON_START+1 exercise the asteroid
///   file lookup this area otherwise pins; frozen as observed, not adjusted to force a
///   particular path.
/// </summary>
internal static class PhenoAst
{
    private const double ChironStart = 1967601.5;
    private const double ChironEnd = 3419437.5;

    // One jd inside each of the nine 600-year ephemeris-file eras the matrix's original
    // jd range (1,000,000-2,600,000) crossed -- confirmed empirically against every
    // non-Chiron asteroid: each of these, in any of the original iflag combos, in either
    // PHA or PHAUT form, names a distinct 'seas_NN.se1'/'seasmNN.se1' file in its "not
    // found" message, and no other row in the original 552 added a tenth.
    private static readonly double[] FilenameBucketJds =
    [
        1_000_000, 1_177_777.777777778, 1_355_555.555555556, 1_533_333.333333333,
        1_888_888.888888889, 2_066_666.666666667, 2_244_444.444444444,
        2_422_222.222222222, 2_600_000,
    ];

    private static readonly double[] ChironBoundaryJds =
    [
        ChironStart - 1, ChironStart, ChironStart + 1,
        ChironEnd - 1, ChironEnd, ChironEnd + 1,
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var jd in FilenameBucketJds)
        {
            rows.Add(BuildRow("PHAFILE", SwissEph.SE_PHOLUS, jd));
        }

        foreach (var jd in ChironBoundaryJds)
        {
            rows.Add(BuildRow("PHACHIRON", SwissEph.SE_CHIRON, jd));
        }
    }

    private static string BuildRow(string prefix, int ipl, double jd)
    {
        var caseId = $"{prefix}|{I(ipl)}|{D(jd)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var attr = new double[20];
            string? serr = null;
            var retc = swe.swe_pheno(jd, ipl, SwissEph.SEFLG_MOSEPH, attr, ref serr);
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
