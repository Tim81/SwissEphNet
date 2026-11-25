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
    IReadOnlyList<FieldMismatch> Mismatches,
    PinContext? Pin = null);

/// <summary>
/// What the pin gate needs to re-derive setest's own tolerance for a field of this iteration:
/// the iteration's "iflag" entry (Tolerances.tolerance_for) and the testcase's t.fix precision
/// (the fallback CheckContext uses for names tolerances.c does not classify).
/// </summary>
public sealed record PinContext(int Iflag, Corpus.Precision Precision);
