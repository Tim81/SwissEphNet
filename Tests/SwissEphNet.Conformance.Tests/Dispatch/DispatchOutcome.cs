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

    public static DispatchOutcome Passed() => new() { Kind = OutcomeKind.Passed };

    public static DispatchOutcome NotImplemented(string reason) =>
        new() { Kind = OutcomeKind.NotImplemented, Reason = reason };

    public static DispatchOutcome DataMissing(string reason) =>
        new() { Kind = OutcomeKind.DataMissing, Reason = reason };

    public static DispatchOutcome Error(string reason) =>
        new() { Kind = OutcomeKind.Error, Reason = reason };

    public static DispatchOutcome Unreproducible(string reason) =>
        new() { Kind = OutcomeKind.Unreproducible, Reason = reason };

    public static DispatchOutcome FromMismatches(IReadOnlyList<FieldMismatch> mismatches) =>
        mismatches.Count == 0
            ? Passed()
            : new DispatchOutcome { Kind = OutcomeKind.ValueMismatch, Mismatches = mismatches };
}
