using System;
using System.Collections.Generic;
using System.Globalization;
using SwissEphNet.Conformance.Tests.Dispatch;

namespace SwissEphNet.Conformance.Tests.KnownFail;

/// <summary>
/// A per-row summary of how badly a VALUE-MISMATCH iteration missed, bucketed by decade so ULP
/// jitter does not move it while a genuine order-of-magnitude regression does.
///
/// known-fail.tsv's "reason" column is free text: a human-readable snapshot of the mismatch,
/// regenerated fresh every run and never compared against on a later one (see
/// <see cref="ConformanceReport.Drifted"/>'s remarks). A known-fail row whose category still
/// reads VALUE-MISMATCH but whose actual relative error grew by several orders of magnitude --
/// a fix that made one field worse while making the overall category unchanged -- was invisible
/// to the gate for exactly that reason: nothing compared the numbers, only the label. This is the
/// column that closes that gap without going all the way to comparing exact values, which would
/// fail on ordinary ULP-level noise between runs.
/// </summary>
public static class MagnitudeKey
{
    /// <summary>
    /// No comparable numeric field: every recorded mismatch is non-numeric (<see cref="FieldMismatch.Diff"/>
    /// is null -- CheckI/CheckEqualsI/CheckS), every numeric field's expected value is exactly
    /// zero (excluded -- see <see cref="Compute"/>), or the row has no mismatches at all (any
    /// category other than VALUE-MISMATCH). Written verbatim into known-fail.tsv's magnitude_key
    /// column; never parsed as a number.
    /// </summary>
    public const string NotApplicable = "n/a";

    /// <summary>
    /// floor(log10(relative error)) maximized over <paramref name="mismatches"/>, i.e. the worst
    /// (least negative / most positive) decade any single field missed by. A field is excluded
    /// from the max, not treated as zero, when: it has no numeric diff (<see cref="FieldMismatch.Diff"/>
    /// is null); its expected value is exactly 0 (relative error is undefined there, and t.exp
    /// carries genuinely run-dependent zeros -- e.g. a coordinate component that is exactly zero
    /// only for specific input angles -- that would otherwise make an unrelated field's ordinary
    /// noise register as "infinitely wrong"); or its relative error is not a finite positive
    /// number (defensive: a mismatch is only ever recorded when the compared values already
    /// differ, so this should not occur, but guards floor(log10(...)) against -Infinity/NaN
    /// rather than propagating either into the known-fail.tsv column). Returns
    /// <see cref="NotApplicable"/> when no field survives that filter.
    /// </summary>
    public static string Compute(IReadOnlyList<FieldMismatch> mismatches)
    {
        double? worstDecade = null;

        foreach (var mismatch in mismatches)
        {
            if (mismatch.Diff is not { } diff)
            {
                continue;
            }

            if (!double.TryParse(mismatch.Expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
            {
                continue;
            }

            if (expected == 0.0)
            {
                continue;
            }

            var relativeError = Math.Abs(diff) / Math.Abs(expected);
            if (!double.IsFinite(relativeError) || relativeError <= 0.0)
            {
                continue;
            }

            var decade = Math.Floor(Math.Log10(relativeError));
            if (worstDecade is null || decade > worstDecade.Value)
            {
                worstDecade = decade;
            }
        }

        return worstDecade is { } value
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : NotApplicable;
    }
}
