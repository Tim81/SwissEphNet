using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// What a comparison needs to know about where it runs: the suite and testcase ids (setest's
/// <c>ctx->current.suite.id</c> and <c>ctx->current.testcase.id</c>, which tolerances.c classifies
/// by) and the testcase's t.fix precision (setest's <c>ctx->precisions</c>).
/// </summary>
public readonly record struct TestCaseScope(int SuiteId, int TestCaseId, Precision Precision);
