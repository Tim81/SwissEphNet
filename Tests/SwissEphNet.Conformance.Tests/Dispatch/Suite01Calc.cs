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
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 5:
                // swe_calc_pctr: 2.10-only, not yet ported.
                return DispatchOutcome.NotImplemented("swe_calc_pctr is not implemented in SwissEphNet (port is at 2.08; added in 2.10).");

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
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }
}
