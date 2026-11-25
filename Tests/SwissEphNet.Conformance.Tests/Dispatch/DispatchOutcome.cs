using System.Collections.Generic;

namespace SwissEphNet.Conformance.Tests.Dispatch;

public enum OutcomeKind
{
    Passed,
    ValueMismatch,
    NotImplemented,
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

/// <summary>
/// How a compared field was checked: CheckD/CheckDD/CheckEqualsD (a double, within a tolerance),
/// CheckI/CheckEqualsI (an int, exactly) or CheckS (a string, exactly). Recorded so the pin gate
/// (<see cref="KnownFail.PinGate"/>) knows how to compare a field's value without guessing from its text.
/// </summary>
public enum FieldKind
{
    Double,
    Int,
    String,
}

/// <summary>
/// One failed comparison. For a file-backed check <see cref="Expected"/> is the t.exp value and
/// <see cref="Actual"/> the port's. For a runtime self-check (<paramref name="SelfCheck"/>, from
/// CHECK_EQUALS_D / CHECK_EQUALS_I) there is no t.exp side: <see cref="Actual"/> is the macro's first
/// operand and <see cref="Expected"/> its second, whichever of them the port computed (see
/// <see cref="KnownFail.PinOperand"/>).
/// </summary>
public sealed record FieldMismatch(string Name, string Expected, string Actual, double? Diff, FieldKind Kind = FieldKind.Double, bool SelfCheck = false);

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
    /// NotImplemented/Error/Unreproducible never claim to have compared t.exp).
    /// </summary>
    public bool AnyComparisonPerformed { get; init; } = true;

    public static DispatchOutcome Passed() => new() { Kind = OutcomeKind.Passed };

    public static DispatchOutcome NotImplemented(string reason) =>
        new() { Kind = OutcomeKind.NotImplemented, Reason = reason };

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
