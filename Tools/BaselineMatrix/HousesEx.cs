using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_houses and swe_houses_ex: unlike swe_houses_armc, these derive armc
/// themselves from tjd_ut via sidereal time and (for swe_houses_ex) nutation, and
/// swe_houses_ex additionally has a SEFLG_SIDEREAL branch that couples house
/// cusps to the current ayanamsa. Both were previously uncovered.
///
/// Kept smaller than the armc sweep -- these exist to characterize the tjd/armc
/// derivation and the sidereal branch, not to re-sweep every geometry again.
/// </summary>
internal static class HousesEx
{
    private static readonly double[] Jds = Grids.JdSpread(4);
    private static readonly double[] GeoLats = [-80, -45, 0, 45, 80];
    private static readonly double[] GeoLons = [-90, 0, 90];

    private static readonly (string Name, int Flag)[] SiderealCombos =
    [
        ("0", 0),
        ("SIDEREAL", SwissEph.SEFLG_SIDEREAL),
        ("SIDEREAL_ECLT0", SwissEph.SEFLG_SIDEREAL | SwissEph.SE_SIDBIT_ECL_T0),
    ];

    public static void AddRows(List<string> rows)
    {
        foreach (var jd in Jds)
        {
            foreach (var geolat in GeoLats)
            {
                foreach (var geolon in GeoLons)
                {
                    foreach (var hsys in Grids.HouseSystems)
                    {
                        rows.Add(BuildHousesRow(jd, geolat, geolon, hsys));
                        foreach (var (flagName, flag) in SiderealCombos)
                        {
                            rows.Add(BuildHousesExRow(jd, geolat, geolon, hsys, flagName, flag));
                        }
                    }
                }
            }
        }
    }

    private static string BuildHousesRow(double jd, double geolat, double geolon, char hsys)
    {
        var caseId = $"HS|{D(jd)}|{D(geolat)}|{D(geolon)}|{hsys}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            var retc = swe.swe_houses(jd, geolat, geolon, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc, serr: null);
        });
    }

    private static string BuildHousesExRow(double jd, double geolat, double geolon, char hsys, string flagName, int flag)
    {
        var caseId = $"HX|{D(jd)}|{D(geolat)}|{D(geolon)}|{hsys}|{flagName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            var retc = swe.swe_houses_ex(jd, flag, geolat, geolon, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc, serr: null);
        });
    }

    private static string[] Fields(int retc, double[] cusp, double[] ascmc, string? serr)
    {
        var fields = new string[1 + 37 + 10 + 1];
        var i = 0;
        fields[i++] = I(retc);
        for (var c = 0; c < 37; c++)
        {
            fields[i++] = D(cusp[c]);
        }
        for (var a = 0; a <= 9; a++)
        {
            fields[i++] = D(ascmc[a]);
        }
        fields[i++] = S(serr);
        return fields;
    }
}
