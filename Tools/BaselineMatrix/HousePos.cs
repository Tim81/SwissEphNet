using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_house_pos: house position of an ecliptic point, for every house system.</summary>
internal static class HousePos
{
    // The obliquity this area originally hardcoded (real mean obliquity), before it swept
    // Grids.Eps like every sibling house area (Houses.cs, HousesEx.cs, Gauquelin.cs) does.
    // Kept as the default here, with its case id shape UNCHANGED (no eps field), so this
    // change is purely additive for the existing 10,528 rows: it must never rename,
    // remove, or alter one of them, including the 375 HP|G|* rows independently verified
    // bit-exact against Astrodienst's C in PR #13.
    //
    // Grids.Eps's other two values -- 0.0 (the degenerate obliquity docs/known-issues.md's
    // "swe_houses_armc reports success while emitting NaN cusps" already documents for the
    // sibling houses-armc area) and 40.0 (moves the polar-degeneracy boundary) -- were never
    // swept here even though every sibling area sweeps Grids.Eps. swe_house_pos with
    // hsys='G' only became reachable at all once PR #13 fixed hcusp[36]->[37]: before that,
    // every 'G' case here threw IndexOutOfRangeException regardless of eps, so there was no
    // working Gauquelin path yet to extend when the other areas picked up Grids.Eps. This
    // adds the two missing eps values as new rows (case id carries an explicit eps field),
    // leaving the default sweep's ids untouched.
    private const double DefaultEps = 23.4392911;

    // 37.5 and 123.456 are deliberately non-cardinal: every other armc/longitude
    // here is a multiple of 90, which makes most cases land exactly on a house
    // cusp or a quadrant boundary -- useful for eyeballing, but it means the grid
    // barely samples the general (non-degenerate) code path at all.
    private static readonly double[] Armcs = [0, 37.5, 90, 180, 270];
    private static readonly double[] GeoLats = [-80, -45, 0, 45, 80];
    private static readonly double[] Lons = [0, 90, 123.456, 180, 270];
    private static readonly double[] Lats = [-5, 0, 5];

    public static void AddRows(List<string> rows)
    {
        foreach (var hsys in Grids.HouseSystems)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var armc in Armcs)
                {
                    foreach (var geolat in GeoLats)
                    {
                        foreach (var lon in Lons)
                        {
                            foreach (var lat in Lats)
                            {
                                rows.Add(BuildRow(hsys, eps, armc, geolat, lon, lat));
                            }
                        }
                    }
                }
            }
        }
    }

    private static string BuildRow(char hsys, double eps, double armc, double geolat, double lon, double lat)
    {
        var caseId = eps == DefaultEps
            ? $"HP|{hsys}|{D(armc)}|{D(geolat)}|{D(lon)}|{D(lat)}"
            : $"HP|{hsys}|{D(eps)}|{D(armc)}|{D(geolat)}|{D(lon)}|{D(lat)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var xpin = new double[6];
            xpin[0] = lon;
            xpin[1] = lat;
            string? serr = null;
            var pos = swe.swe_house_pos(armc, geolat, eps, hsys, xpin, ref serr);
            return [D(pos), S(serr)];
        });
    }
}

/// <summary>swe_house_name: display name for every house system letter.</summary>
internal static class HouseName
{
    public static void AddRows(List<string> rows)
    {
        foreach (var hsys in Grids.HouseSystems)
        {
            var caseId = $"HN|{hsys}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [S(swe.swe_house_name(hsys))];
            }));
        }
    }
}
