using System.Collections.Generic;

namespace SwissEphNet.Conformance.Tests.Dispatch;

public enum OutcomeKind
{
    Passed,
    ValueMismatch,
    NotImplemented,
    DataMissing,
    Error,
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

    public static DispatchOutcome FromMismatches(IReadOnlyList<FieldMismatch> mismatches) =>
        mismatches.Count == 0
            ? Passed()
            : new DispatchOutcome { Kind = OutcomeKind.ValueMismatch, Mismatches = mismatches };
}
