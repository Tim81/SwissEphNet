using System.Globalization;

namespace OracleVerify;

/// <summary>
/// One parsed line from either external/.c-reference/dump-c-2.10.03.tsv or dump-net.tsv -- see
/// Tools/OracleDump/Program.cs's and Tools/CReference/sedump.c's shared header comments for the
/// on-disk shape: case_id, retc, err, then (decimal, hex) pairs, one pair per value the row's
/// func returns.
///
/// The decimal column is not read here at all. It exists in the dump files for a human to eyeball
/// (and, since the exponent-case fix, to actually match byte for byte between the two dumps), but
/// the value this comparer treats as authoritative is always the one decoded from the hex column
/// -- that is the whole reason the hex column is there.
///
/// The err column is read as-is, still backslash-escaped exactly the way
/// Tools/OracleDump/Program.cs's EscapeErr and sedump.c's emit_escaped wrote it. Both sides apply
/// the same escaping, so comparing the escaped text is equivalent to comparing the underlying
/// serr strings; there is no need to unescape it here just to compare it.
/// </summary>
internal sealed class DumpRow
{
    public required string CaseId { get; init; }
    public required int Retc { get; init; }
    public required string Err { get; init; }
    public required IReadOnlyList<double> Values { get; init; }

    public static DumpRow Parse(string line, string path, int lineNumber)
    {
        var fields = line.Split('\t');
        if (fields.Length < 3 || (fields.Length - 3) % 2 != 0)
        {
            throw new FormatException(
                $"{path}:{lineNumber}: malformed row, expected case_id, retc, err, then one or more (decimal, hex) pairs: '{line}'");
        }

        var caseId = fields[0];
        if (caseId.Length == 0)
        {
            throw new FormatException($"{path}:{lineNumber}: empty case_id.");
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var retc))
        {
            throw new FormatException($"{path}:{lineNumber}: cannot parse retc '{fields[1]}' at case {caseId}.");
        }

        var err = fields[2];

        var pairCount = (fields.Length - 3) / 2;
        var values = new double[pairCount];
        for (var i = 0; i < pairCount; i++)
        {
            var hex = fields[3 + (i * 2) + 1];
            if (hex.Length != 16 || !ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits))
            {
                throw new FormatException($"{path}:{lineNumber}: cannot parse hex column at value index {i} ('{hex}') at case {caseId}.");
            }
            values[i] = BitConverter.UInt64BitsToDouble(bits);
        }

        return new DumpRow { CaseId = caseId, Retc = retc, Err = err, Values = values };
    }
}
