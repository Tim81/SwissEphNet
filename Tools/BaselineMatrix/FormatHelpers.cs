using SwissEphNet;
using static BaselineMatrix.Format;

namespace BaselineMatrix;

/// <summary>
/// swe_split_deg, swe_cs2degstr, swe_cs2timestr, swe_cs2lonlatstr, swe_degnorm,
/// swe_difdeg2n, swe_difdegn, swe_csnorm, swe_deg_midp, swe_rad_midp.
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
}
