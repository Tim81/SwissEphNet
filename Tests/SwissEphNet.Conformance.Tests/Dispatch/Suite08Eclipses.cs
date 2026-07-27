using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 8: "Eclipses" (external/swisseph/setest/suite_08_eclipses.c).</summary>
/// <remarks>
/// Buffers are always allocated at the reference tool's declared local sizes
/// ("double jd, xxtret[10], xxgeopos[3], xxattr[20];"), not at whatever
/// smaller count a given testcase's CHECK_DD call happens to compare -- the
/// underlying SwissEphNet calls (mechanically ported from the same C) can
/// write past a checked-count-sized buffer, which is exactly what an
/// undersized array here surfaced as an IndexOutOfRangeException.
/// </remarks>
internal static class Suite08Eclipses
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
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
                var xxtret = new double[10];
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_glob(jd, f.GetInt("iephe"), f.GetInt("ifltype"), xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var xxgeopos = new double[3];
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_sol_eclipse_where(jd, f.GetInt("iephe"), xxgeopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxgeopos", xxgeopos[..2]);
                ctx.CheckDD("xxattr", xxattr[..8]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxtret = new double[10];
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret[..7]);
                ctx.CheckDD("xxattr", xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_sol_eclipse_how(jd, f.GetInt("iephe"), geopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 5:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                var xxtret = new double[10];
                string serr = "";
                var rc = swe.swe_lun_occult_when_glob(jd, ipl, star, f.GetInt("iephe"), f.GetInt("ifltype"), xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 6:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                var xxgeopos = new double[3];
                var xxattr = new double[20];
                string serr = "";
                swe.swe_lun_occult_where(jd, ipl, star, f.GetInt("iephe"), xxgeopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckDD("xxgeopos", xxgeopos[..2]);
                ctx.CheckDD("xxattr", xxattr[..8]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 7:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                var xxtret = new double[10];
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_lun_occult_when_loc(jd, ipl, star, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret[..7]);
                ctx.CheckDD("xxattr", xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 8:
            {
                var jd = f.GetDouble("jd");
                var xxtret = new double[10];
                string serr = "";
                var rc = swe.swe_lun_eclipse_when(jd, f.GetInt("iephe"), f.GetInt("ifltype"), xxtret, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
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
                var xxtret = new double[10];
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                ctx.CheckDD("xxattr", xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 10:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxattr = new double[20];
                string serr = "";
                var rc = swe.swe_lun_eclipse_how(jd, f.GetInt("iephe"), geopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", xxattr[..11]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 8 has no testcase {testCaseId}.");
        }
    }
}
