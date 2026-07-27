using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_house_pos: house position of an ecliptic point, for every house system.</summary>
internal static class HousePos
{
    private static readonly double Eps = 23.4392911;
    private static readonly double[] Armcs = [0, 90, 180, 270];
    private static readonly double[] GeoLats = [-80, -45, 0, 45, 80];
    private static readonly double[] Lons = [0, 90, 180, 270];
    private static readonly double[] Lats = [-5, 0, 5];

    public static void AddRows(List<string> rows)
    {
        foreach (var hsys in Grids.HouseSystems)
        {
            foreach (var armc in Armcs)
            {
                foreach (var geolat in GeoLats)
                {
                    foreach (var lon in Lons)
                    {
                        foreach (var lat in Lats)
                        {
                            rows.Add(BuildRow(hsys, armc, geolat, lon, lat));
                        }
                    }
                }
            }
        }
    }

    private static string BuildRow(char hsys, double armc, double geolat, double lon, double lat)
    {
        var caseId = $"HP|{hsys}|{D(armc)}|{D(geolat)}|{D(lon)}|{D(lat)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var xpin = new double[6];
            xpin[0] = lon;
            xpin[1] = lat;
            string? serr = null;
            var pos = swe.swe_house_pos(armc, geolat, Eps, hsys, xpin, ref serr);
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
