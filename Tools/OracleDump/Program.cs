// The .NET side of the bit-exact comparison harness's first stage. Reads
// Tools/OracleGrid/grid-analytic.tsv (see that file's own header for the column layout, the case
// for a shared grid, and why 'J' is absent from the house-system letters) and replays every row
// against this port, printing each result's raw IEEE-754 bit pattern so
// scripts/run-oracle-dump.ps1 can queue it up against Tools/CReference/sedump.c's C output for a
// later, separate comparison pass. sedump.c's own header documents the exact output shape this
// file mirrors.
//
// INVOCATION
//
//   OracleDump.exe <grid.tsv> <output.tsv>
//
// A FRESH SwissEph INSTANCE PER ROW
//
// swe_houses_armc carries a hidden field emulating a C static (saved_sundec) that changes hsys
// 'I'/'i' results depending on what a PRIOR call computed on the SAME instance -- see
// Tools/BaselineGen/Program.cs's header and SwissEphNet/CPort/SweHouse.cs. Reusing one instance
// across rows would make this driver disagree with sedump.c (which calls swe_close() before
// every row) for a reason that has nothing to do with the port, so a brand new SwissEph is
// constructed for every row here too.
//
// OUTPUT COLUMN LAYOUT
//
// One line per data row, tab separated: case_id, retc, err, then every double the row's func
// returns as a (decimal, hex) pair -- CALC/CALC_UT emit xx[0..5] (6 doubles); HOUSES/HOUSES_ARMC
// emit cusp[0..36] then ascmc[0..9] (47 doubles), a fixed width across every house system even
// though only cusp[1..12] (or cusp[1..36] for hsys 'G') are ever populated. See sedump.c's
// header for why the width is fixed this way and why retc/err come before the doubles.

using System.Globalization;
using SwissEphNet;

namespace OracleDump;

internal static class Program
{
    private const int ExpectedColumns = 12;
    private const int CuspCount = 37; // cusp[0..36]
    private const int AscmcCount = 10; // ascmc[0..9]

    private static readonly string ExpectedHeader = string.Join('\t',
        "case_id", "func", "ipl", "tjd", "iflag", "hsys", "geolon", "geolat", "height", "armc", "eps", "sid_mode");

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: OracleDump <grid.tsv> <output.tsv>");
            return 1;
        }

        var gridPath = args[0];
        var outputPath = args[1];

        using var reader = new StreamReader(gridPath);
        using var writer = new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        var headerSeen = false;
        var rowCount = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            if (line[0] == '#')
            {
                continue;
            }

            if (!headerSeen)
            {
                if (line != ExpectedHeader)
                {
                    throw new InvalidDataException(
                        $"grid header does not match what this driver expects.\nexpected: {ExpectedHeader}\ngot:      {line}");
                }
                headerSeen = true;
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != ExpectedColumns)
            {
                throw new InvalidDataException($"row has {fields.Length} column(s), expected {ExpectedColumns}: {line}");
            }

            ProcessRow(fields, writer);
            rowCount++;
        }

        if (!headerSeen)
        {
            throw new InvalidDataException($"grid file {gridPath} had no header row.");
        }
        if (rowCount == 0)
        {
            throw new InvalidDataException($"grid file {gridPath} produced zero rows -- a run that processed nothing is not a pass.");
        }

        Console.Error.WriteLine($"OracleDump: wrote {rowCount} row(s) to {outputPath}");
        return 0;
    }

    private static void ProcessRow(string[] fields, TextWriter writer)
    {
        var caseId = fields[0];
        var func = fields[1];

        // Fresh library state before every row -- see this file's header comment.
        using var swe = new SwissEph();

        switch (func)
        {
            case "CALC":
            case "CALC_UT":
                ProcessCalc(swe, caseId, func, fields, writer);
                break;
            case "HOUSES":
                ProcessHouses(swe, caseId, fields, writer);
                break;
            case "HOUSES_ARMC":
                ProcessHousesArmc(swe, caseId, fields, writer);
                break;
            default:
                throw new InvalidDataException($"unknown func '{func}' at case {caseId}");
        }
    }

    private static void ProcessCalc(SwissEph swe, string caseId, string func, string[] fields, TextWriter writer)
    {
        var ipl = ParseInt(fields[2], caseId, "ipl");
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var iflag = ParseInt(fields[4], caseId, "iflag");

        if (HasValue(fields[6]) || HasValue(fields[7]) || HasValue(fields[8]))
        {
            var geolon = ParseDouble(fields[6], caseId, "geolon");
            var geolat = ParseDouble(fields[7], caseId, "geolat");
            var height = ParseDouble(fields[8], caseId, "height");
            swe.swe_set_topo(geolon, geolat, height);
        }
        if (HasValue(fields[11]))
        {
            var sidMode = ParseInt(fields[11], caseId, "sid_mode");
            swe.swe_set_sid_mode(sidMode, 0, 0);
        }

        var xx = new double[6];
        string? serr = null;
        var retc = func == "CALC"
            ? swe.swe_calc(tjd, ipl, iflag, xx, ref serr)
            : swe.swe_calc_ut(tjd, ipl, iflag, xx, ref serr);

        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(EscapeErr(serr));
        for (var i = 0; i < 6; i++)
        {
            EmitValue(writer, xx[i]);
        }
        writer.Write('\n');
    }

    private static void ProcessHouses(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var tjd = ParseDouble(fields[3], caseId, "tjd");
        var hsys = ParseHsys(fields[5], caseId);
        var geolon = ParseDouble(fields[6], caseId, "geolon");
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        var cusp = new double[40];
        var ascmc = new double[10];
        var retc = swe.swe_houses(tjd, geolat, geolon, hsys, cusp, ascmc);

        WriteHousesRow(writer, caseId, retc, cusp, ascmc);
    }

    private static void ProcessHousesArmc(SwissEph swe, string caseId, string[] fields, TextWriter writer)
    {
        var armc = ParseDouble(fields[9], caseId, "armc");
        var eps = ParseDouble(fields[10], caseId, "eps");
        var hsys = ParseHsys(fields[5], caseId);
        var geolat = ParseDouble(fields[7], caseId, "geolat");

        var cusp = new double[40];
        var ascmc = new double[10];
        var retc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

        WriteHousesRow(writer, caseId, retc, cusp, ascmc);
    }

    // No serr output parameter on swe_houses/swe_houses_armc -- the err column stays empty for
    // these two funcs, which is not a driver defect, the C API genuinely has nothing to report
    // there (see sedump.c's header).
    private static void WriteHousesRow(TextWriter writer, string caseId, int retc, double[] cusp, double[] ascmc)
    {
        writer.Write(caseId);
        writer.Write('\t');
        writer.Write(retc.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t');
        for (var c = 0; c < CuspCount; c++)
        {
            EmitValue(writer, cusp[c]);
        }
        for (var a = 0; a < AscmcCount; a++)
        {
            EmitValue(writer, ascmc[a]);
        }
        writer.Write('\n');
    }

    private static char ParseHsys(string field, string caseId)
    {
        if (field.Length != 1)
        {
            throw new InvalidDataException($"hsys must be exactly one character at case {caseId}: '{field}'");
        }
        return field[0];
    }

    private static bool HasValue(string field) => field.Length != 0;

    private static double ParseDouble(string field, string caseId, string column)
    {
        if (field.Length == 0 || !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as a double at case {caseId}: '{field}'");
        }
        return value;
    }

    private static int ParseInt(string field, string caseId, string column)
    {
        if (field.Length == 0 || !int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as an int at case {caseId}: '{field}'");
        }
        return value;
    }

    // Mirrors sedump.c's emit_escaped and Tools/BaselineMatrix/Format.cs's S(): a raw serr
    // string could in principle contain a tab or newline and corrupt the TSV shape if printed
    // as-is.
    private static string EscapeErr(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    // %.17g on the C side; G17 is the round-trip-precise .NET counterpart, but the two runtimes
    // do not spell the same digits the same way: G17 renders an exponent as "E-05", %.17g as
    // "e-05". ToCStyleG17 below rewrites G17's output into %.17g's exact spelling (lowercase
    // exponent marker, C's minimum-two-digit exponent width) so this decimal column is directly
    // diffable against sedump.c's, instead of only agreeing once you already trust the hex
    // column beside it. The hex column stays the one a comparison pass treats as authoritative --
    // this fix makes the decimal column stop lying about that, not replaces it.
    private static void EmitValue(TextWriter writer, double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        writer.Write('\t');
        writer.Write(ToCStyleG17(value));
        writer.Write('\t');
        writer.Write(bits.ToString("x16", CultureInfo.InvariantCulture));
    }

    private static readonly char[] ExponentMarkers = ['e', 'E'];

    // Rewrites .NET's G17 exponent spelling into C's %.17g spelling. The mantissa digits
    // themselves are not touched -- both sides are printing the exact same double (the hex
    // column beside this one is the proof), and the mantissa is where the only meaningful
    // divergence could hide, so leaving it untouched is what makes this a formatting fix and
    // not a second, independent number-rendering path.
    private static string ToCStyleG17(double value)
    {
        var text = value.ToString("G17", CultureInfo.InvariantCulture);
        var markerIndex = text.IndexOfAny(ExponentMarkers);
        if (markerIndex < 0)
        {
            return text;
        }

        var mantissa = text[..markerIndex];
        var exponent = text[(markerIndex + 1)..];

        var sign = '+';
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[0];
            exponent = exponent[1..];
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length < 2)
        {
            exponent = exponent.PadLeft(2, '0');
        }

        return mantissa + "e" + sign + exponent;
    }
}
