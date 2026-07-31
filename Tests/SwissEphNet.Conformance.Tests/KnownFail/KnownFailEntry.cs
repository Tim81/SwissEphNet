using System;
using SwissEphNet.Conformance.Tests.Dispatch;

namespace SwissEphNet.Conformance.Tests.KnownFail;

public enum FailureCategory
{
    NotImplemented,
    ValueMismatch,
    DataMissing,
    Error,
    Unreproducible,
}

public static class FailureCategoryNames
{
    public const string NotImplemented = "NOT-IMPLEMENTED";
    public const string ValueMismatch = "VALUE-MISMATCH";
    public const string DataMissing = "DATA-MISSING";
    public const string Error = "ERROR";
    public const string Unreproducible = "UNREPRODUCIBLE";

    public static string ToName(FailureCategory category) => category switch
    {
        FailureCategory.NotImplemented => NotImplemented,
        FailureCategory.ValueMismatch => ValueMismatch,
        FailureCategory.DataMissing => DataMissing,
        FailureCategory.Error => Error,
        FailureCategory.Unreproducible => Unreproducible,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static FailureCategory Parse(string name) => name switch
    {
        NotImplemented => FailureCategory.NotImplemented,
        ValueMismatch => FailureCategory.ValueMismatch,
        DataMissing => FailureCategory.DataMissing,
        Error => FailureCategory.Error,
        Unreproducible => FailureCategory.Unreproducible,
        _ => throw new FormatException($"Unknown failure category '{name}'."),
    };

    /// <summary>
    /// The FailureCategory a live dispatch <see cref="OutcomeKind"/> maps to.
    /// The gate (ConformanceReport) uses this to catch a known-fail entry's
    /// recorded category silently rotting -- e.g. a VALUE-MISMATCH that has
    /// degraded into an ERROR crash, or vice versa -- which key-membership
    /// alone would let through as "still on the list, still failing".
    /// <see cref="OutcomeKind.Passed"/> has no failure category; callers must
    /// not ask for one.
    /// </summary>
    public static FailureCategory FromOutcomeKind(OutcomeKind kind) => kind switch
    {
        OutcomeKind.NotImplemented => FailureCategory.NotImplemented,
        OutcomeKind.ValueMismatch => FailureCategory.ValueMismatch,
        OutcomeKind.DataMissing => FailureCategory.DataMissing,
        OutcomeKind.Error => FailureCategory.Error,
        OutcomeKind.Unreproducible => FailureCategory.Unreproducible,
        OutcomeKind.Passed => throw new InvalidOperationException("Passed has no failure category."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public readonly record struct IterationKey(int Suite, int TestCase, int Iteration)
{
    public override string ToString() => $"{Suite}.{TestCase}.{Iteration}";
}

/// <summary>
/// <paramref name="MagnitudeKey"/> is
/// <see cref="SwissEphNet.Conformance.Tests.KnownFail.MagnitudeKey.NotApplicable"/> for every
/// category other than <see cref="FailureCategory.ValueMismatch"/> -- see
/// <see cref="SwissEphNet.Conformance.Tests.KnownFail.MagnitudeKey"/>'s remarks for what it means
/// and how it is computed, and <see cref="ConformanceReport"/> for how it is compared against a
/// live run's own computation to catch magnitude drift a bare category comparison cannot.
/// </summary>
public sealed record KnownFailEntry(IterationKey Key, FailureCategory Category, string MagnitudeKey, string Reason);
