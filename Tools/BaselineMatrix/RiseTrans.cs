using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_rise_trans and swe_rise_trans_true_hor across SE_CALC_RISE/SET/MTRANSIT/ITRANSIT,
/// reachable under SEFLG_MOSEPH with no ephemeris file. Nothing else in the matrix calls
/// either function directly (swe_gauquelin_sector calls swe_rise_trans internally for
/// imeth 2-5, but that only exercises RISE/SET with a couple of bit combos -- see
/// Gauquelin.cs). Timed directly against this branch before choosing this grid: a plain
/// RISE/SET/MTRANSIT/ITRANSIT call is sub-millisecond to a couple of milliseconds;
/// swe_rise_trans_true_hor is a bit more, averaging a couple of milliseconds. Both are
/// cheap enough for a reasonably wide sweep.
/// </summary>
internal static class RiseTrans
{
    private const int MOSEPH = SwissEph.SEFLG_MOSEPH;

    private static readonly int[] Bodies =
    [
        SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_VENUS,
        SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_SATURN, SwissEph.SE_URANUS,
        SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO,
    ];

    private static readonly double[] Jds = Grids.JdSpread(5);

    private static readonly (string Name, int Rsmi)[] RsmiTypes =
    [
        ("RISE", SwissEph.SE_CALC_RISE),
        ("SET", SwissEph.SE_CALC_SET),
        ("MTRANSIT", SwissEph.SE_CALC_MTRANSIT),
        ("ITRANSIT", SwissEph.SE_CALC_ITRANSIT),
    ];

    private static readonly (double Lon, double Lat, double Height)[] Observers =
        [(0, 51.5, 0), (-118.24, 34.05, 100)];

    private static readonly (string Name, int Bit)[] RiseBits =
    [
        ("DISC_CENTER", SwissEph.SE_BIT_DISC_CENTER),
        ("DISC_BOTTOM", SwissEph.SE_BIT_DISC_BOTTOM),
        ("NO_REFRACTION", SwissEph.SE_BIT_NO_REFRACTION),
        ("HINDU_RISING", SwissEph.SE_BIT_HINDU_RISING),
        ("CIVIL_TWILIGHT", SwissEph.SE_BIT_CIVIL_TWILIGHT),
        ("NAUTIC_TWILIGHT", SwissEph.SE_BIT_NAUTIC_TWILIGHT),
        ("ASTRO_TWILIGHT", SwissEph.SE_BIT_ASTRO_TWILIGHT),
        ("FIXED_DISC_SIZE", SwissEph.SE_BIT_FIXED_DISC_SIZE),
        ("GEOCTR_NO_ECL_LAT", SwissEph.SE_BIT_GEOCTR_NO_ECL_LAT),
    ];

    public static void AddRows(List<string> rows)
    {
        AddPlainRows(rows);
        AddBitVariantRows(rows);
        AddAtmoVariantRows(rows);
        AddTrueHorRows(rows);
    }

    private static void AddPlainRows(List<string> rows)
    {
        foreach (var ipl in Bodies)
        {
            foreach (var jd in Jds)
            {
                foreach (var (rsmiName, rsmi) in RsmiTypes)
                {
                    foreach (var observer in Observers)
                    {
                        var caseId = $"RT|{I(ipl)}|{D(jd)}|{rsmiName}|{D(observer.Lon)},{D(observer.Lat)},{D(observer.Height)}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                            double tret = 0;
                            string? serr = null;
                            var retc = swe.swe_rise_trans(jd, ipl, null, MOSEPH, rsmi, geopos, 0, 0, ref tret, ref serr);
                            return [I(retc), D(tret), S(serr)];
                        }));
                    }
                }
            }
        }
    }

    private static void AddBitVariantRows(List<string> rows)
    {
        int[] bitBodies = [SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MARS];
        var jds = Grids.JdSpread(3);
        (string Name, int Rsmi)[] baseTypes = [("RISE", SwissEph.SE_CALC_RISE), ("SET", SwissEph.SE_CALC_SET)];
        var observer = Observers[0];

        foreach (var ipl in bitBodies)
        {
            foreach (var jd in jds)
            {
                foreach (var (baseName, baseRsmi) in baseTypes)
                {
                    foreach (var (bitName, bit) in RiseBits)
                    {
                        var caseId = $"RTBIT|{I(ipl)}|{D(jd)}|{baseName}|{bitName}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                            double tret = 0;
                            string? serr = null;
                            var retc = swe.swe_rise_trans(jd, ipl, null, MOSEPH, baseRsmi | bit, geopos, 0, 0, ref tret, ref serr);
                            return [I(retc), D(tret), S(serr)];
                        }));
                    }
                }
            }
        }
    }

    private static void AddAtmoVariantRows(List<string> rows)
    {
        int[] atmoBodies = [SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MARS];
        var jds = Grids.JdSpread(3);
        (double AtPress, double AtTemp)[] atmoCombos = [(0, 0), (900, 25)];
        var observer = Observers[0];

        foreach (var ipl in atmoBodies)
        {
            foreach (var jd in jds)
            {
                foreach (var (atpress, attemp) in atmoCombos)
                {
                    var caseId = $"RTATM|{I(ipl)}|{D(jd)}|{D(atpress)},{D(attemp)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                        double tret = 0;
                        string? serr = null;
                        var retc = swe.swe_rise_trans(jd, ipl, null, MOSEPH, SwissEph.SE_CALC_RISE, geopos, atpress, attemp, ref tret, ref serr);
                        return [I(retc), D(tret), S(serr)];
                    }));
                }
            }
        }
    }

    private static void AddTrueHorRows(List<string> rows)
    {
        int[] trueHorBodies = [SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_JUPITER, SwissEph.SE_PLUTO];
        var jds = Grids.JdSpread(3);
        double[] horHgts = [0, 10, -50];
        var observer = Observers[0];

        foreach (var ipl in trueHorBodies)
        {
            foreach (var jd in jds)
            {
                foreach (var (rsmiName, rsmi) in RsmiTypes)
                {
                    foreach (var horhgt in horHgts)
                    {
                        var caseId = $"RTH|{I(ipl)}|{D(jd)}|{rsmiName}|{D(horhgt)}";
                        rows.Add(SafeRow(caseId, () =>
                        {
                            using var swe = new SwissEph();
                            var geopos = new[] { observer.Lon, observer.Lat, observer.Height };
                            double tret = 0;
                            string? serr = null;
                            var retc = swe.swe_rise_trans_true_hor(jd, ipl, null, MOSEPH, rsmi, geopos, 0, 0, horhgt, ref tret, ref serr);
                            return [I(retc), D(tret), S(serr)];
                        }));
                    }
                }
            }
        }
    }
}
