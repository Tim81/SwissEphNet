using SwissEphNet;
using static BaselineGen.Format;

namespace BaselineGen;

/// <summary>
/// Date/time conversions: swe_julday, swe_revjul, swe_date_conversion, swe_deltat,
/// swe_sidtime, swe_sidtime0, swe_time_equ, swe_jdet_to_utc, swe_utc_to_jd.
/// </summary>
internal static class DateTime_
{
    // (year, month, day, hour) tuples spanning proleptic-Julian edge cases, the
    // Julian/Gregorian reform, leap years/centuries, and a wide year range.
    private static readonly (int Y, int M, int D, double H)[] Dates =
    [
        (-4712, 1, 1, 12.0),
        (-100, 3, 15, 0.0),
        (1, 1, 1, 0.0),
        (100, 2, 28, 6.0),
        (1582, 10, 4, 23.999),
        (1582, 10, 15, 0.0),
        (1600, 2, 29, 12.0),
        (1700, 2, 28, 12.0),
        (1900, 1, 1, 0.0),
        (1900, 2, 28, 12.0),
        (1972, 1, 1, 0.0),
        (2000, 1, 1, 12.0),
        (2000, 2, 29, 12.0),
        (2024, 2, 29, 0.0),
        (2026, 7, 27, 12.0),
        (2100, 2, 28, 12.0),
        (2400, 2, 29, 12.0),
        (9999, 12, 31, 23.999),
    ];

    // (year, month, day, hour, minute, second) UTC tuples, including leap-second dates.
    private static readonly (int Y, int M, int D, int H, int Mi, double S)[] UtcDates =
    [
        (2000, 1, 1, 12, 0, 0.0),
        (1972, 6, 30, 23, 59, 59.0),
        (1972, 6, 30, 23, 59, 60.0),
        (2016, 12, 31, 23, 59, 60.0),
        (1582, 10, 4, 23, 59, 59.0),
        (1600, 2, 29, 0, 0, 0.0),
        (1900, 2, 28, 23, 59, 59.999),
        (2024, 2, 29, 12, 30, 45.5),
        (9999, 12, 31, 23, 59, 59.999),
        (-100, 3, 15, 6, 0, 0.0),
    ];

    private static readonly double[] Jds = Grids.JdSpread(15);
    private static readonly double[] RevJulJds = [.. Grids.JdSpread(12), 0.0, 1721425.5, 2299160.5, 2451545.0];
    private static readonly int[] GregFlags = [0, 1];

    public static void AddRows(List<string> rows)
    {
        AddJulday(rows);
        AddRevJul(rows);
        AddDateConversion(rows);
        AddDeltat(rows);
        AddSidtime(rows);
        AddSidtime0(rows);
        AddTimeEqu(rows);
        AddJdetToUtc(rows);
        AddUtcToJd(rows);
    }

    private static void AddJulday(List<string> rows)
    {
        foreach (var (y, m, d, h) in Dates)
        {
            foreach (var greg in GregFlags)
            {
                var caseId = $"JD|{I(y)}|{I(m)}|{I(d)}|{D(h)}|{I(greg)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    var jd = swe.swe_julday(y, m, d, h, greg);
                    return [D(jd)];
                }));
            }
        }
    }

    private static void AddRevJul(List<string> rows)
    {
        foreach (var jd in RevJulJds)
        {
            foreach (var greg in GregFlags)
            {
                var caseId = $"RJ|{D(jd)}|{I(greg)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    int y = 0, m = 0, d = 0;
                    double h = 0;
                    swe.swe_revjul(jd, greg, ref y, ref m, ref d, ref h);
                    return [I(y), I(m), I(d), D(h)];
                }));
            }
        }
    }

    private static void AddDateConversion(List<string> rows)
    {
        char[] calendars = ['g', 'j'];
        foreach (var (y, m, d, h) in Dates)
        {
            foreach (var cal in calendars)
            {
                var caseId = $"DC|{I(y)}|{I(m)}|{I(d)}|{D(h)}|{C(cal)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    double tjd = 0;
                    var retc = swe.swe_date_conversion(y, m, d, h, cal, ref tjd);
                    return [I(retc), D(tjd)];
                }));
            }
        }
    }

    private static void AddDeltat(List<string> rows)
    {
        foreach (var jd in Jds)
        {
            var caseId = $"DT|{D(jd)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [D(swe.swe_deltat(jd))];
            }));
        }
    }

    private static void AddSidtime(List<string> rows)
    {
        foreach (var jd in Jds)
        {
            var caseId = $"ST|{D(jd)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                return [D(swe.swe_sidtime(jd))];
            }));
        }
    }

    private static void AddSidtime0(List<string> rows)
    {
        double[] jds = Grids.JdSpread(8);
        double[] ecls = [23.4392911, 0.0, 20.0];
        double[] nuts = [0.0, 0.002, -0.001];

        foreach (var jd in jds)
        {
            foreach (var ecl in ecls)
            {
                foreach (var nut in nuts)
                {
                    var caseId = $"ST0|{D(jd)}|{D(ecl)}|{D(nut)}";
                    rows.Add(SafeRow(caseId, () =>
                    {
                        using var swe = new SwissEph();
                        return [D(swe.swe_sidtime0(jd, ecl, nut))];
                    }));
                }
            }
        }
    }

    private static void AddTimeEqu(List<string> rows)
    {
        foreach (var jd in Jds)
        {
            var caseId = $"TE|{D(jd)}";
            rows.Add(SafeRow(caseId, () =>
            {
                using var swe = new SwissEph();
                string? serr = null;
                var retc = swe.swe_time_equ(jd, out var e, ref serr);
                return [I(retc), D(e), S(serr)];
            }));
        }
    }

    private static void AddJdetToUtc(List<string> rows)
    {
        foreach (var jd in Grids.JdSpread(12))
        {
            foreach (var greg in GregFlags)
            {
                var caseId = $"JU|{D(jd)}|{I(greg)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    int y = 0, m = 0, d = 0, h = 0, mi = 0;
                    double s = 0;
                    swe.swe_jdet_to_utc(jd, greg, ref y, ref m, ref d, ref h, ref mi, ref s);
                    return [I(y), I(m), I(d), I(h), I(mi), D(s)];
                }));
            }
        }
    }

    private static void AddUtcToJd(List<string> rows)
    {
        foreach (var (y, m, d, h, mi, s) in UtcDates)
        {
            foreach (var greg in GregFlags)
            {
                var caseId = $"UJ|{I(y)}|{I(m)}|{I(d)}|{I(h)}|{I(mi)}|{D(s)}|{I(greg)}";
                rows.Add(SafeRow(caseId, () =>
                {
                    using var swe = new SwissEph();
                    var dret = new double[2];
                    string? serr = null;
                    var retc = swe.swe_utc_to_jd(y, m, d, h, mi, s, greg, dret, ref serr);
                    return [I(retc), D(dret[0]), D(dret[1]), S(serr)];
                }));
            }
        }
    }
}
