using System;
using System.Collections.Generic;
using System.Globalization;
using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Accumulates CHECK_D / CHECK_DD / CHECK_I / CHECK_S-style comparisons for one
/// iteration, mirroring external/swisseph/setest/checkpoints.c and
/// globals_suite.c exactly (including the "xx" array's per-component
/// precision special case).
/// </summary>
public sealed class CheckContext(ExpFields expected, Precision precision)
{
    private readonly List<FieldMismatch> _mismatches = [];

    public IReadOnlyList<FieldMismatch> Mismatches => _mismatches;

    /// <summary>CHECK_D(name) -- always uses the testcase's overall precision.</summary>
    public void CheckD(string name, double actual) => CheckDInternal(name, actual, precision.All);

    /// <summary>
    /// CHECK_DD(name, length) -- for name == "xx" and index &lt; 6, uses
    /// precision.Xx[i]; otherwise uses the overall precision for every index.
    /// </summary>
    public void CheckDD(string name, IReadOnlyList<double> actual)
    {
        for (var i = 0; i < actual.Count; i++)
        {
            var fieldName = $"{name}[{i}]";
            var fieldPrecision = name == "xx" && i < 6 ? precision.Xx[i] : precision.All;
            CheckDInternal(fieldName, actual[i], fieldPrecision);
        }
    }

    public void CheckI(string name, int actual)
    {
        var exp = expected.GetInt(name);
        if (actual != exp)
        {
            _mismatches.Add(new FieldMismatch(name, exp.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), null));
        }
    }

    /// <summary>CHECK_EQUALS_I(actual, literal) -- a pure runtime self-consistency check, not file-backed.</summary>
    public void CheckEqualsI(string name, int actual, int expectedLiteral)
    {
        if (actual != expectedLiteral)
        {
            _mismatches.Add(new FieldMismatch(name, expectedLiteral.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), null));
        }
    }

    public void CheckS(string name, string actual)
    {
        var exp = expected.GetRawString(name);
        var escapedActual = EscapeNewlines(actual);
        if (!string.Equals(exp, escapedActual, StringComparison.Ordinal))
        {
            _mismatches.Add(new FieldMismatch(name, exp, escapedActual, null));
        }
    }

    private void CheckDInternal(string name, double actual, double allowedDiff)
    {
        var exp = expected.GetDouble(name);
        if (double.IsNaN(actual) || double.IsNaN(exp) || Math.Abs(exp - actual) > allowedDiff)
        {
            _mismatches.Add(new FieldMismatch(
                name,
                exp.ToString("G17", CultureInfo.InvariantCulture),
                actual.ToString("G17", CultureInfo.InvariantCulture),
                exp - actual));
        }
    }

    private static string EscapeNewlines(string value) => value.Replace("\n", "\\n");
}
