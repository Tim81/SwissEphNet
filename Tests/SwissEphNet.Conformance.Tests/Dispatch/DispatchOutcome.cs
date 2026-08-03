using System.Collections.Generic;

namespace SwissEphNet.Conformance.Tests.Dispatch;

public enum OutcomeKind
{
    Passed,
    ValueMismatch,
    NotImplemented,
    DataMissing,
    Error,

    /// <summary>
    /// The reference call cannot be faithfully reproduced given a
    /// representational mismatch between C and C#, independent of any bug in
    /// either the port or this harness. See Suite06Houses' remarks on
    /// testcase 6 (swe_house_pos): the reference C passes one `int hsys` that
    /// is read raw by an early toupper/switch check and only truncated to a
    /// char later, deep inside a nested call -- two different effective
    /// values from one C variable. A single C# `char` argument cannot supply
    /// both, so no argument choice reproduces the reference behavior in every
    /// case.
    /// </summary>
    Unreproducible,
}

public sealed record FieldMismatch(string Name, string Expected, string Actual, double? Diff);

public sealed class DispatchOutcome
{
    public required OutcomeKind Kind { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<FieldMismatch> Mismatches { get; init; } = [];

    /// <summary>
    /// Whether the <see cref="CheckContext"/> that produced this outcome (via
    /// <see cref="FromMismatches(CheckContext)"/>) actually performed at least
    /// one comparison. False only for a mis-implemented dispatch case whose
    /// <c>CheckContext</c> was constructed but never had a Check* method
    /// called on it -- see <see cref="Dispatch.CheckContext.AnyComparisonPerformed"/>.
    /// Always true for an outcome built any other way (Passed/NotImplemented/
    /// DataMissing/Error/Unreproducible never claim to have compared t.exp).
    /// </summary>
    public bool AnyComparisonPerformed { get; init; } = true;

    public static DispatchOutcome Passed() => new() { Kind = OutcomeKind.Passed };

    public static DispatchOutcome NotImplemented(string reason) =>
        new() { Kind = OutcomeKind.NotImplemented, Reason = reason };

    public static DispatchOutcome DataMissing(string reason) =>
        new() { Kind = OutcomeKind.DataMissing, Reason = reason };

    public static DispatchOutcome Error(string reason) =>
        new() { Kind = OutcomeKind.Error, Reason = reason };

    public static DispatchOutcome Unreproducible(string reason) =>
        new() { Kind = OutcomeKind.Unreproducible, Reason = reason };

    /// <summary>
    /// Builds a Passed/ValueMismatch outcome from a finished <see cref="CheckContext"/>,
    /// carrying forward <see cref="CheckContext.AnyComparisonPerformed"/> so the
    /// completeness guard in <c>ConformanceRunner.Run</c> can tell a genuine
    /// pass (something was compared and matched) apart from a dispatch case
    /// that dropped its only comparison and defaulted to "no mismatches found".
    /// </summary>
    public static DispatchOutcome FromMismatches(CheckContext ctx) =>
        ctx.Mismatches.Count == 0
            ? new DispatchOutcome { Kind = OutcomeKind.Passed, AnyComparisonPerformed = ctx.AnyComparisonPerformed }
            : new DispatchOutcome { Kind = OutcomeKind.ValueMismatch, Mismatches = ctx.Mismatches, AnyComparisonPerformed = ctx.AnyComparisonPerformed };
}
