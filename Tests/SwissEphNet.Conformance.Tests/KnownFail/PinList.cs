using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SwissEphNet.Conformance.Tests.Dispatch;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>
/// Reads/writes Tests/conformance/known-fail-pins.tsv: for every VALUE-MISMATCH row of
/// known-fail.tsv, the name, kind and port-produced value of each field that mismatches t.exp, and
/// for a failing runtime self-check (CHECK_EQUALS_*) both of its operands (the second as "name#2").
/// A separate file, not a column of known-fail.tsv: a row has one pin per mismatching field (up to
/// dozens), known-fail.tsv is one row per iteration and is parsed by several scripts, and the
/// pins are the part that is allowed to move only through the logged regeneration
/// (scripts/regenerate-known-fail.ps1), which writes both files together.
/// </summary>
public static class PinList
{
    private static readonly string[] Header = ["suite", "testcase", "iteration", "field", "kind", "port_value"];

    public static IReadOnlyDictionary<IterationKey, IReadOnlyList<PinnedField>> Load(string path)
    {
        var result = new Dictionary<IterationKey, List<PinnedField>>();
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
            if (parts.Length != 6)
            {
                throw new FormatException($"{path}:{lineNumber}: expected 6 tab-separated columns, got {parts.Length}: '{line}'");
            }

            var key = new IterationKey(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
            if (!Enum.TryParse<FieldKind>(parts[4], ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            {
                throw new FormatException($"{path}:{lineNumber}: unknown field kind '{parts[4]}'.");
            }

            if (!result.TryGetValue(key, out var list))
            {
                result[key] = list = [];
            }

            list.Add(new PinnedField(key, parts[3], kind, parts[5]));
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<PinnedField>)kv.Value);
    }

    /// <summary>
    /// The pins of every VALUE-MISMATCH result in <paramref name="results"/>, ordered by row (a
    /// row's fields keep the order the dispatcher compared them in, so a repeated field name keeps
    /// its occurrences in sequence).
    /// </summary>
    public static IReadOnlyList<PinnedField> FromResults(IEnumerable<IterationResult> results, string epheDir)
    {
        var pins = new List<PinnedField>();
        foreach (var r in results.Where(r => r.Kind == OutcomeKind.ValueMismatch)
                     .OrderBy(r => r.Key.Suite).ThenBy(r => r.Key.TestCase).ThenBy(r => r.Key.Iteration))
        {
            foreach (var m in r.Mismatches)
            {
                foreach (var operand in PinGate.Operands(m, epheDir))
                {
                    pins.Add(new PinnedField(r.Key, operand.Name, operand.Kind, operand.Text));
                }
            }
        }

        return pins;
    }

    public static void Save(string path, IEnumerable<PinnedField> pins)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.NewLine = "\n";
        writer.WriteLine(string.Join('\t', Header));
        foreach (var pin in pins)
        {
            writer.WriteLine(string.Join(
                '\t',
                pin.Key.Suite.ToString(CultureInfo.InvariantCulture),
                pin.Key.TestCase.ToString(CultureInfo.InvariantCulture),
                pin.Key.Iteration.ToString(CultureInfo.InvariantCulture),
                pin.Name,
                pin.Kind.ToString(),
                pin.Value));
        }
    }
}
