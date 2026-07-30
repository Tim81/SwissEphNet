using System.Globalization;

namespace OracleVerify;

/// <summary>
/// Reads/writes Tests/oracle/known-diff.tsv: the accounted-for-difference list this comparer
/// checks every row against -- mirrors
/// Tests/SwissEphNet.Conformance.Tests/KnownFail/KnownFailList.cs's contract exactly, including
/// hard-failing (not skipping) on a bad header, a wrong column count, or a duplicate key, since a
/// reader that tolerated any of those could silently compare against a truncated or corrupted
/// list and report a false PASS.
/// </summary>
internal static class KnownDiffList
{
    private static readonly string[] Header = ["case_id", "category", "max_ulp", "reason"];

    /// <summary>
    /// The max_ulp column's non-numeric marker for a categorical difference (see
    /// <see cref="KnownDiffEntry.MaxUlp"/>'s remarks) -- distinguishable from any real value on
    /// sight, and from any real value in code because <see cref="ulong.TryParse(string, out ulong)"/>
    /// rejects it outright, so a malformed numeric column can never be silently misread as this.
    /// </summary>
    private const string CategoricalMarker = "categorical";

    public static IReadOnlyDictionary<string, KnownDiffEntry> Load(string path)
    {
        var result = new Dictionary<string, KnownDiffEntry>(StringComparer.Ordinal);
        using var reader = new StreamReader(path);

        var headerLine = reader.ReadLine();
        if (headerLine is null || !headerLine.Split('\t').SequenceEqual(Header, StringComparer.Ordinal))
        {
            throw new FormatException($"{path}: expected header '{string.Join('\t', Header)}', got '{headerLine}'.");
        }

        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length != 4)
            {
                throw new FormatException($"{path}:{lineNumber}: expected 4 tab-separated columns, got {parts.Length}: '{line}'");
            }

            var caseId = parts[0];
            if (caseId.Length == 0)
            {
                throw new FormatException($"{path}:{lineNumber}: empty case_id.");
            }

            var category = DiffCategoryNames.Parse(parts[1]);
            ulong? maxUlp;
            if (parts[2] == CategoricalMarker)
            {
                maxUlp = null;
            }
            else if (ulong.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxUlp))
            {
                maxUlp = parsedMaxUlp;
            }
            else
            {
                throw new FormatException($"{path}:{lineNumber}: cannot parse max_ulp '{parts[2]}' for case {caseId}.");
            }

            var entry = new KnownDiffEntry(caseId, category, maxUlp, parts[3]);
            if (!result.TryAdd(caseId, entry))
            {
                throw new FormatException($"{path}:{lineNumber}: duplicate entry for case_id '{caseId}'.");
            }
        }

        return result;
    }

    public static void Save(string path, IEnumerable<KnownDiffEntry> entries)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.NewLine = "\n";
        writer.WriteLine(string.Join('\t', Header));

        foreach (var entry in entries.OrderBy(e => e.CaseId, StringComparer.Ordinal))
        {
            writer.WriteLine(string.Join(
                '\t',
                entry.CaseId,
                DiffCategoryNames.ToName(entry.Category),
                entry.MaxUlp is { } maxUlp ? maxUlp.ToString(CultureInfo.InvariantCulture) : CategoricalMarker,
                entry.Reason));
        }
    }
}
