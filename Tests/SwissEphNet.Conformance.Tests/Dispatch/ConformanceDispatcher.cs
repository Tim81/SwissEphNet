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

    // Suites 1, 6, 7, 9 and 10 declare file-scope statics (xx, cusps, xxnasc, xxtret, tret, ...) shared
    // by every iteration and testcase too; see each class's remarks.
    private readonly Suite01Calc _suite01 = new();
    private readonly Suite06Houses _suite06 = new();
    private readonly Suite07Apsides _suite07 = new();
    private readonly Suite09Rise _suite09 = new();
    private readonly Suite10Solcross _suite10 = new();

    // Suite 8's xxtret/xxgeopos/xxattr are suite-scoped C locals too (see
    // Suite08Eclipses' remarks) -- some testcases' expected values are pure
    // carry-over from a previous testcase's last call.
    private readonly Suite08Eclipses _suite08 = new();

    // Suite 11's working arrays are file-scope statics shared by every testcase too (see
    // Suite11PhenoAzalt's remarks).
    private readonly Suite11PhenoAzalt _suite11 = new();

    public DispatchOutcome Dispatch(SwissEph swe, ExpTestSuite suite, ExpTestCase testCase, ExpIteration iteration, Precision precision)
    {
        var scope = new TestCaseScope(suite.Id, testCase.Id, precision);
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

            if (ipl is not null && EphemerisFileResolver.NeedsAsteroidFileWeDoNotShip(ipl.Value))
            {
                return DispatchOutcome.DataMissing(
                    $"{plField}={ipl.Value} is a numbered asteroid beyond the four with built-in orbital elements " +
                    "(Ceres/Pallas/Juno/Vesta), which requires a per-asteroid file (e.g. se00433s.se1) this repo " +
                    "does not ship at any tier.");
            }
        }

        var iflagForCenterBody = f.TryGetInt("iflag");
        if (iflagForCenterBody is not null && EphemerisFileResolver.NeedsCenterBodySatFileWeDoNotHave(iflagForCenterBody.Value))
        {
            return DispatchOutcome.DataMissing(
                "iflag includes SEFLG_CENTER_BODY, which reads a per-planet ephe/sat/ record (e.g. sepm9599.se1) " +
                "even for a major-planet ipl, not shipped by default. Set SWISSEPH_CONFORMANCE_INCLUDE_MOONS=1 " +
                "and provide ephe/sat/ to opt in.");
        }

        return suite.Id switch
        {
            1 => _suite01.Dispatch(swe, scope, iteration),
            2 => _suite02.Dispatch(swe, scope, iteration),
            3 => Suite03Misc.Dispatch(swe, scope, iteration),
            4 => Suite04Ayanamsa.Dispatch(swe, scope, iteration),
            5 => Suite05DateTime.Dispatch(swe, scope, iteration),
            6 => _suite06.Dispatch(swe, scope, iteration),
            7 => _suite07.Dispatch(swe, scope, iteration),
            8 => _suite08.Dispatch(swe, scope, iteration),
            9 => _suite09.Dispatch(swe, scope, iteration),
            10 => _suite10.Dispatch(swe, scope, iteration),
            11 => _suite11.Dispatch(swe, scope, iteration),
            _ => DispatchOutcome.Error($"Unknown suite id {suite.Id}."),
        };
    }

    private static readonly string[] PlanetFields = ["ipl", "iplctr"];

    /// <summary>
    /// Testcases whose target function does not exist in SwissEphNet at all --
    /// true regardless of what data the iteration needs, so checked before any
    /// data-availability check. The port now tracks the same pinned C tag
    /// (v2.10.04) the conformance corpus targets, so this switch currently
    /// has no cases; it stays as the mechanism for the next API gap, not dead code.
    /// </summary>
    private static bool TryGetStaticNotImplementedReason(int suiteId, int testCaseId, out string reason)
    {
        switch (suiteId, testCaseId)
        {
            default:
                reason = "";
                return false;
        }
    }
}
