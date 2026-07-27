using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 8: "Eclipses" (external/swisseph/setest/suite_08_eclipses.c).</summary>
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
                var xxgeopos = new double[2];
                var xxattr = new double[8];
                string serr = "";
                var rc = swe.swe_sol_eclipse_where(jd, f.GetInt("iephe"), xxgeopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxgeopos", xxgeopos);
                ctx.CheckDD("xxattr", xxattr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxtret = new double[7];
                var xxattr = new double[11];
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                ctx.CheckDD("xxattr", xxattr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxattr = new double[11];
                string serr = "";
                var rc = swe.swe_sol_eclipse_how(jd, f.GetInt("iephe"), geopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", xxattr);
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
                var xxgeopos = new double[2];
                var xxattr = new double[8];
                string serr = "";
                swe.swe_lun_occult_where(jd, ipl, star, f.GetInt("iephe"), xxgeopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckDD("xxgeopos", xxgeopos);
                ctx.CheckDD("xxattr", xxattr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 7:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                var xxtret = new double[7];
                var xxattr = new double[11];
                string serr = "";
                var rc = swe.swe_lun_occult_when_loc(jd, ipl, star, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                ctx.CheckDD("xxattr", xxattr);
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
                // swe_sol_eclipse_when_loc again (with a 10-element xxtret this
                // time, not 7). This is reproduced verbatim: t.exp's recorded
                // values were generated by that exact call, bug or not.
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxtret = new double[10];
                var xxattr = new double[11];
                string serr = "";
                var rc = swe.swe_sol_eclipse_when_loc(jd, f.GetInt("iephe"), geopos, xxtret, xxattr, f.GetInt("backward") != 0, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret);
                ctx.CheckDD("xxattr", xxattr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 10:
            {
                var jd = f.GetDouble("jd");
                var geopos = new[] { geolon, geolat, altitude };
                var xxattr = new double[11];
                string serr = "";
                var rc = swe.swe_lun_eclipse_how(jd, f.GetInt("iephe"), geopos, xxattr, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxattr", xxattr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 8 has no testcase {testCaseId}.");
        }
    }
}
