using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_gauquelin_sector, reachable under SEFLG_MOSEPH with no ephemeris file for the
/// classical bodies (Sun, Moon, Mercury..Pluto). imeth 0 ("with latitude") and imeth 1
/// ("without latitude") both call swe_house_pos with hsys 'G' (36 Gauquelin sectors).
/// That used to throw IndexOutOfRangeException for every classical-body input tried
/// here -- SweHouse.cs's cusp buffer for hsys 'G' was undersized (hcusp[36] instead of
/// the upstream swehouse.c:2224 hcusp[37]) -- but the fix landed (see
/// Tests/baseline/baseline-2.8.0.2.env.txt's local-regenerations log, entry 5): the
/// committed baseline has computed Gauquelin house positions here, not EXCEPTION rows,
/// and this doc comment previously still described the pre-fix behavior. imeth 2-5
/// (from rise/set, with and without refraction) go through swe_rise_trans instead and
/// never hit that bug either way; they are measurably slower than imeth 0/1 but still
/// sub-millisecond on average (timed directly against this branch).
///
/// SE_CHIRON and SE_CERES in Bodies do NOT hit the hsys 'G' bug at any imeth: swe_calc
/// for a minor planet always requires a file (see PhenoAst.cs's doc comment for the
/// full explanation), so swe_gauquelin_sector's own swe_calc/swe_rise_trans call for
/// these two bodies fails first, before reaching swe_house_pos at all. They are included
/// anyway to freeze that uniform "no ephemeris file" error across every imeth and the
/// starname branch, not to exercise the 'G' bug a second time.
/// </summary>
internal static class Gauquelin
{
    private const int MOSEPH = SwissEph.SEFLG_MOSEPH;

    private static readonly int[] Bodies =
    [
        SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_VENUS,
        SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_SATURN, SwissEph.SE_URANUS,
        SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO, SwissEph.SE_CHIRON, SwissEph.SE_CERES,
    ];

    private static readonly double[] Jds = Grids.JdSpread(4);

    // 0 = with latitude, 1 = without latitude (both hit the hsys 'G' bug -- see above),
    // 2 = from rise/set, 3 = from rise/set with refraction, 4/5 = the same two methods
    // without/with the no-refraction bit permutation SweCL.cs actually implements for
    // imeth in [2,5] (see the risemeth bit derivation in swe_gauquelin_sector).
    private static readonly int[] Imeths = [0, 1, 2, 3, 4, 5];

    private static readonly (double Lon, double Lat, double Height)[] Observers =
        [(0, 51.5, 0), (-118.24, 34.05, 100)];

    public static void AddRows(List<string> rows)
    {
        foreach (var ipl in Bodies)
        {
            foreach (var jd in Jds)
            {
                foreach (var imeth in Imeths)
                {
                    foreach (var observer in Observers)
                    {
                        rows.Add(BuildRow(ipl, null, jd, imeth, observer, 0, 0));
                    }
                }
            }
        }

        AddAtmoRows(rows);
        AddStarnameRows(rows);
        AddInvalidMethodRows(rows);
    }

    private static void AddAtmoRows(List<string> rows)
    {
        // (0, 0) is deliberately not repeated here: it is exactly what the main sweep
        // above already passes for every body/jd/observer at imeth=3, so including it
        // would only reproduce those exact case ids. Only the non-default combo is new.
        int[] atmoBodies = [SwissEph.SE_SUN, SwissEph.SE_MARS];
        var jds = Grids.JdSpread(2);
        (double AtPress, double AtTemp)[] atmoCombos = [(900, 25)];
        var observer = Observers[0];

        foreach (var ipl in atmoBodies)
        {
            foreach (var jd in jds)
            {
                foreach (var (atpress, attemp) in atmoCombos)
                {
                    rows.Add(BuildRow(ipl, null, jd, 3, observer, atpress, attemp));
                }
            }
        }
    }

    private static void AddStarnameRows(List<string> rows)
    {
        // No sefstars.txt is loaded (SwissEph.DefaultFileProvider is a no-op provider, set
        // once by Tools/BaselineMatrix/Areas.cs, so no instance in this matrix can reach a
        // real file), so this exercises the star-not-found error path, not a real star position.
        var observer = Observers[0];
        foreach (var imeth in Imeths)
        {
            rows.Add(BuildRow(0, "Aldebaran", Jds[0], imeth, observer, 0, 0));
        }
    }

    private static void AddInvalidMethodRows(List<string> rows)
    {
        var observer = Observers[0];
        foreach (var imeth in new[] { -1, 6 })
        {
            rows.Add(BuildRow(SwissEph.SE_SUN, null, Jds[0], imeth, observer, 0, 0));
        }
    }

    private static string BuildRow(int ipl, string? starname, double jd, int imeth, (double Lon, double Lat, double Height) observer, double atpress, double attemp)
    {
        var starLabel = starname ?? I(ipl);
        var caseId = $"GQ|{starLabel}|{D(jd)}|{I(imeth)}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}|{D(atpress)},{D(attemp)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
            double dgsect = 0;
            string? serr = null;
            var retc = swe.swe_gauquelin_sector(jd, ipl, starname, MOSEPH, imeth, geopos, atpress, attemp, ref dgsect, ref serr);
            return [I(retc), D(dgsect), S(serr)];
        });
    }
}
