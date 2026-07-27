using System;
using System.Collections.Generic;
using System.Globalization;

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

    public int LineNumber { get; init; }

    public IReadOnlyDictionary<string, string> RawValues => _values;

    public void Set(string name, string rawValue) => _values[name] = rawValue;

    public bool Contains(string name) => _values.ContainsKey(name);

    public string GetRawString(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            throw new KeyNotFoundException($"Field '{name}' not found (line {LineNumber}).");
        }

        return value;
    }

    public string? TryGetRawString(string name) => _values.TryGetValue(name, out var value) ? value : null;

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

    /// <summary>Total number of "name: value" assertion/data lines across every iteration.</summary>
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
                        total += iteration.Fields.RawValues.Count;
                    }
                }
            }

            return total;
        }
    }
}
