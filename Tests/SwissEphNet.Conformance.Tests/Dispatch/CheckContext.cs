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
    private int _comparisonCount;

    public IReadOnlyList<FieldMismatch> Mismatches => _mismatches;

    /// <summary>
    /// Whether at least one Check* call actually ran a comparison -- against
    /// t.exp (CheckD/CheckDD/CheckI/CheckS) or, for the handful of testcases
    /// that assert a pure runtime self-consistency condition instead
    /// (CHECK_EQUALS_I, not file-backed), against another computed value. Used
    /// by the completeness guard in <c>ConformanceRunner.Run</c>: a testcase
    /// that reaches a Passed/ValueMismatch outcome without this ever going
    /// true compared nothing at all, which is not a real pass -- see
    /// Suite01Calc testcase 4 (CheckEqualsI-only, legitimately) versus the
    /// Suite03Misc regression this guards against (a CheckS call replaced by a
    /// discarded plain field read, which left the outcome "Passed" with zero
    /// comparisons performed).
    /// </summary>
    public bool AnyComparisonPerformed => _comparisonCount > 0;

    /// <summary>CHECK_D(name) -- always uses the testcase's overall precision.</summary>
    public void CheckD(string name, double actual) => CheckDInternal(name, actual, precision.All);

    /// <summary>
    /// CHECK_DD(name, length) -- for any name with an "xx" *prefix* (xx, xxnasc,
    /// xxndsc, xxperi, xxaphe, xxdret, xxtret, xxattr, xxgeopos, ...) and
    /// index &lt; 6, uses precision.Xx[i]; otherwise uses the overall precision
    /// for every index.
    /// </summary>
    /// <remarks>
    /// Matches external/swisseph/setest/checkpoints.c:14 exactly:
    /// <c>strncmp(name,"xx", 2) == 0 &amp;&amp; i &lt; 6</c> -- a prefix test, not an
    /// exact-name test. The exact-name form lives in a different function
    /// (check_equals_dd, checkpoints.c:146) that no suite actually calls;
    /// testsuite.m4:79 maps CHECK_DD to the prefix-matching check_dd. Getting
    /// this wrong is not cosmetic: every array whose name starts with "xx" --
    /// xxnasc/xxndsc/xxperi/xxaphe (suite 7), xxdret (suite 7), xxtret/xxattr
    /// (suites 8-9), xxgeopos (suite 8) -- would otherwise be compared at
    /// precision.All instead of the (usually far tighter) precision.Xx[i],
    /// which is a false-pass generator: in suites 7-9 the carried-over
    /// precision.All is often 1e-3 (leaked from suite 6's testsuite-level
    /// override) against precision.Xx of 1e-8/1e-6, so indices 0-2 would be
    /// let through 100,000x too loosely and 3-5 by 1,000x.
    /// </remarks>
    public void CheckDD(string name, IReadOnlyList<double> actual)
    {
        for (var i = 0; i < actual.Count; i++)
        {
            var fieldName = $"{name}[{i}]";
            var fieldPrecision = name.StartsWith("xx", StringComparison.Ordinal) && i < 6 ? precision.Xx[i] : precision.All;
            CheckDInternal(fieldName, actual[i], fieldPrecision);
        }
    }

    public void CheckI(string name, int actual)
    {
        _comparisonCount++;
        var exp = expected.GetIntCompared(name);
        if (actual != exp)
        {
            _mismatches.Add(new FieldMismatch(name, exp.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), null));
        }
    }

    /// <summary>CHECK_EQUALS_I(actual, literal) -- a pure runtime self-consistency check, not file-backed.</summary>
    public void CheckEqualsI(string name, int actual, int expectedLiteral)
    {
        _comparisonCount++;
        if (actual != expectedLiteral)
        {
            _mismatches.Add(new FieldMismatch(name, expectedLiteral.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), null));
        }
    }

    /// <summary>
    /// CHECK_S(name). <paramref name="actual"/> may legitimately be null: many
    /// SwissEphNet calls (a mechanical C port) leave a `ref string serr` output
    /// parameter untouched on a success path where the C original never wrote
    /// to its char* buffer, the same way an empty C string ("") represents "no
    /// error" in t.exp. Both compare equal to an empty expected string.
    /// </summary>
    public void CheckS(string name, string? actual)
    {
        _comparisonCount++;
        var exp = expected.GetRawStringCompared(name);
        var escapedActual = EscapeNewlines(actual ?? "");
        if (!string.Equals(exp, escapedActual, StringComparison.Ordinal))
        {
            _mismatches.Add(new FieldMismatch(name, exp, escapedActual, null));
        }
    }

    private void CheckDInternal(string name, double actual, double allowedDiff)
    {
        _comparisonCount++;
        var exp = expected.GetDoubleCompared(name);
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
