using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 8: "Eclipses" (external/swisseph/setest/suite_08_eclipses.c).</summary>
/// <remarks>
/// <para>
/// suite_08_eclipses.c:5 declares "double jd, xxtret[10], xxgeopos[3],
/// xxattr[20];" once, as locals of testsuite_8() itself -- shared across
/// every testcase in the suite (nested-function closure, same as every other
/// TESTCASE in the same suite), not fresh per call. A testcase whose
/// underlying function does not write every element it declares leaves the
/// rest holding whatever a *previous* testcase's call put there, and t.exp's
/// recorded expectations reflect exactly that carry-over, not zero.
/// Concretely: TC9's xxtret[6]/[7] (all four iterations) are TC8's last
/// leftover values, and TC7's xxattr[10] (all eight iterations) is TC4's.
/// TC3 has the same exposure but is currently masked because the value it
/// would carry happens to be 0.0.
/// </para>
/// <para>
/// This class is therefore stateful (one instance per corpus run, the same
/// pattern <see cref="Suite02FixStar"/> uses for its carried "iflag") so that
/// carry-over is reproduced in testcase execution order, matching the
/// reference tool's actual behavior instead of a fresh, zeroed buffer every
/// call.
/// </para>
/// </remarks>
internal sealed class Suite08Eclipses
{
    private readonly double[] _xxtret = new double[10];
    private readonly double[] _xxgeopos = new double[3];
    private readonly double[] _xxattr = new double[20];

    public DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        // SETUP: read every iteration.
        var geolat = f.GetDouble("geolat");
        var geolon = f.GetDouble("geolon");
        var altitude = f.GetDouble("altitude");

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_glob(jd, f.GetInt("iephe"), f.GetInt("ifltype"), _xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                string serr = "";
                var rc = swe.swe_sol_eclipse_where(jd, f.GetInt("iephe"), _xxgeopos, _xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxgeopos", _xxgeopos[..2]);
                ctx.CheckDD("xxattr", _xxattr[..8]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, _xxtret, _xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret[..7]);
                ctx.CheckDD("xxattr", _xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                string serr = "";
                var rc = swe.swe_sol_eclipse_how(jd, f.GetInt("iephe"), geopos, _xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", _xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 5:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                string serr = "";
                var rc = swe.swe_lun_occult_when_glob(jd, ipl, star, f.GetInt("iephe"), f.GetInt("ifltype"), _xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 6:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                string serr = "";
                swe.swe_lun_occult_where(jd, ipl, star, f.GetInt("iephe"), _xxgeopos, _xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckDD("xxgeopos", _xxgeopos[..2]);
                ctx.CheckDD("xxattr", _xxattr[..8]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 7:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                string serr = "";
                var rc = swe.swe_lun_occult_when_loc(jd, ipl, star, f.GetInt("iephe"), geopos, _xxtret, _xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret[..7]);
                ctx.CheckDD("xxattr", _xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 8:
            {
                var jd = f.GetDouble("jd");
                string serr = "";
                var rc = swe.swe_lun_eclipse_when(jd, f.GetInt("iephe"), f.GetInt("ifltype"), _xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 9:
            {
                // Upstream's own suite_08_eclipses.c TESTCASE(9), despite being
                // titled "swe_lun_eclipse_when_loc( )", actually calls
                // swe_sol_eclipse_when_loc again (with all 10 of xxtret's
                // elements checked this time, not 7). This is reproduced
                // verbatim: t.exp's recorded values were generated by that
                // exact call, bug or not.
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, _xxtret, _xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", _xxtret);
                ctx.CheckDD("xxattr", _xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 10:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                string serr = "";
                var rc = swe.swe_lun_eclipse_how(jd, f.GetInt("iephe"), geopos, _xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", _xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            default:
                return DispatchOutcome.Error($"Suite 8 has no testcase {testCaseId}.");
        }
    }
}
