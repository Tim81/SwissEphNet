using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_houses and swe_houses_ex: unlike swe_houses_armc, these derive armc
/// themselves from tjd_ut via sidereal time and (for swe_houses_ex) nutation, and
/// swe_houses_ex additionally has a SEFLG_SIDEREAL branch that couples house
/// cusps to the current ayanamsa, selected via swe_set_sid_mode -- not by OR-ing
/// SE_SIDBIT_ECL_T0/SE_SIDBIT_SSY_PLANE into iflag. (SE_SIDBIT_ECL_T0 is 256, the
/// same bit as SEFLG_SPEED; OR-ing it into iflag does not select the ECL_T0
/// projection at all, it silently turns speed on. Ayanamsa.cs's SIDBIT rows already
/// pass the bits through sid_mode correctly -- this matches that.) Both swe_houses
/// and swe_houses_ex were previously uncovered.
///
/// Kept smaller than the armc sweep -- these exist to characterize the tjd/armc
/// derivation and the sidereal branch, not to re-sweep every geometry again.
/// </summary>
internal static class HousesEx
{
    private static readonly double[] Jds = Grids.JdSpread(4);
    private static readonly double[] GeoLats = [-80, -45, 0, 45, 80];
    private static readonly double[] GeoLons = [-90, 0, 90];

    // A couple of sidereal modes so the sidereal rows sample more than whatever the
    // library defaults to when no mode has ever been set (Fagan/Bradley).
    private static readonly int[] SiderealModes = [SwissEph.SE_SIDM_FAGAN_BRADLEY, SwissEph.SE_SIDM_LAHIRI];

    private static readonly (string Name, int Bit)[] SidBits =
    [
        ("", 0),
        ("_ECLT0", SwissEph.SE_SIDBIT_ECL_T0),
        ("_SSYPLANE", SwissEph.SE_SIDBIT_SSY_PLANE),
    ];

    private readonly record struct Variant(string Name, bool Sidereal, int SidMode);

    private static readonly Variant[] Variants = BuildVariants();

    private static Variant[] BuildVariants()
    {
        var list = new List<Variant> { new("PLAIN", false, 0) };
        foreach (var sidMode in SiderealModes)
        {
            foreach (var (bitName, bit) in SidBits)
            {
                list.Add(new($"SIDEREAL_{sidMode}{bitName}", true, sidMode | bit));
            }
        }
        return [.. list];
    }

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
                        foreach (var variant in Variants)
                        {
                            rows.Add(BuildHousesExRow(jd, geolat, geolon, hsys, variant));
                            rows.Add(BuildHousesEx2Row(jd, geolat, geolon, hsys, variant));
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
            return Fields(retc, cusp, ascmc);
        });
    }

    private static string BuildHousesExRow(double jd, double geolat, double geolon, char hsys, Variant variant)
    {
        var caseId = $"HX|{D(jd)}|{D(geolat)}|{D(geolon)}|{hsys}|{variant.Name}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            if (variant.Sidereal)
            {
                swe.swe_set_sid_mode(variant.SidMode, 0, 0);
            }
            var iflag = variant.Sidereal ? SwissEph.SEFLG_SIDEREAL : 0;
            var cusp = new double[40];
            var ascmc = new double[10];
            var retc = swe.swe_houses_ex(jd, iflag, geolat, geolon, hsys, cusp, ascmc);
            return Fields(retc, cusp, ascmc);
        });
    }

    private static string[] Fields(int retc, double[] cusp, double[] ascmc)
    {
        var fields = new string[1 + 37 + 10];
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
        return fields;
    }

    /// <summary>
    /// swe_houses_ex2: the same tjd/armc-derivation and sidereal-branch coverage as
    /// BuildHousesExRow above, plus the cusp_speed/ascmc_speed out-parameters swe_houses_ex
    /// has no way to request. Previously uncovered anywhere in this matrix
    /// (docs/known-issues.md, "31 of 107 public swe_* entry points have no matrix coverage").
    /// Passing non-null cusp_speed/ascmc_speed arrays turns on speed computation the same way
    /// AddArmcEx2Rows in Houses.cs does for swe_houses_armc_ex2.
    /// </summary>
    private static string BuildHousesEx2Row(double jd, double geolat, double geolon, char hsys, Variant variant)
    {
        var caseId = $"HX2|{D(jd)}|{D(geolat)}|{D(geolon)}|{hsys}|{variant.Name}";
        return SafeRow(caseId, () =>
        {
            using var swe = new SwissEph();
            if (variant.Sidereal)
            {
                swe.swe_set_sid_mode(variant.SidMode, 0, 0);
            }
            var iflag = variant.Sidereal ? SwissEph.SEFLG_SIDEREAL : 0;
            var cusp = new double[40];
            var ascmc = new double[10];
            var cuspSpeed = new double[40];
            var ascmcSpeed = new double[10];
            string? serr = null;
            var retc = swe.swe_houses_ex2(jd, iflag, geolat, geolon, hsys, cusp, ascmc, cuspSpeed, ascmcSpeed, ref serr);
            return FieldsWithSpeed(retc, cusp, ascmc, cuspSpeed, ascmcSpeed);
        });
    }

    private static string[] FieldsWithSpeed(int retc, double[] cusp, double[] ascmc, double[] cuspSpeed, double[] ascmcSpeed)
    {
        var fields = new string[1 + 37 + 10 + 37 + 10];
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
        for (var c = 0; c < 37; c++)
        {
            fields[i++] = D(cuspSpeed[c]);
        }
        for (var a = 0; a <= 9; a++)
        {
            fields[i++] = D(ascmcSpeed[a]);
        }
        return fields;
    }
}
