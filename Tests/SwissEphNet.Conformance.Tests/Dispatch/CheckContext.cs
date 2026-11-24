using System;
using System.Collections.Generic;
using System.Globalization;
using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Accumulates CHECK_D / CHECK_DD / CHECK_I / CHECK_S-style comparisons for one
/// iteration, mirroring external/swisseph/setest/checkpoints.c and
/// globals_suite.c exactly (including the "xx" array's per-component
/// fallback precision and, since v2.10.04, the per-quantity tolerances of tolerances.c).
/// </summary>
public sealed class CheckContext(ExpFields expected, TestCaseScope scope)
{
    private readonly Precision precision = scope.Precision;

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

    /// <summary>
    /// CHECK_D(name) -- checkpoints.c:6 check_d: the testcase's overall precision is the fallback
    /// tolerance; <see cref="CheckDInternal"/> replaces it with the per-quantity tolerance when
    /// tolerances.c knows the name.
    /// </summary>
    public void CheckD(string name, double actual) => CheckDInternal(name, actual, precision.All);

    /// <summary>
    /// CHECK_DD(name, length) -- for any name with an "xx" *prefix* (xx, xxnasc,
    /// xxndsc, xxperi, xxaphe, xxdret, xxtret, xxattr, xxgeopos, ...) and
    /// index &lt; 6, the fallback precision is precision.Xx[i]; otherwise the overall precision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matches external/swisseph/setest/checkpoints.c:10-19 exactly:
    /// <c>strncmp(name,"xx", 2) == 0 &amp;&amp; i &lt; 6</c> -- a prefix test, not an
    /// exact-name test. The exact-name form lives in a different function
    /// (check_equals_dd, checkpoints.c:147) that no suite actually calls;
    /// testsuite.m4 maps CHECK_DD to the prefix-matching check_dd.
    /// </para>
    /// <para>
    /// Since v2.10.04 (f952691) that precision is only the fallback: check_d_internal ends in
    /// check_equals_d_internal (checkpoints.c:129), which asks tolerance_for (tolerances.c) first and
    /// uses the t.fix precision only for names that function does not classify (it returns -1).
    /// For the classified names, which is nearly all of the corpus, the per-quantity tolerance
    /// decides and the t.fix precision never enters.
    /// </para>
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
    /// CHECK_EQUALS_D(actual, expected) -- a pure runtime self-consistency check of two computed
    /// doubles (a round trip), not file-backed. Mirrors external/swisseph/setest/checkpoints.c's
    /// check_equals_d (:116) and check_equals_d_internal (:129): the tolerance is the per-quantity
    /// one from tolerances.c when it classifies <paramref name="name"/> (evaluated at the second
    /// argument, the "expected" side), the testcase's overall precision otherwise; a NaN on either
    /// side fails.
    /// </summary>
    public void CheckEqualsD(string name, double actual, double expectedValue)
    {
        _comparisonCount++;
        CheckEqualsDInternal(name, actual, expectedValue, precision.All);
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

    private void CheckDInternal(string name, double actual, double fallbackPrecision)
    {
        _comparisonCount++;
        var exp = expected.GetDoubleCompared(name);
        CheckEqualsDInternal(name, actual, exp, fallbackPrecision);
    }

    // checkpoints.c:129 check_equals_d_internal. The harness never has -p parameters, so
    // ctx->precisions.from_parameters is false and tolerance_for always applies.
    private void CheckEqualsDInternal(string name, double act, double exp, double fallbackPrecision)
    {
        var tol = fallbackPrecision;
        var t = Tolerances.tolerance_for(name, exp, scope.SuiteId, scope.TestCaseId, expected.PeekInt("iflag") ?? 0);
        if (t >= 0)
        {
            tol = t;
        }

        if (double.IsNaN(act) || double.IsNaN(exp) || Math.Abs(exp - act) > tol)
        {
            _mismatches.Add(new FieldMismatch(
                name,
                exp.ToString("G17", CultureInfo.InvariantCulture),
                act.ToString("G17", CultureInfo.InvariantCulture),
                exp - act));
        }
    }

    private static string EscapeNewlines(string value) => value.Replace("\n", "\\n");
}
