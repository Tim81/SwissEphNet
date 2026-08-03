// Replays Tools/NetStandardCompat/grid-netstandard.tsv's swe_calc calls under whichever target
// framework this build was compiled for, and writes one output row per input row: case_id, retc,
// err, then (decimal, hex) pairs for xx[0..5] -- the same shape Tools/OracleDump/Program.cs's
// ProcessCalc already writes, reused here so a reviewer who already knows that format does not
// need to learn a second one. scripts/verify-netstandard-compat.ps1 runs this exe once per target
// framework (net10.0, net8.0, net462, net48) and compares the hex columns.
//
// INVOCATION
//
//   NetStandardCompatDump.exe <grid.tsv> <output.tsv>
//
// Unlike Tools/OracleDump, this driver never opens an ephemeris file at all: every row's iflag
// carries SEFLG_MOSEPH, and every fictitious body this grid covers falls back to its built-in
// element table the moment SentinelEpheDir fails to resolve seorbel.txt -- see
// gen-grid-netstandard.ps1's own header for the full mechanism. swe_set_ephe_path is still called,
// pinned to SentinelEpheDir, so this driver's behavior does not depend on the current directory or
// a compiled-in default the way an unset path would -- same reasoning as
// Tools/OracleDump/Program.cs's identical constant and call.

using System.Globalization;
using SwissEphNet;

namespace NetStandardCompatDump;

internal static class Program
{
    private const int ExpectedColumns = 4;

    // string.Join(string, ...), not the char-separator overload OracleDump's own ExpectedHeader*
    // fields use: the char-separator overload's availability on net462 is not worth relying on
    // when the string-separator one has been available since the very first .NET Framework and
    // needs no verification either way.
    private static readonly string ExpectedHeader = string.Join("\t", "case_id", "ipl", "tjd", "iflag");

    // Deliberately contains '?', not a legal Windows path character -- mirrors
    // Tools/OracleDump/Program.cs's identical SentinelEpheDir and its own comment on what this
    // guarantee does and does not cover.
    private const string SentinelEpheDir = "swisseph-netstandard-compat-sentinel-path?that-cannot-exist";

    private static int Main(string[] args)
    {
        // Same reasoning as Tools/OracleDump/Program.cs's identical call: a contributor with
        // SE_EPHE_PATH set for an unrelated Swiss Ephemeris install must not silently change what
        // this driver measures.
        Environment.SetEnvironmentVariable("SE_EPHE_PATH", null);

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: NetStandardCompatDump <grid.tsv> <output.tsv>");
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
                    throw new InvalidDataException($"grid header does not match what this driver expects.\nexpected: {ExpectedHeader}\ngot:      {line}");
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

        Console.Error.WriteLine($"NetStandardCompatDump: wrote {rowCount} row(s) to {outputPath}");
        return 0;
    }

    private static void ProcessRow(string[] fields, TextWriter writer)
    {
        var caseId = fields[0];
        var ipl = ParseInt(fields[1], caseId, "ipl");
        var tjd = ParseDouble(fields[2], caseId, "tjd");
        var iflag = ParseInt(fields[3], caseId, "iflag");

        // Fresh library instance per row -- matches Tools/OracleDump/Program.cs's identical choice
        // and reasoning (no call in this grid depends on state a prior row could have left behind,
        // and a fresh instance keeps it that way by construction rather than by care).
        using var swe = new SwissEph();
        swe.swe_set_ephe_path(SentinelEpheDir);

        var xx = new double[6];
        string? serr = null;
        var retc = swe.swe_calc(tjd, ipl, iflag, xx, ref serr);

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

    private static bool HasValue(string field) => field.Length != 0;

    private static double ParseDouble(string field, string caseId, string column)
    {
        if (!HasValue(field) || !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as a double at case {caseId}: '{field}'");
        }
        return value;
    }

    private static int ParseInt(string field, string caseId, string column)
    {
        if (!HasValue(field) || !int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"cannot parse '{column}' as an int at case {caseId}: '{field}'");
        }
        return value;
    }

    // Mirrors Tools/OracleDump/Program.cs's identical EscapeErr. The null-forgiving `!` below is
    // needed only on net462/net48: those targets have no nullable-annotated BCL reference
    // assemblies, so the compiler cannot see that string.IsNullOrEmpty(value) narrows value to
    // non-null past this point the way it does on net8.0/net10.0 -- CS8602 fires there without it.
    private static string EscapeErr(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return value!
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    // Decimal column for human review, hex column for machine comparison -- same split as
    // Tools/OracleDump/Program.cs's EmitValue. No C reference is involved here (both sides of
    // every comparison this tool feeds are .NET runtimes), so there is no %.17g spelling to match
    // and no ToCStyleG17 rewrite: plain "R" (round-trip) is enough for the decimal column to be
    // readable. The hex column is built from BitConverter.DoubleToInt64Bits, not
    // BitConverter.DoubleToUInt64Bits (which Tools/OracleDump/Program.cs uses): that overload was
    // only added in .NET 5 and does not exist on net462/net48, two of the four target frameworks
    // this project builds for, so the older, always-available DoubleToInt64Bits plus an unchecked
    // cast is used instead -- the same pattern Tools/OracleVerify/UlpMath.cs's own OrderedKey
    // already relies on for net10.0-only code, applied here because it is required, not merely
    // consistent.
    private static void EmitValue(TextWriter writer, double value)
    {
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        writer.Write('\t');
        writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(bits.ToString("x16", CultureInfo.InvariantCulture));
    }
}
