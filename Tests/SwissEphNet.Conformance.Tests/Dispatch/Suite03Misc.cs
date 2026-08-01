using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 3: "Various small functions (names et al.)" (external/swisseph/setest/suite_03_misc.c).</summary>
internal static class Suite03Misc
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;
        var ctx = new CheckContext(f, precision);

        switch (testCaseId)
        {
            case 1:
            {
                var ipl = f.GetInt("ipl");
                var name = swe.swe_get_planet_name(ipl);
                ctx.CheckS("name", name);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 2:
            {
                var sidMode = f.GetInt("sid_mode");
                var name = swe.swe_get_ayanamsa_name(sidMode);
                ctx.CheckS("name", name);
                return DispatchOutcome.FromMismatches(ctx);
            }

            default:
                return DispatchOutcome.Error($"Suite 3 has no testcase {testCaseId}.");
        }
    }
}
