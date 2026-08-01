using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 1: "Various swe_calc calls in different modes" (external/swisseph/setest/suite_01_calc.c).</summary>
internal static class Suite01Calc
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        switch (testCaseId)
        {
            case 1:
            {
                // SETUP: iflag, iephe, jd read every iteration.
                var iflag = f.GetInt("iflag");
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_calc(jd, ipl, iflag | iephe, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 2:
            {
                var iflag = f.GetInt("iflag");
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_calc_ut(jd, ipl, iflag | iephe, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 3:
            {
                var iflag = f.GetInt("iflag");
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                var geolon = f.GetDouble("geolon");
                var geolat = f.GetDouble("geolat");
                var altitude = f.GetDouble("altitude");
                var ipl = f.GetInt("ipl");
                swe.swe_set_topo(geolon, geolat, altitude);
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_calc(jd, ipl, iflag | iephe, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 4:
            {
                // "swe_calc( ) - Equatorial followed by Ecliptic": a pure
                // runtime self-consistency check (CHECK_EQUALS_I(rc,iflag)),
                // not compared against a stored expected value.
                //
                // SETUP unconditionally reads "iflag" every iteration in the
                // reference C, regardless of which testcase runs -- this
                // testcase's own body never uses that value (it computes its
                // own local iflag from iephe instead), but t.exp still
                // carries the field because SETUP recorded it. Read it here
                // too, purely so the harness completeness guard's per-field
                // half (ConformanceRunner.UnconsumedKeys) sees it as accounted
                // for rather than silently unchecked.
                //
                // This is a plain, discarded field read -- structurally the
                // same shape as the regression the guard's other half
                // (CheckContext.AnyComparisonPerformed) exists to catch (see
                // Suite03Misc testcase 1). It stays legitimate here, and does
                // not need to route through a Check* call instead, because
                // "iflag" genuinely has nothing to compare it against (SETUP
                // records it, but no CHECK_* macro in the reference ever reads
                // it back) and because this testcase still performs a real
                // comparison below (CheckEqualsI), so AnyComparisonPerformed
                // is satisfied independently of this line.
                _ = f.GetInt("iflag");
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var xx = new double[6];
                string serr = "";
                var iflag = SwissEph.SEFLG_EQUATORIAL | iephe;
                swe.swe_calc(jd, ipl, iflag, xx, ref serr); // discarded, matches upstream
                iflag = iephe;
                var rc = swe.swe_calc(jd, ipl, iflag, xx, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckEqualsI("rc==iflag", rc, iflag);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 5:
            {
                var iflag = f.GetInt("iflag");
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var iplctr = f.GetInt("iplctr");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_calc_pctr(jd, ipl, iplctr, iflag | iephe, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            default:
                return DispatchOutcome.Error($"Suite 1 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckCalc(ExpFields f, Precision precision, int rc, double[] xx, string serr)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckDD("xx", xx);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }
}
