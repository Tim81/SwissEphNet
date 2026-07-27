namespace SwissEphNet.Conformance.Tests.KnownFail;

public enum FailureCategory
{
    NotImplemented,
    ValueMismatch,
    DataMissing,
    Error,
}

public static class FailureCategoryNames
{
    public const string NotImplemented = "NOT-IMPLEMENTED";
    public const string ValueMismatch = "VALUE-MISMATCH";
    public const string DataMissing = "DATA-MISSING";
    public const string Error = "ERROR";

    public static string ToName(FailureCategory category) => category switch
    {
        FailureCategory.NotImplemented => NotImplemented,
        FailureCategory.ValueMismatch => ValueMismatch,
        FailureCategory.DataMissing => DataMissing,
        FailureCategory.Error => Error,
        _ => throw new System.ArgumentOutOfRangeException(nameof(category)),
    };

    public static FailureCategory Parse(string name) => name switch
    {
        NotImplemented => FailureCategory.NotImplemented,
        ValueMismatch => FailureCategory.ValueMismatch,
        DataMissing => FailureCategory.DataMissing,
        Error => FailureCategory.Error,
        _ => throw new System.FormatException($"Unknown failure category '{name}'."),
    };
}

public readonly record struct IterationKey(int Suite, int TestCase, int Iteration)
{
    public override string ToString() => $"{Suite}.{TestCase}.{Iteration}";
}

public sealed record KnownFailEntry(IterationKey Key, FailureCategory Category, string Reason);
