using System.Collections.Generic;
using SwissEphNet.Conformance.Tests.Dispatch;
using SwissEphNet.Conformance.Tests.KnownFail;

namespace SwissEphNet.Conformance.Tests;

public sealed record IterationResult(
    IterationKey Key,
    string SuiteDescription,
    string? TestCaseDescription,
    OutcomeKind Kind,
    string? Reason,
    IReadOnlyList<FieldMismatch> Mismatches);
