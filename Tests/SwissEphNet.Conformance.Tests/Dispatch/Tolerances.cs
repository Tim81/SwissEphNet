using System;
using System.Globalization;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Per-quantity comparison tolerances, mirroring external/swisseph/setest/tolerances.c as of
/// v2.10.04 (e308765d; added by upstream commit f952691, "decided 2026-11-22 by Alois, setest
/// maintainer", see setest.md). The structure and names follow the C one for one so that the next
/// upstream change to that file can be diffed against this one.
/// </summary>
/// <remarks>
/// checkpoints.c:129-139 (<c>check_equals_d_internal</c>) calls <c>tolerance_for</c> for every
/// double comparison and uses its result in place of the t.fix precision whenever it is not
/// negative. The harness never has -p parameters, so <c>ctx->precisions.from_parameters</c> is
/// always false and that override always applies.
/// </remarks>
internal static class Tolerances
{
    // tolerances.c:7-18
    public const double TOL_ANGLE_DEG = 3e-8;
    public const double TOL_ANGLE_RAD = TOL_ANGLE_DEG * Math.PI / 180.0;
    public const double TOL_SEARCH_DEG = 1.0 / 3600000.0;
    public const double TOL_DIST_AU = 1e-10;
    public const double TOL_KM = 0.015;
    public const double TOL_SPEED = 1e-9;
    public const double TOL_TIME_DAY = 1e-8;
    public const double TOL_TIME_HOUR = TOL_TIME_DAY * 24.0;
    public const double TOL_TIME_YEAR = TOL_TIME_DAY / 365.25;
    public const double TOL_DIMLESS = 1e-7;
    public const double TOL_HOUSEPOS = 1e-9;
    public const double TOL_RELATIVE = 1e-14;

    // tolerances.c:20-33, "quantity"
    internal enum Quantity
    {
        Q_UNKNOWN,
        Q_ANGLE,        // degrees, or radians with SEFLG_RADIANS
        Q_ANGLE_DEG,    // always degrees
        Q_SEARCH,       // self-check of a search function against its target position, degrees
        Q_DIST,         // AU
        Q_KM,
        Q_SPEED,        // per day
        Q_TIME_DAY,     // Julian day or days
        Q_TIME_HOUR,
        Q_TIME_YEAR,
        Q_DIMLESS,
        Q_HOUSEPOS,
    }

    // tolerances.c:35-45. Split "xxattr[3]" into base "xxattr" and index 3; index -1 if there is none.
    // atoi() parses a leading integer and yields 0 when there is none; names here always carry one.
    internal static int split_name(string name, out string basename)
    {
        var n = 0;
        var idx = -1;
        while (n < name.Length && name[n] != '[')
        {
            n++;
        }

        basename = name[..n];
        if (n < name.Length && name[n] == '[')
        {
            idx = Atoi(name.AsSpan(n + 1));
        }

        return idx;
    }

    private static int Atoi(ReadOnlySpan<char> s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        var start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            i++;
        }

        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            i++;
        }

        return int.TryParse(s[start..i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static bool Is(string basename, string s) => string.Equals(basename, s, StringComparison.Ordinal);

    // tolerances.c:51-58. Position vector as returned by swe_calc(): longitude, latitude, distance and
    // their speeds, or x, y, z and their speeds with SEFLG_XYZ.
    internal static Quantity kind_position(int idx, int iflag)
    {
        if ((iflag & SwissEph.SEFLG_XYZ) != 0)
        {
            return idx <= 2 ? Quantity.Q_DIST : Quantity.Q_SPEED;
        }

        if (idx <= 1)
        {
            return Quantity.Q_ANGLE;
        }

        if (idx == 2)
        {
            return Quantity.Q_DIST;
        }

        return Quantity.Q_SPEED;
    }

    // tolerances.c:60-69. swe_get_orbital_elements(), dret[0..16] (swephprg.md).
    internal static Quantity kind_orbital_element(int idx)
    {
        if (idx == 0 || idx == 15 || idx == 16) return Quantity.Q_DIST;
        if (idx == 1) return Quantity.Q_DIMLESS;
        if (idx >= 2 && idx <= 9) return Quantity.Q_ANGLE_DEG;
        if (idx == 10 || idx == 12) return Quantity.Q_TIME_YEAR;
        if (idx == 11) return Quantity.Q_SPEED;
        if (idx == 13 || idx == 14) return Quantity.Q_TIME_DAY;
        return Quantity.Q_UNKNOWN;
    }

    // tolerances.c:71-77. Eclipse and occultation attributes attr[] (swephprg.md); suite 8,
    // testcases 9 and 10 are lunar eclipses, where attr[3] is not used.
    internal static Quantity kind_eclipse_attr(int tcase, int idx)
    {
        if (idx >= 4 && idx <= 7) return Quantity.Q_ANGLE_DEG;
        if (idx == 3 && tcase != 9 && tcase != 10) return Quantity.Q_KM;
        return Quantity.Q_DIMLESS;
    }

    // tolerances.c:79-83. swe_pheno(), attr[0..4]: phase angle, phase, elongation, apparent diameter, magnitude.
    internal static Quantity kind_pheno(int idx)
    {
        if (idx == 0 || idx == 2 || idx == 3) return Quantity.Q_ANGLE_DEG;
        return Quantity.Q_DIMLESS;
    }

    // tolerances.c:85-120
    internal static Quantity kind_of(int suite, int tcase, string basename, int idx, int iflag)
    {
        if (suite == 10 && (Is(basename, "xcross") || Is(basename, "xx"))) return Quantity.Q_SEARCH;
        if (Is(basename, "xx")) return suite == 6 ? Quantity.Q_ANGLE_DEG : kind_position(idx, iflag);
        if (Is(basename, "xxaphe") || Is(basename, "xxperi") || Is(basename, "xxnasc") || Is(basename, "xxndsc")) return kind_position(idx, iflag);
        if (Is(basename, "xxdret")) return kind_orbital_element(idx);
        if (Is(basename, "dmax") || Is(basename, "dmin") || Is(basename, "dtrue")) return Quantity.Q_DIST;
        if (Is(basename, "xxtret"))
        {
            // suite 9: heliacal_pheno_ut() returns altitudes, vis_limit_mag() a magnitude, altitude and azimuth
            if (suite == 9 && tcase == 4) return Quantity.Q_ANGLE_DEG;
            if (suite == 9 && tcase == 5) return idx == 0 ? Quantity.Q_DIMLESS : Quantity.Q_ANGLE_DEG;
            return Quantity.Q_TIME_DAY;
        }

        if (Is(basename, "tret")) return Quantity.Q_TIME_DAY;
        if (Is(basename, "xxgeopos")) return Quantity.Q_ANGLE_DEG;
        if (Is(basename, "xxattr")) return kind_eclipse_attr(tcase, idx);
        if (Is(basename, "mag")) return Quantity.Q_DIMLESS;
        if (Is(basename, "daya") || Is(basename, "daya_ut") || Is(basename, "daya_ex") || Is(basename, "daya_ex_ut")) return Quantity.Q_ANGLE_DEG;
        if (Is(basename, "jd") || Is(basename, "jd_ut") || Is(basename, "deltat") || Is(basename, "E")) return Quantity.Q_TIME_DAY;
        if (Is(basename, "tjd_lat") || Is(basename, "tjd_lmt") || Is(basename, "jx")) return Quantity.Q_TIME_DAY;
        if (Is(basename, "ut")) return Quantity.Q_TIME_HOUR;  // swe_revjul() returns hours
        if (Is(basename, "cusps") || Is(basename, "ascmc")) return Quantity.Q_ANGLE;
        if (Is(basename, "cusp_speed") || Is(basename, "ascmc_speed")) return Quantity.Q_SPEED;
        if (Is(basename, "armc") || Is(basename, "xlon") || Is(basename, "xlat")) return Quantity.Q_ANGLE_DEG;
        if (Is(basename, "hp") || Is(basename, "gp")) return Quantity.Q_HOUSEPOS;

        // suite 11 (2026-11-22)
        if (Is(basename, "xpheno")) return kind_pheno(idx);
        if (Is(basename, "xaz") || Is(basename, "alt") || Is(basename, "xrefr")) return Quantity.Q_ANGLE_DEG;
        if (Is(basename, "xpn") || Is(basename, "xback"))
        {
            // swe_cotrans(_sp)(): longitude, latitude, distance and their speeds, in degrees;
            // swe_azalt_rev(): longitude and latitude or right ascension and declination
            if (idx <= 1) return Quantity.Q_ANGLE_DEG;
            if (idx == 2) return Quantity.Q_DIST;
            return Quantity.Q_SPEED;
        }

        return Quantity.Q_UNKNOWN;
    }

    /// <summary>
    /// tolerances.c:122-145. Tolerance for a compared value, or -1 if its kind of quantity is unknown.
    /// <paramref name="iflag"/> is the iteration's "iflag" entry as get_entry("iflag", I, ctx) finds it
    /// (0 if t.exp has none).
    /// </summary>
    internal static double tolerance_for(string name, double exp, int suite, int tcase, int iflag)
    {
        var idx = split_name(name, out var basename);
        double tol;
        switch (kind_of(suite, tcase, basename, idx, iflag))
        {
            case Quantity.Q_ANGLE: tol = (iflag & SwissEph.SEFLG_RADIANS) != 0 ? TOL_ANGLE_RAD : TOL_ANGLE_DEG; break;
            case Quantity.Q_ANGLE_DEG: tol = TOL_ANGLE_DEG; break;
            case Quantity.Q_SEARCH: tol = TOL_SEARCH_DEG; break;
            case Quantity.Q_DIST: tol = TOL_DIST_AU; break;
            case Quantity.Q_KM: tol = TOL_KM; break;
            case Quantity.Q_SPEED: tol = TOL_SPEED; break;
            case Quantity.Q_TIME_DAY: tol = TOL_TIME_DAY; break;
            case Quantity.Q_TIME_HOUR: tol = TOL_TIME_HOUR; break;
            case Quantity.Q_TIME_YEAR: tol = TOL_TIME_YEAR; break;
            case Quantity.Q_DIMLESS: tol = TOL_DIMLESS; break;
            case Quantity.Q_HOUSEPOS: tol = TOL_HOUSEPOS; break;
            default: return -1;
        }

        if (TOL_RELATIVE * Math.Abs(exp) > tol) tol = TOL_RELATIVE * Math.Abs(exp);
        return tol;
    }
}
