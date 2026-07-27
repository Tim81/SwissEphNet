using SwissEphNet;

namespace BaselineMatrix;

/// <summary>
/// Shared sweep values used by more than one matrix generator.
/// </summary>
internal static class Grids
{
    // All house-system letters, plus two invalid ones ('Z' and '0') to exercise the
    // default fallback and its serr/deprecation path from an input that was never
    // assigned a house system at all. 'I' and 'i' are distinct (upper = Treindl
    // solution, lower = Makransky solution). 'P' has no case of its own in the
    // switch because the default branch IS the Placidus implementation -- there is
    // no separate "unhandled letter" fallback to fall through to. 'J' has no case
    // either and reaches that same default, which is worth freezing as an explicit,
    // deliberate input rather than an accident nobody is watching.
    public static readonly char[] HouseSystems =
        "ABCDEFGHIiJKLMNOPQRSTUVWXYZ0".ToCharArray();

    // Real mean obliquity, the degenerate eps=0 edge case, and eps=40: not just
    // another arithmetic sample, eps=40 moves the polar-degeneracy boundary for
    // Placidus/Koch to |geolat| = 50, so it puts a different subset of GeoLats
    // (namely the 50s) onto the degenerate branch than the real obliquity does.
    // Dropping it would sample only one branch-selection boundary, not the
    // mechanism that selects the boundary in the first place.
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
        const int steps = 24;
        var values = new double[steps];
        for (var i = 0; i < steps; i++)
        {
            values[i] = i * (360.0 / steps);
        }
        return values;
    }

    // ipl values requested for swe_calc / swe_calc_ut: Sun..Pluto, mean/true node,
    // mean/oscillating apogee, Earth, plus the special SE_ECL_NUT pseudo-body (obliquity
    // and nutation land in xx[0]/xx[1] -- the cheapest way to characterize that code directly).
    public static readonly int[] CalcPlanets =
    [
        SwissEph.SE_SUN, SwissEph.SE_MOON, SwissEph.SE_MERCURY, SwissEph.SE_VENUS,
        SwissEph.SE_MARS, SwissEph.SE_JUPITER, SwissEph.SE_SATURN, SwissEph.SE_URANUS,
        SwissEph.SE_NEPTUNE, SwissEph.SE_PLUTO, SwissEph.SE_MEAN_NODE, SwissEph.SE_TRUE_NODE,
        SwissEph.SE_MEAN_APOG, SwissEph.SE_OSCU_APOG, SwissEph.SE_EARTH, SwissEph.SE_ECL_NUT,
    ];

    // Single flags, plus flag combinations that actually occur together in practice.
    // SEFLG_TOPOCTR is handled as its own pass in Calc.cs, since it only means
    // something after swe_set_topo has been called.
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
        ("NONUT", SwissEph.SEFLG_NONUT),
        ("NOABERR", SwissEph.SEFLG_NOABERR),
        ("NOGDEFL", SwissEph.SEFLG_NOGDEFL),
        ("BARYCTR", SwissEph.SEFLG_BARYCTR),
        ("SIDEREAL", SwissEph.SEFLG_SIDEREAL),
        ("SPEED_EQUATORIAL", SwissEph.SEFLG_SPEED | SwissEph.SEFLG_EQUATORIAL),
        ("SPEED_XYZ", SwissEph.SEFLG_SPEED | SwissEph.SEFLG_XYZ),
        ("J2000_EQUATORIAL", SwissEph.SEFLG_J2000 | SwissEph.SEFLG_EQUATORIAL),
        ("HELCTR_SPEED", SwissEph.SEFLG_HELCTR | SwissEph.SEFLG_SPEED),
    ];

    /// <summary>
    /// <paramref name="count"/> Julian day numbers evenly spread across the
    /// requested range, inclusive of both ends. <paramref name="count"/> must be
    /// at least 2 -- a spread of one point is a contradiction in terms.
    /// </summary>
    public static double[] JdSpread(int count, double lo = 1_000_000, double hi = 2_600_000)
    {
        if (count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "JdSpread needs at least 2 points to span a range.");
        }

        var values = new double[count];
        var step = (hi - lo) / (count - 1);
        for (var i = 0; i < count; i++)
        {
            values[i] = lo + i * step;
        }
        return values;
    }
}
