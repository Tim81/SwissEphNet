namespace OracleVerify;

/// <summary>
/// Loads one whole dump TSV (see <see cref="DumpRow"/>) keyed by case_id, and refuses outright
/// rather than silently under-comparing: a duplicate case_id or an empty file is a hard failure
/// here, the same posture Tools/OracleDump/Program.cs and Tools/CReference/sedump.c both take
/// on their own output ("a run that processed nothing is not a pass").
/// </summary>
internal static class DumpFile
{
    public static IReadOnlyDictionary<string, DumpRow> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Dump file not found: {path}", path);
        }

        var rows = new Dictionary<string, DumpRow>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var row = DumpRow.Parse(line, path, lineNumber);
            if (!rows.TryAdd(row.CaseId, row))
            {
                throw new FormatException($"{path}:{lineNumber}: duplicate case_id '{row.CaseId}'.");
            }
        }

        if (rows.Count == 0)
        {
            throw new FormatException($"{path} produced zero rows -- a run that processed nothing is not a pass.");
        }

        return rows;
    }
}
