using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Maps each (TESTSUITE id, TESTCASE id) to the SwissEphNet call it exercises,
/// derived from external/swisseph/setest/suite_NN_*.c (not from the
/// human-readable section-descr strings).
/// </summary>
public sealed class ConformanceDispatcher
{
    // Suite 2 has no SETUP block; "iflag" is a suite-scoped C local that
    // carries over between testcases (see Suite02FixStar's remarks). One
    // instance per corpus run reproduces that.
    private readonly Suite02FixStar _suite02 = new();

    public DispatchOutcome Dispatch(SwissEph swe, ExpTestSuite suite, ExpTestCase testCase, ExpIteration iteration, Precision precision)
    {
        if (TryGetStaticNotImplementedReason(suite.Id, testCase.Id, out var staticReason))
        {
            return DispatchOutcome.NotImplemented(staticReason);
        }

        var f = iteration.Fields;

        var iephe = f.TryGetInt("iephe");
        if (iephe is not null && EphemerisFileResolver.NeedsJplDataWeDoNotHave(iephe.Value))
        {
            return DispatchOutcome.DataMissing(
                "iephe includes SEFLG_JPLEPH, which requires a multi-hundred-MB DE file this repo does not ship. " +
                "Set SWISSEPH_CONFORMANCE_INCLUDE_JPL=1 and SWISSEPH_CONFORMANCE_JPL_FILE to opt in.");
        }

        foreach (var plField in PlanetFields)
        {
            var ipl = f.TryGetInt(plField);
            if (ipl is not null && EphemerisFileResolver.NeedsMoonDataWeDoNotHave(ipl.Value))
            {
                return DispatchOutcome.DataMissing(
                    $"{plField}={ipl.Value} is a planetary-moon body, which requires ephe/sat/ (227 MB), not shipped by default. " +
                    "Set SWISSEPH_CONFORMANCE_INCLUDE_MOONS=1 and provide ephe/sat/ to opt in.");
            }
        }

        return suite.Id switch
        {
            1 => Suite01Calc.Dispatch(swe, testCase.Id, iteration, precision),
            2 => _suite02.Dispatch(swe, testCase.Id, iteration, precision),
            3 => Suite03Misc.Dispatch(swe, testCase.Id, iteration, precision),
            4 => Suite04Ayanamsa.Dispatch(swe, testCase.Id, iteration, precision),
            5 => Suite05DateTime.Dispatch(swe, testCase.Id, iteration, precision),
            6 => Suite06Houses.Dispatch(swe, testCase.Id, iteration, precision),
            7 => Suite07Apsides.Dispatch(swe, testCase.Id, iteration, precision),
            8 => Suite08Eclipses.Dispatch(swe, testCase.Id, iteration, precision),
            9 => Suite09Rise.Dispatch(swe, testCase.Id, iteration, precision),
            10 => Suite10Solcross.Dispatch(testCase.Id, iteration),
            _ => DispatchOutcome.Error($"Unknown suite id {suite.Id}."),
        };
    }

    private static readonly string[] PlanetFields = ["ipl", "iplctr"];

    /// <summary>
    /// Testcases whose target function does not exist in SwissEphNet at all
    /// (2.10-only additions; port is at 2.08) -- true regardless of what data
    /// the iteration needs, so checked before any data-availability check.
    /// </summary>
    private static bool TryGetStaticNotImplementedReason(int suiteId, int testCaseId, out string reason)
    {
        switch (suiteId, testCaseId)
        {
            case (1, 5):
                reason = "swe_calc_pctr is not implemented in SwissEphNet (port is at 2.08; added in 2.10).";
                return true;
            case (6, 8):
                reason = "swe_houses_ex2 is not implemented in SwissEphNet (port is at 2.08; added in 2.10).";
                return true;
            case (6, 9):
                reason = "swe_houses_armc_ex2 is not implemented in SwissEphNet (port is at 2.08; added in 2.10).";
                return true;
            case (10, _):
                reason = "the swe_solcross/swe_mooncross/swe_helio_cross family is not implemented in SwissEphNet (port is at 2.08; added in 2.10).";
                return true;
            default:
                reason = "";
                return false;
        }
    }
}
