using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SwissEphNet.Conformance.Tests.Corpus;

/// <summary>
/// One "key: value # comment" line as it literally appears in t.exp, keyed by
/// name (e.g. "jd", "xx[0]", "ihsy"). The value is stored exactly as read
/// (including any trailing "# comment" text) -- see <see cref="GetDouble"/>
/// and <see cref="GetInt"/> for why: the reference reader (reader.c) never
/// strips trailing comments from a value either, it just relies on sscanf's
/// leading-numeric-prefix behavior when it later parses the value as a
/// number. We reproduce that by truncating at "#" only inside the numeric
/// accessors, never when a field is read as a raw string.
/// </summary>
public sealed class ExpFields
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    public int LineNumber { get; init; }

    public IReadOnlyDictionary<string, string> RawValues => _values;

    /// <summary>Every physical "name: value" line seen, including duplicates that overwrote nothing.</summary>
    public int RawLineCount { get; private set; }

    /// <summary>
    /// Keys read via any accessor (GetDouble/GetInt/GetRawString/...), whether
    /// as a testcase input or as an expected-value comparison. Used by
    /// the completeness guard in <c>ConformanceRunner.Run</c> to catch a testcase/comparison that
    /// silently never looked at a field t.exp actually asserts.
    /// </summary>
    public IReadOnlyCollection<string> ConsumedKeys => _consumed;

    /// <summary>
    /// Records a "name: value" line. Matches the reference reader's semantics
    /// (external/swisseph/setest/reader.c: find_value returns the *first*
    /// matching entry in the block's table -- push_row only ever appends, it
    /// never overwrites) rather than a plain dictionary assignment's
    /// last-write-wins: on a repeated key within the same section, the first
    /// occurrence wins and later ones are recorded only in the raw line count.
    /// </summary>
    public void Set(string name, string rawValue)
    {
        RawLineCount++;
        _values.TryAdd(name, rawValue);
    }

    public bool Contains(string name) => _values.ContainsKey(name);

    public string GetRawString(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            throw new KeyNotFoundException($"Field '{name}' not found (line {LineNumber}).");
        }

        _consumed.Add(name);
        return value;
    }

    public string? TryGetRawString(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return null;
        }

        _consumed.Add(name);
        return value;
    }

    /// <summary>Parses a value as a double, truncating at a trailing "#" comment first.</summary>
    public double GetDouble(string name)
    {
        var raw = GetRawString(name);
        var numeric = TruncateComment(raw);
        if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Field '{name}' = '{raw}' is not a parseable double (line {LineNumber}).");
        }

        return value;
    }

    public double? TryGetDouble(string name) => Contains(name) ? GetDouble(name) : null;

    /// <summary>Parses a value as an int, truncating at a trailing "#" comment first.</summary>
    public int GetInt(string name)
    {
        var raw = GetRawString(name);
        var numeric = TruncateComment(raw);
        if (!int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Field '{name}' = '{raw}' is not a parseable int (line {LineNumber}).");
        }

        return value;
    }

    public int? TryGetInt(string name) => Contains(name) ? GetInt(name) : null;

    public double[] GetDoubleArray(string prefix, int count)
    {
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = GetDouble($"{prefix}[{i}]");
        }

        return result;
    }

    /// <summary>
    /// Keys present in this block that no accessor has read, excluding a
    /// caller-supplied set of purely-decorative/structural keys (section-id,
    /// section-descr, initialize -- see ConformanceRunner). A non-empty result
    /// means either an input the dispatcher ignores (harmless) or, more
    /// importantly, an asserted value the dispatcher never compared (a silent
    /// false pass: e.g. an undersized buffer that stops a CHECK_DD short).
    /// </summary>
    public IReadOnlyList<string> UnconsumedKeys(IReadOnlyCollection<string> excludedKeys) =>
        _values.Keys.Where(k => !_consumed.Contains(k) && !excludedKeys.Contains(k)).ToList();

    private static string TruncateComment(string raw)
    {
        var hashIndex = raw.IndexOf('#');
        var trimmed = hashIndex >= 0 ? raw[..hashIndex] : raw;
        return trimmed.Trim();
    }
}

public sealed class ExpIteration
{
    public required int Id { get; init; }
    public required ExpFields Fields { get; init; }
}

public sealed class ExpTestCase
{
    public required int Id { get; init; }
    public required string? Description { get; init; }
    public required ExpFields Fields { get; init; }
    public required IReadOnlyList<ExpIteration> Iterations { get; init; }
}

public sealed class ExpTestSuite
{
    public required int Id { get; init; }
    public required string? Description { get; init; }
    public required ExpFields Fields { get; init; }
    public required IReadOnlyList<ExpTestCase> TestCases { get; init; }
}

public sealed class ExpDocument
{
    public required IReadOnlyDictionary<string, string> Header { get; init; }
    public required IReadOnlyList<ExpTestSuite> TestSuites { get; init; }

    public int TotalIterationCount
    {
        get
        {
            var total = 0;
            foreach (var suite in TestSuites)
            {
                foreach (var testCase in suite.TestCases)
                {
                    total += testCase.Iterations.Count;
                }
            }

            return total;
        }
    }

    public int TotalTestCaseCount
    {
        get
        {
            var total = 0;
            foreach (var suite in TestSuites)
            {
                total += suite.TestCases.Count;
            }

            return total;
        }
    }

    /// <summary>
    /// Total number of physical "name: value" lines across every iteration
    /// (RawLineCount, not the deduplicated RawValues.Count -- a handful of
    /// iterations in suite 9 read the same field twice with identical values,
    /// and a line count should still count both).
    /// </summary>
    public int TotalValueLineCount
    {
        get
        {
            var total = 0;
            foreach (var suite in TestSuites)
            {
                foreach (var testCase in suite.TestCases)
                {
                    foreach (var iteration in testCase.Iterations)
                    {
                        total += iteration.Fields.RawLineCount;
                    }
                }
            }

            return total;
        }
    }
}
