using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Suite 10: "Various swe_*cross calls" (external/swisseph/setest/suite_10_solcross.c).
/// The entire swe_solcross / swe_mooncross / swe_helio_cross family is
/// 2.10-only and not yet ported (port is at 2.08), so every testcase in this
/// suite is NOT-IMPLEMENTED.
/// </summary>
internal static class Suite10Solcross
{
    private static readonly string[] FunctionNames =
    [
        "swe_solcross",
        "swe_solcross_ut",
        "swe_mooncross",
        "swe_mooncross_ut",
        "swe_mooncross_node",
        "swe_mooncross_node_ut",
        "swe_helio_cross",
        "swe_helio_cross_ut",
    ];

    public static DispatchOutcome Dispatch(int testCaseId, ExpIteration iteration)
    {
        if (testCaseId is < 1 or > 8)
        {
            return DispatchOutcome.Error($"Suite 10 has no testcase {testCaseId}.");
        }

        var name = FunctionNames[testCaseId - 1];
        return DispatchOutcome.NotImplemented($"{name} is not implemented in SwissEphNet (port is at 2.08; added in 2.10).");
    }
}
