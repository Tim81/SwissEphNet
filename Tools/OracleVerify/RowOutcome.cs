namespace OracleVerify;

/// <summary>One value field where the C and .NET dumps decode to different doubles.</summary>
internal sealed record FieldDiff(int Index, string Label, double CValue, double NetValue, ulong Ulp);

/// <summary>
/// Which part of "every hex column, the integer return code, and the serr text are equal" a
/// non-matching row failed on. Only meaningful when <see cref="RowOutcome.Matches"/> is false.
///
/// This is a coarser signal than <see cref="DiffCategory"/> on purpose: a hex-only difference can
/// be recorded as either <see cref="DiffCategory.PortVersion"/> or
/// <see cref="DiffCategory.LibmResidual"/>, a distinction this comparer cannot make on its own --
/// telling the two apart means tracing the row to a specific named C runtime function and citing
/// its pinned ULP bound (see scripts/verify-crt-parity.ps1), a one-time act of human investigation,
/// not something recomputed from the dumps on every run. What the comparer CAN check mechanically
/// is whether a row's category is even the right *shape* for what is currently failing -- a row
/// recorded as RETC whose retc now matches (shape flipped to HexOnlyDiffers, or vanished
/// entirely) is drift, exactly as much as a VALUE-MISMATCH degrading into an ERROR is drift in
/// ConformanceReport.
///
/// The three values are checked in priority order (retc, then hex, then serr): a row whose retc
/// differs is <see cref="RetcDiffers"/> even if its serr text also differs, and a row whose hex
/// differs is <see cref="HexOnlyDiffers"/> even if its serr text also differs. <see cref="ErrOnlyDiffers"/>
/// is reserved for the case that actually needs its own category -- retc and every hex column
/// already agree, and only the diagnostic string does not.
/// </summary>
internal enum FailureShape
{
    HexOnlyDiffers,
    RetcDiffers,
    ErrOnlyDiffers,
}

internal sealed class RowOutcome
{
    public required string CaseId { get; init; }
    public required int CRetc { get; init; }
    public required int NetRetc { get; init; }
    public required string CErr { get; init; }
    public required string NetErr { get; init; }
    public required IReadOnlyList<FieldDiff> FieldDiffs { get; init; }

    public bool RetcMatches => CRetc == NetRetc;
    public bool HexMatches => FieldDiffs.Count == 0;

    /// <summary>
    /// Ordinal, matching Tests/SwissEphNet.Conformance.Tests/Dispatch/CheckContext.cs's CheckS --
    /// serr is part of the API contract, not prose a culture-aware comparison should normalize.
    /// </summary>
    public bool ErrMatches => string.Equals(CErr, NetErr, StringComparison.Ordinal);

    /// <summary>The full pass condition: hex columns, the return code, and the serr text are all equal.</summary>
    public bool Matches => RetcMatches && HexMatches && ErrMatches;

    /// <summary>
    /// The largest distance among <see cref="FieldDiffs"/> that is not categorical (see
    /// <see cref="UlpMath.CategoricalDistance"/>), or 0 if there are no field diffs or all of them
    /// are categorical. Only meaningful together with <see cref="HasCategoricalFieldDiff"/> --
    /// see <see cref="KnownDiffEntry.MaxUlp"/>'s remarks for why a categorical diff is never
    /// folded into this number.
    /// </summary>
    public ulong MaxUlp => FieldDiffs.Count == 0
        ? 0UL
        : FieldDiffs
            .Where(f => f.Ulp != UlpMath.CategoricalDistance)
            .Select(f => f.Ulp)
            .DefaultIfEmpty(0UL)
            .Max();

    /// <summary>Whether at least one field differs categorically (a NaN on one side, a finite value on the other).</summary>
    public bool HasCategoricalFieldDiff => FieldDiffs.Any(f => f.Ulp == UlpMath.CategoricalDistance);

    public FailureShape Shape =>
        !RetcMatches ? FailureShape.RetcDiffers :
        !HexMatches ? FailureShape.HexOnlyDiffers :
        FailureShape.ErrOnlyDiffers;
}

internal static class RowComparer
{
    public static RowOutcome Compare(DumpRow c, DumpRow net)
    {
        if (!string.Equals(c.CaseId, net.CaseId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"internal: Compare called with mismatched case ids '{c.CaseId}' / '{net.CaseId}'.");
        }

        if (c.Values.Count != net.Values.Count)
        {
            throw new FormatException(
                $"{c.CaseId}: value-field count differs between the two dumps (c={c.Values.Count}, net={net.Values.Count}).");
        }

        var func = c.CaseId.Split('|')[0];
        var labels = FieldLabels.For(func, c.CaseId);
        if (labels.Count != c.Values.Count)
        {
            throw new FormatException(
                $"{c.CaseId}: func '{func}' expects {labels.Count} value field(s), row has {c.Values.Count}.");
        }

        var diffs = new List<FieldDiff>();
        for (var i = 0; i < c.Values.Count; i++)
        {
            var cv = c.Values[i];
            var nv = net.Values[i];
            var ulp = UlpMath.Distance(cv, nv);
            if (ulp != 0)
            {
                diffs.Add(new FieldDiff(i, labels[i], cv, nv, ulp));
            }
        }

        return new RowOutcome
        {
            CaseId = c.CaseId,
            CRetc = c.Retc,
            NetRetc = net.Retc,
            CErr = c.Err,
            NetErr = net.Err,
            FieldDiffs = diffs,
        };
    }
}
