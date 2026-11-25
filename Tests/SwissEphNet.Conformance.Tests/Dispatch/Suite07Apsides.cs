using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 7: "Apsides and orbital elements functions" (external/swisseph/setest/suite_07_apsides.c).</summary>
/// <remarks>
/// suite_07_apsides.c:5 declares <c>xxnasc[6], xxndsc[6], xxperi[6], xxaphe[6], xxdret[20]</c> at file
/// scope, shared by every iteration and testcase, so a call that fails without writing them leaves the
/// previous iteration's values behind for CHECK_DD to record. Hence one instance per corpus run.
/// <c>dmax</c>, <c>dmin</c> and <c>dtrue</c> are locals in the C too. <c>serr</c> is reset by SETUP.
/// </remarks>
internal sealed class Suite07Apsides
{
    private readonly double[] _xxnasc = new double[6];
    private readonly double[] _xxndsc = new double[6];
    private readonly double[] _xxperi = new double[6];
    private readonly double[] _xxaphe = new double[6];
    private readonly double[] _xxdret = new double[20];

    public DispatchOutcome Dispatch(SwissEph swe, TestCaseScope scope, ExpIteration iteration)
    {
        var testCaseId = scope.TestCaseId;
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
                var xxnasc = _xxnasc;
                var xxndsc = _xxndsc;
                var xxperi = _xxperi;
                var xxaphe = _xxaphe;
                string serr = "";
                var rc = swe.swe_nod_aps(jd, ipl, iflag | iephe, method, xxnasc, xxndsc, xxperi, xxaphe, ref serr);
                return CheckNodAps(f, scope, rc, serr, xxnasc, xxndsc, xxperi, xxaphe);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var method = f.GetInt("method");
                var xxnasc = _xxnasc;
                var xxndsc = _xxndsc;
                var xxperi = _xxperi;
                var xxaphe = _xxaphe;
                string serr = "";
                var rc = swe.swe_nod_aps_ut(jd, ipl, iflag | iephe, method, xxnasc, xxndsc, xxperi, xxaphe, ref serr);
                return CheckNodAps(f, scope, rc, serr, xxnasc, xxndsc, xxperi, xxaphe);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                // Reference tool declares "double ... xxdret[20]"; only the
                // first 17 are checked (CHECK_DD(xxdret,17)), but the buffer
                // passed to swe_get_orbital_elements must be the full 20.
                var xxdret = _xxdret;
                string serr = "";
                var rc = swe.swe_get_orbital_elements(jd, ipl, iflag | iephe, xxdret, ref serr);
                var ctx = new CheckContext(f, scope);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckDD("xxdret", xxdret[..17]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                double dmax = 0, dmin = 0, dtrue = 0;
                string serr = "";
                var rc = swe.swe_orbit_max_min_true_distance(jd, ipl, iflag | iephe, ref dmax, ref dmin, ref dtrue, ref serr);
                var ctx = new CheckContext(f, scope);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckD("dmax", dmax);
                ctx.CheckD("dmin", dmin);
                ctx.CheckD("dtrue", dtrue);
                return DispatchOutcome.FromMismatches(ctx);
            }

            default:
                return DispatchOutcome.Error($"Suite 7 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckNodAps(ExpFields f, TestCaseScope scope, int rc, string serr, double[] xxnasc, double[] xxndsc, double[] xxperi, double[] xxaphe)
    {
        var ctx = new CheckContext(f, scope);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        ctx.CheckDD("xxnasc", xxnasc);
        ctx.CheckDD("xxndsc", xxndsc);
        ctx.CheckDD("xxperi", xxperi);
        ctx.CheckDD("xxaphe", xxaphe);
        return DispatchOutcome.FromMismatches(ctx);
    }
}
