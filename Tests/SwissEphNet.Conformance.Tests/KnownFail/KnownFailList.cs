using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>
/// Reads/writes Tests/conformance/known-fail.tsv: the work queue of iterations
/// the port is known to fail today, one per line, so a conformance run can
/// tell "expected failure, still work to do" apart from "new regression".
/// </summary>
public static class KnownFailList
{
    private static readonly string[] Header = ["suite", "testcase", "iteration", "category", "reason"];

    public static IReadOnlyDictionary<IterationKey, KnownFailEntry> Load(string path)
    {
        var result = new Dictionary<IterationKey, KnownFailEntry>();
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
            if (parts.Length != 5)
            {
                throw new FormatException($"{path}:{lineNumber}: expected 5 tab-separated columns, got {parts.Length}: '{line}'");
            }

            var key = new IterationKey(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
            var category = FailureCategoryNames.Parse(parts[3]);
            var entry = new KnownFailEntry(key, category, parts[4]);

            if (!result.TryAdd(key, entry))
            {
                throw new FormatException($"{path}:{lineNumber}: duplicate entry for iteration {key}.");
            }
        }

        return result;
    }

    public static void Save(string path, IEnumerable<KnownFailEntry> entries)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.NewLine = "\n";
        writer.WriteLine(string.Join('\t', Header));

        foreach (var entry in entries
                     .OrderBy(e => e.Key.Suite)
                     .ThenBy(e => e.Key.TestCase)
                     .ThenBy(e => e.Key.Iteration))
        {
            writer.WriteLine(string.Join(
                '\t',
                entry.Key.Suite.ToString(CultureInfo.InvariantCulture),
                entry.Key.TestCase.ToString(CultureInfo.InvariantCulture),
                entry.Key.Iteration.ToString(CultureInfo.InvariantCulture),
                FailureCategoryNames.ToName(entry.Category),
                entry.Reason));
        }
    }
}
