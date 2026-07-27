using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>swe_cotrans, swe_cotrans_sp, swe_azalt, swe_azalt_rev.</summary>
internal static class CoordHelpers
{
    private static readonly double[] Lons = [0, 90, 180, 270, 359.999];
    private static readonly double[] Lats = [-89, -45, 0, 45, 89];
    private static readonly double[] Epsilons = [23.4392911, 0.0, 40.0];

    public static void AddRows(List<string> rows)
    {
        AddCotrans(rows);
        AddCotransSp(rows);
        AddAzalt(rows);
        AddAzaltRev(rows);
    }

    private static void AddCotrans(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var eps in Epsilons)
                {
                    var caseId = $"CT|{D(lon)}|{D(lat)}|{D(eps)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, 1.0];
                        var xpn = new double[3];
                        swe.swe_cotrans(xpo, xpn, eps);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2])];
                    }));
                }
            }
        }
    }

    private static void AddCotransSp(List<string> rows)
    {
        foreach (var lon in Lons)
        {
            foreach (var lat in Lats)
            {
                foreach (var eps in Epsilons)
                {
                    var caseId = $"CTS|{D(lon)}|{D(lat)}|{D(eps)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        double[] xpo = [lon, lat, 1.0, 1.0, 0.1, 0.01];
                        var xpn = new double[6];
                        swe.swe_cotrans_sp(xpo, xpn, eps);
                        return [D(xpn[0]), D(xpn[1]), D(xpn[2]), D(xpn[3]), D(xpn[4]), D(xpn[5])];
                    }));
                }
            }
        }
    }

    private static readonly double[] AzaltJds = Grids.JdSpread(4);
    private static readonly (string Name, int Flag)[] AzaltDirections =
        [("ECL2HOR", SwissEph.SE_ECL2HOR), ("EQU2HOR", SwissEph.SE_EQU2HOR)];
    private static readonly double[][] Geoposes =
        [[0, 45, 0], [-73.5, 40.7, 10], [0, 89, 0]];
    private static readonly double[][] AzaltXins =
        [[0, 0], [90, 45], [270, -30]];

    private static void AddAzalt(List<string> rows)
    {
        foreach (var jd in AzaltJds)
        {
            foreach (var (dirName, dir) in AzaltDirections)
            {
                foreach (var geopos in Geoposes)
                {
                    foreach (var xin in AzaltXins)
                    {
                        var caseId = $"AZ|{D(jd)}|{dirName}|{D(geopos[0])},{D(geopos[1])},{D(geopos[2])}|{D(xin[0])},{D(xin[1])}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var xaz = new double[3];
                            swe.swe_azalt(jd, dir, geopos, 1013.25, 15.0, xin, xaz);
                            return [D(xaz[0]), D(xaz[1]), D(xaz[2])];
                        }));
                    }
                }
            }
        }
    }

    private static readonly (string Name, int Flag)[] AzaltRevDirections =
        [("HOR2ECL", SwissEph.SE_HOR2ECL), ("HOR2EQU", SwissEph.SE_HOR2EQU)];
    private static readonly double[][] AzaltRevXins =
        [[0, 0], [90, 45], [270, -30]];

    private static void AddAzaltRev(List<string> rows)
    {
        foreach (var jd in AzaltJds)
        {
            foreach (var (dirName, dir) in AzaltRevDirections)
            {
                foreach (var geopos in Geoposes)
                {
                    foreach (var xin in AzaltRevXins)
                    {
                        var caseId = $"AZR|{D(jd)}|{dirName}|{D(geopos[0])},{D(geopos[1])},{D(geopos[2])}|{D(xin[0])},{D(xin[1])}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var xout = new double[2];
                            swe.swe_azalt_rev(jd, dir, geopos, xin, xout);
                            return [D(xout[0]), D(xout[1])];
                        }));
                    }
                }
            }
        }
    }
}
