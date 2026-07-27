using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_houses_armc: pure computation, no ephemeris or file dependency, so this is
/// the densest matrix in the harness. Every row uses a brand new SwissEph instance
/// -- swe_houses_armc keeps a hidden field (saved_sundec) that emulates a C static
/// and would otherwise make hsys 'I' depend on call order.
///
/// Every row records cusp[0..36], not just cusp[0..12]: for hsys 'G' (Gauquelin
/// sectors) the library writes all 36 sectors, and that range is otherwise
/// completely invisible to the baseline. For every other house system, cusp[13..36]
/// are left at their zero-initialized default and simply pad the row out -- a fixed
/// column count keeps the file mechanically diffable across house systems.
/// </summary>
internal static class Houses
{
    private const int CuspCount = 37; // cusp[0..36]

    public static void AddRows(List<string> rows)
    {
        foreach (var hsys in Grids.HouseSystems)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var geolat in Grids.GeoLats)
                {
                    foreach (var armc in Grids.Armcs)
                    {
                        rows.Add(BuildRow(hsys, eps, geolat, armc));
                    }
                }
            }
        }
    }

    private static string BuildRow(char hsys, double eps, double geolat, double armc)
    {
        var caseId = $"H|{hsys}|{D(eps)}|{D(geolat)}|{D(armc)}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc);
        });
    }

    /// <summary>
    /// Dedicated coverage for the saved_sundec hazard: hsys 'I'/'i' with
    /// ascmc[9] pre-set to the sentinel 99 (use previous state) versus a real
    /// declination. On a fresh instance saved_sundec always starts at 99, so the
    /// sentinel case is deterministic here -- but it is exactly the input shape
    /// that would NOT be reproducible if instances were reused across rows.
    /// </summary>
    public static void AddSunshineStateRows(List<string> rows)
    {
        char[] sunHsys = ['I', 'i'];
        double[] geolats = [-80, -60, -30, 0, 30, 60, 80];
        double[] armcs = [0, 45, 90, 135, 180, 225, 270, 315];
        (string Name, double Value)[] ascmc9Inits = [("sentinel99", 99), ("dec15_5", 15.5), ("decNeg20", -20.0)];

        foreach (var hsys in sunHsys)
        {
            foreach (var eps in Grids.Eps)
            {
                foreach (var geolat in geolats)
                {
                    foreach (var armc in armcs)
                    {
                        foreach (var (name, value) in ascmc9Inits)
                        {
                            rows.Add(BuildSunshineRow(hsys, eps, geolat, armc, name, value));
                        }
                    }
                }
            }
        }
    }

    private static string BuildSunshineRow(char hsys, double eps, double geolat, double armc, string initName, double ascmc9Init)
    {
        var caseId = $"HSUN|{hsys}|{D(eps)}|{D(geolat)}|{D(armc)}|{initName}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            var cusp = new double[40];
            var ascmc = new double[10];
            ascmc[9] = ascmc9Init;
            var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc);
        });
    }

    private static string[] Fields(int retc, double[] cusp, double[] ascmc)
    {
        var fields = new string[1 + CuspCount + 10];
        var i = 0;
        fields[i++] = I(retc);
        for (var c = 0; c < CuspCount; c++)
        {
            fields[i++] = D(cusp[c]);
        }
        for (var a = 0; a <= 9; a++)
        {
            fields[i++] = D(ascmc[a]);
        }
        return fields;
    }
}
