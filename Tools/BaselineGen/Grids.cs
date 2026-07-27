using SwissEphNet;

namespace BaselineGen;

/// <summary>
/// Shared sweep values used by more than one matrix generator.
/// </summary>
internal static class Grids
{
    // All house-system letters. 'I' and 'i' are distinct (upper = Treindl solution,
    // lower = Makransky solution). 'J' and 'P' are not implemented explicitly and
    // fall through to the default (Placidus) branch -- that fallback is itself
    // behavior worth freezing.
    public static readonly char[] HouseSystems =
        "ABCDEFGHIiJKLMNOPQRSTUVWXY".ToCharArray();

    public static readonly double[] Eps = [23.4392911, 0.0, 40.0];

    // Geographic latitudes: a coarse regular grid plus explicit polar-circle
    // extremes (|lat| > 66) where Placidus and Koch degenerate.
    public static readonly double[] GeoLats =
    [
        -89, -85, -80, -75, -70, -67, -66, -60, -50, -40, -30, -20, -10,
        0,
        10, 20, 30, 40, 50, 60, 66, 67, 70, 75, 80, 85, 89
    ];

    public static readonly double[] Armcs = BuildArmcs();

    private static double[] BuildArmcs()
    {
        var values = new double[40];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i * 9;
        }
        return values;
    }

    // ipl values requested for swe_calc / swe_calc_ut: Sun..Pluto, mean/true node,
    // mean/oscillating apogee, Earth.
    public static readonly int[] CalcPlanets =
    [
        SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_VENUS,
        SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_SATURN, SwissEph.SE_URANUS,
        SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO, SwissEph.SE_MEAN_NODE, SwissEph.SE_TRUE_NODE,
        SwissEph.SE_MEAN_APOG, SwissEph.SE_OSCU_APOG, SwissEph.SE_EARTH
    ];

    public static readonly (string Name, int Flag)[] CalcIflagCombos =
    [
        ("0", 0),
        ("SPEED", SwissEph.SEFLG_SPEED),
        ("EQUATORIAL", SwissEph.SEFLG_EQUATORIAL),
        ("XYZ", SwissEph.SEFLG_XYZ),
        ("J2000", SwissEph.SEFLG_J2000),
        ("HELCTR", SwissEph.SEFLG_HELCTR),
        ("TRUEPOS", SwissEph.SEFLG_TRUEPOS),
        ("RADIANS", SwissEph.SEFLG_RADIANS),
    ];

    /// <summary>
    /// <paramref name="count"/> Julian day numbers evenly spread across the
    /// requested range, inclusive of both ends.
    /// </summary>
    public static double[] JdSpread(int count, double lo = 1_000_000, double hi = 2_600_000)
    {
        var values = new double[count];
        var step = (hi - lo) / (count - 1);
        for (var i = 0; i < count; i++)
        {
            values[i] = lo + i * step;
        }
        return values;
    }
}
