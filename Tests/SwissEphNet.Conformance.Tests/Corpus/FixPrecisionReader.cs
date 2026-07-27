using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SwissEphNet.Conformance.Tests.Corpus;

/// <summary>Resolved tolerance for one testcase: an overall precision plus a per-xx[i] precision.</summary>
public readonly record struct Precision(double All, double[] Xx)
{
    /// <summary>Precision applicable to a named, non-xx[] field (cusps, ascmc, armc, jd_ut, ...).</summary>
    public double ForField(string name) => name == "xx" ? throw new InvalidOperationException("use ForXx") : All;

    public double ForXx(int index) => Xx[index];

    /// <summary>
    /// DEFAULT_PRECISION for every component. Used as a fallback for the rare
    /// (suite, testcase) pair present in t.exp but absent from t.fix (a real,
    /// small drift between the two files -- see FixPrecisionReader's remarks)
    /// where nothing in that suite ever overrides precision anyway.
    /// </summary>
    public static Precision Default => new(
        FixPrecisionReader.DefaultPrecision,
        [FixPrecisionReader.DefaultPrecision, FixPrecisionReader.DefaultPrecision, FixPrecisionReader.DefaultPrecision,
         FixPrecisionReader.DefaultPrecision, FixPrecisionReader.DefaultPrecision, FixPrecisionReader.DefaultPrecision]);
}

/// <summary>
/// Resolves the precision (tolerance) applicable to each (suite, testcase) pair
/// from setest/t.fix, reproducing the reference tool's actual, quirky
/// resolution rules (external/swisseph/setest/setest.c: prepare_precisions,
/// read_value; reader.c: read_next_block).
/// </summary>
/// <remarks>
/// <para>
/// Three key facts, verified by reading the reference C source rather than
/// guessed from field names:
/// </para>
/// <para>
/// 1. "precision" / "precision-xx" are looked up per testcase via a hierarchy
///    search: the testcase's own directly-declared lines first, then its
///    enclosing testsuite's own directly-declared lines, then the handful of
///    lines that appear before the very first TESTSUITE in the file
///    ("GENERAL" scope). The first level with the key wins.
/// </para>
/// <para>
/// 2. Lines whose first non-whitespace character is '#' are comments and are
///    never seen by that lookup, including "#precision:1e-9"-style lines --
///    despite starting with the word "precision", they are inert.
/// </para>
/// <para>
/// 3. The resolved (all, xx[0..5]) tolerance is stored in one mutable
///    location that is *only overwritten* when a testcase's hierarchy search
///    actually finds something. A testcase that finds nothing anywhere in its
///    hierarchy silently inherits whatever the previous testcase (in suite
///    declaration order) left behind -- this is a real, observable behavior of
///    the reference tool, confirmed here by cross-checking against the
///    "precision"/"precision-xx" lines t.exp itself embeds per testcase at
///    generation time (only ever written when found, which is exactly the
///    set of testcases this reader computes a *newly set* tolerance for).
/// </para>
/// <para>
/// The default, absent any override anywhere in the hierarchy for the very
/// first testcase, is 1e-9 (external/swisseph/setest/constants.c:
/// DEFAULT_PRECISION).
/// </para>
/// </remarks>
public static class FixPrecisionReader
{
    public const double DefaultPrecision = 1e-9;

    /// <summary>
    /// Resolves precision per (suiteId, testCaseId), restricted to the
    /// (suite, testcase) pairs that actually occur in <paramref name="doc"/>.
    /// t.fix carries orphaned/disabled content (an unused "suite 66", and a
    /// stray duplicate testcase block under suite 5) that no longer
    /// corresponds to anything in the frozen t.exp; restricting to what t.exp
    /// actually contains keeps that drift from corrupting the resolution.
    /// </summary>
    public static IReadOnlyDictionary<(int Suite, int TestCase), Precision> Resolve(string fixPath, ExpDocument doc)
    {
        var validPairs = new HashSet<(int, int)>();
        foreach (var suite in doc.TestSuites)
        {
            foreach (var testCase in suite.TestCases)
            {
                validPairs.Add((suite.Id, testCase.Id));
            }
        }

        using var reader = new StreamReader(fixPath);
        return Resolve(reader, validPairs);
    }

    internal static IReadOnlyDictionary<(int Suite, int TestCase), Precision> Resolve(
        TextReader reader,
        HashSet<(int Suite, int TestCase)> validPairs)
    {
        var result = new Dictionary<(int, int), Precision>();

        double? generalAll = null;
        double[]? generalXx = null;

        double currentAll = DefaultPrecision;
        var currentXx = new[] { DefaultPrecision, DefaultPrecision, DefaultPrecision, DefaultPrecision, DefaultPrecision, DefaultPrecision };

        // Scope tracking. "General" is before the first TESTSUITE. Once a
        // TESTSUITE is open, lines belong to it until the first nested
        // TESTCASE; once a TESTCASE is open, lines belong to it until the
        // first nested ITERATION (after which we simply skip lines, since
        // iteration-level data is irrelevant to precision).
        var scope = Scope.General;
        var suiteActive = false; // whether the currently-open suite exists in the exp doc
        var suiteId = 0;
        double? suiteAll = null;
        double[]? suiteXx = null;

        var testCaseActive = false; // whether the currently-open testcase exists in the exp doc
        var testCaseId = 0;
        double? tcAll = null;
        double[]? tcXx = null;

        void ResolveAndRecordTestCase()
        {
            if (!testCaseActive)
            {
                return;
            }

            var foundAll = tcAll ?? suiteAll ?? generalAll;
            if (foundAll is not null)
            {
                currentAll = foundAll.Value;
                for (var i = 0; i < 6; i++)
                {
                    currentXx[i] = foundAll.Value;
                }
            }

            var foundXx = tcXx ?? suiteXx ?? generalXx;
            if (foundXx is not null)
            {
                Array.Copy(foundXx, currentXx, 6);
            }

            result[(suiteId, testCaseId)] = new Precision(currentAll, (double[])currentXx.Clone());
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (trimmed == "TESTSUITE")
            {
                ResolveAndRecordTestCase();
                testCaseActive = false;
                tcAll = null;
                tcXx = null;
                scope = Scope.Suite;
                suiteActive = false;
                suiteAll = null;
                suiteXx = null;
                suiteId = 0;
                continue;
            }

            if (trimmed == "TESTCASE")
            {
                ResolveAndRecordTestCase();
                scope = Scope.TestCase;
                testCaseActive = false;
                tcAll = null;
                tcXx = null;
                testCaseId = 0;
                continue;
            }

            if (trimmed == "ITERATION")
            {
                scope = Scope.Iteration;
                continue;
            }

            if (scope == Scope.Iteration)
            {
                // Iteration-level content is irrelevant to precision resolution.
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (name == "section-id" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
            {
                if (scope == Scope.Suite)
                {
                    suiteId = idValue;
                    suiteActive = ContainsSuite(validPairs, suiteId);
                }
                else if (scope == Scope.TestCase)
                {
                    testCaseId = idValue;
                    testCaseActive = suiteActive && validPairs.Contains((suiteId, testCaseId));
                }

                continue;
            }

            if (name == "precision" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var precAll))
            {
                switch (scope)
                {
                    case Scope.General: generalAll = precAll; break;
                    case Scope.Suite: suiteAll = precAll; break;
                    case Scope.TestCase: tcAll = precAll; break;
                }

                continue;
            }

            if (name == "precision-xx")
            {
                var parts = value.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 6)
                {
                    throw new FormatException($"'precision-xx' expects 6 comma-separated values, got '{value}'.");
                }

                var xx = new double[6];
                for (var i = 0; i < 6; i++)
                {
                    xx[i] = double.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                switch (scope)
                {
                    case Scope.General: generalXx = xx; break;
                    case Scope.Suite: suiteXx = xx; break;
                    case Scope.TestCase: tcXx = xx; break;
                }
            }
        }

        ResolveAndRecordTestCase();

        return result;
    }

    private static bool ContainsSuite(HashSet<(int Suite, int TestCase)> pairs, int suite)
    {
        foreach (var pair in pairs)
        {
            if (pair.Suite == suite)
            {
                return true;
            }
        }

        return false;
    }

    private enum Scope
    {
        General,
        Suite,
        TestCase,
        Iteration,
    }
}
