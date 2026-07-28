using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 7: "Apsides and orbital elements functions" (external/swisseph/setest/suite_07_apsides.c).</summary>
internal static class Suite07Apsides
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        // SETUP: read every iteration.
        var iflag = f.GetInt("iflag");
        var iephe = f.GetInt("iephe");

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var method = f.GetInt("method");
                var xxnasc = new double[6];
                var xxndsc = new double[6];
                var xxperi = new double[6];
                var xxaphe = new double[6];
                string serr = "";
                var rc = swe.swe_nod_aps(jd, ipl, iflag | iephe, method, xxnasc, xxndsc, xxperi, xxaphe, ref serr);
                return CheckNodAps(f, precision, rc, serr, xxnasc, xxndsc, xxperi, xxaphe);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var method = f.GetInt("method");
                var xxnasc = new double[6];
                var xxndsc = new double[6];
                var xxperi = new double[6];
                var xxaphe = new double[6];
                string serr = "";
                var rc = swe.swe_nod_aps_ut(jd, ipl, iflag | iephe, method, xxnasc, xxndsc, xxperi, xxaphe, ref serr);
                return CheckNodAps(f, precision, rc, serr, xxnasc, xxndsc, xxperi, xxaphe);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                // Reference tool declares "double ... xxdret[20]"; only the
                // first 17 are checked (CHECK_DD(xxdret,17)), but the buffer
                // passed to swe_get_orbital_elements must be the full 20.
                var xxdret = new double[20];
                string serr = "";
                var rc = swe.swe_get_orbital_elements(jd, ipl, iflag | iephe, xxdret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckDD("xxdret", xxdret[..17]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                double dmax = 0, dmin = 0, dtrue = 0;
                string serr = "";
                var rc = swe.swe_orbit_max_min_true_distance(jd, ipl, iflag | iephe, ref dmax, ref dmin, ref dtrue, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckD("dmax", dmax);
                ctx.CheckD("dmin", dmin);
                ctx.CheckD("dtrue", dtrue);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 7 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckNodAps(ExpFields f, Precision precision, int rc, string serr, double[] xxnasc, double[] xxndsc, double[] xxperi, double[] xxaphe)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        ctx.CheckDD("xxnasc", xxnasc);
        ctx.CheckDD("xxndsc", xxndsc);
        ctx.CheckDD("xxperi", xxperi);
        ctx.CheckDD("xxaphe", xxaphe);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }
}
