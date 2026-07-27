using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_split_deg, swe_cs2degstr, swe_cs2timestr, swe_cs2lonlatstr, swe_degnorm,
/// swe_difdeg2n, swe_difdegn, swe_csnorm, swe_deg_midp, swe_rad_midp, and their
/// pure-function siblings swe_radnorm, swe_difrad2n, swe_difcsn, swe_difcs2n,
/// swe_csroundsec, swe_d2l, swe_day_of_week -- added alongside the degree/centisec
/// versions above specifically so leaving them out stays a deliberate choice
/// rather than an arbitrary gap.
/// </summary>
internal static class FormatHelpers
{
    public static void AddRows(List<string> rows)
    {
        AddSplitDeg(rows);
        AddCs2DegStr(rows);
        AddCs2TimeStr(rows);
        AddCs2LonLatStr(rows);
        AddDegNorm(rows);
        AddDifDeg(rows);
        AddCsNorm(rows);
        AddMidp(rows);
        AddRadNorm(rows);
        AddDifRad2n(rows);
        AddDifCsn(rows);
        AddCsRoundSec(rows);
        AddD2l(rows);
        AddDayOfWeek(rows);
    }

    private static readonly double[] SplitDegValues =
        [0, 15.5, 29.999999, 30.0000001, 45.123456, 89.9999999, 180.5, 270.75, 359.9999999, -10, 400];

    private static readonly (string Name, int Flag)[] SplitDegRoundFlags =
    [
        ("0", 0),
        ("ROUND_SEC", SwissEph.SE_SPLIT_DEG_ROUND_SEC),
        ("ROUND_MIN", SwissEph.SE_SPLIT_DEG_ROUND_MIN),
        ("ROUND_DEG", SwissEph.SE_SPLIT_DEG_ROUND_DEG),
        ("ZODIACAL", SwissEph.SE_SPLIT_DEG_ZODIACAL),
        ("ZODIACAL_ROUND_SEC", SwissEph.SE_SPLIT_DEG_ZODIACAL | SwissEph.SE_SPLIT_DEG_ROUND_SEC),
        ("KEEP_SIGN", SwissEph.SE_SPLIT_DEG_KEEP_SIGN),
        ("KEEP_DEG", SwissEph.SE_SPLIT_DEG_KEEP_DEG),
        ("NAKSHATRA", SwissEph.SE_SPLIT_DEG_NAKSHATRA),
    ];

    private static void AddSplitDeg(List<string> rows)
    {
        foreach (var ddeg in SplitDegValues)
        {
            foreach (var (flagName, flag) in SplitDegRoundFlags)
            {
                var caseId = $"SD|{D(ddeg)}|{flagName}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    swe.swe_split_deg(ddeg, flag, out var ideg, out var imin, out var isec, out var dsecfr, out var isgn);
                    return [I(ideg), I(imin), I(isec), D(dsecfr), I(isgn)];
                }));
            }
        }
    }

    private static readonly int[] Cs2Values =
        [0, 1, 3_599_999, 43_200_000, 129_599_999, -1, -3_599_999, 64_800_000];

    private static void AddCs2DegStr(List<string> rows)
    {
        foreach (var t in Cs2Values)
        {
            var caseId = $"CD|{I(t)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [S(swe.swe_cs2degstr(t))];
            }));
        }
    }

    private static readonly char[] TimeSeps = [':', '.'];
    private static readonly bool[] SuppressZeroValues = [true, false];

    private static void AddCs2TimeStr(List<string> rows)
    {
        foreach (var t in Cs2Values)
        {
            foreach (var sep in TimeSeps)
            {
                foreach (var suppressZero in SuppressZeroValues)
                {
                    var caseId = $"CTM|{I(t)}|{C(sep)}|{B(suppressZero)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        return [S(swe.swe_cs2timestr(t, sep, suppressZero))];
                    }));
                }
            }
        }
    }

    private static readonly (char P, char M)[] LonLatChars = [('N', 'S'), ('E', 'W')];

    private static void AddCs2LonLatStr(List<string> rows)
    {
        foreach (var t in Cs2Values)
        {
            foreach (var (pchar, mchar) in LonLatChars)
            {
                var caseId = $"CLL|{I(t)}|{C(pchar)}{C(mchar)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    return [S(swe.swe_cs2lonlatstr(t, pchar, mchar))];
                }));
            }
        }
    }

    private static readonly double[] DegNormValues =
        [-720, -361, -360, -359.999, -180, -0.0001, 0, 0.0001, 180, 359.999, 360, 360.0001, 720, 123456.789, -123456.789];

    private static void AddDegNorm(List<string> rows)
    {
        foreach (var x in DegNormValues)
        {
            var caseId = $"DN|{D(x)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [D(swe.swe_degnorm(x))];
            }));
        }
    }

    private static readonly double[] PairValues = [0, 90, 180, 270, 359, -30, 400];

    private static void AddDifDeg(List<string> rows)
    {
        foreach (var p1 in PairValues)
        {
            foreach (var p2 in PairValues)
            {
                rows.Add(SafeRow($"D2N|{D(p1)}|{D(p2)}", () =>
                {
                    using var swe = new SwissEph();
                    return [D(swe.swe_difdeg2n(p1, p2))];
                }));
                rows.Add(SafeRow($"DGN|{D(p1)}|{D(p2)}", () =>
                {
                    using var swe = new SwissEph();
                    return [D(swe.swe_difdegn(p1, p2))];
                }));
            }
        }
    }

    private static readonly int[] CsNormValues =
        [0, 1, -1, 129_600_000, -129_600_000, 64_800_000, 200_000_000, -500_000];

    private static void AddCsNorm(List<string> rows)
    {
        foreach (var p in CsNormValues)
        {
            var caseId = $"CN|{I(p)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [I(swe.swe_csnorm(p))];
            }));
        }
    }

    private static void AddMidp(List<string> rows)
    {
        foreach (var x1 in PairValues)
        {
            foreach (var x0 in PairValues)
            {
                rows.Add(SafeRow($"DM|{D(x1)}|{D(x0)}", () =>
                {
                    using var swe = new SwissEph();
                    return [D(swe.swe_deg_midp(x1, x0))];
                }));
                rows.Add(SafeRow($"RM|{D(x1)}|{D(x0)}", () =>
                {
                    using var swe = new SwissEph();
                    return [D(swe.swe_rad_midp(x1, x0))];
                }));
            }
        }
    }

    // Radian-scale mirror of DegNormValues: same shape (near zero, near the wrap
    // point at 2*PI, negative, and a large out-of-range value), scaled to radians.
    private static readonly double[] RadValues =
        [-4 * Math.PI, -Math.PI, -0.0001, 0, 0.0001, Math.PI, 2 * Math.PI, 2 * Math.PI + 0.0001, 10, -10];

    private static void AddRadNorm(List<string> rows)
    {
        foreach (var x in RadValues)
        {
            var caseId = $"RN|{D(x)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [D(swe.swe_radnorm(x))];
            }));
        }
    }

    private static void AddDifRad2n(List<string> rows)
    {
        foreach (var p1 in RadValues)
        {
            foreach (var p2 in RadValues)
            {
                var caseId = $"R2N|{D(p1)}|{D(p2)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    return [D(swe.swe_difrad2n(p1, p2))];
                }));
            }
        }
    }

    private static void AddDifCsn(List<string> rows)
    {
        foreach (var p1 in CsNormValues)
        {
            foreach (var p2 in CsNormValues)
            {
                rows.Add(SafeRow($"DCN|{I(p1)}|{I(p2)}", () =>
                {
                    using var swe = new SwissEph();
                    return [I(swe.swe_difcsn(p1, p2))];
                }));
                rows.Add(SafeRow($"DC2N|{I(p1)}|{I(p2)}", () =>
                {
                    using var swe = new SwissEph();
                    return [I(swe.swe_difcs2n(p1, p2))];
                }));
            }
        }
    }

    private static void AddCsRoundSec(List<string> rows)
    {
        foreach (var p in CsNormValues)
        {
            var caseId = $"CRS|{I(p)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [I(swe.swe_csroundsec(p))];
            }));
        }
    }

    // Moderate values including .5 rounding-edge cases; swe_d2l does no overflow
    // check, so this deliberately avoids values large enough to overflow Int32,
    // which would just be testing undefined behavior rather than the function.
    private static readonly double[] D2lValues =
        [0, 0.4, 0.5, 0.6, -0.4, -0.5, -0.6, 123456.789, -123456.789, 2_000_000_000.4];

    private static void AddD2l(List<string> rows)
    {
        // swe_d2l is static -- no SwissEph instance needed or created.
        foreach (var x in D2lValues)
        {
            var caseId = $"D2L|{D(x)}";
            rows.Add(SafeRow(caseId, () => [I(SwissEph.swe_d2l(x))]));
        }
    }

    private static void AddDayOfWeek(List<string> rows)
    {
        foreach (var jd in Grids.JdSpread(10))
        {
            var caseId = $"DOW|{D(jd)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [I(swe.swe_day_of_week(jd))];
            }));
        }
    }
}
