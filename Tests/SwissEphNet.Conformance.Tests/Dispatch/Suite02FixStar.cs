using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Suite 2: "Fixed stars" (external/swisseph/setest/suite_02_fixstar.c).
/// </summary>
/// <remarks>
/// Unlike every other suite, suite 2 declares no SETUP block: "iflag" is a
/// plain local declared once for the whole suite ("int rc, iflag, ipl;") and
/// only reassigned in testcase 1 and testcase 6 ("iflag = GET_I(iflag);").
/// Testcases 2, 4, 5, and 7 read whatever value iflag was last set to by a
/// previous testcase in the same run -- t.exp reflects this exactly: those
/// testcases carry no "iflag" field of their own at all. This class is
/// therefore stateful (one instance per corpus run, not a static method) so
/// that carry-over is reproduced in testcase execution order.
/// </remarks>
internal sealed class Suite02FixStar
{
    // suite_02_fixstar.c:5-7: "static double xx[6],jd; static int rc, iflag, ipl; static char serr[255],
    // star[100];" -- file scope, shared by every iteration and testcase. With no SETUP block nothing
    // resets them between iterations, so xx and serr carry over too, wherever a call returns without
    // writing them. rc, ipl, jd and star are always assigned before they are read.
    private int _iflag;
    private readonly double[] _xx = new double[6];
    private string _serr = "";

    public DispatchOutcome Dispatch(SwissEph swe, TestCaseScope scope, ExpIteration iteration)
    {
        var testCaseId = scope.TestCaseId;
        var f = iteration.Fields;

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                _iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = _xx;
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = _xx;
                var rc = swe.swe_fixstar_ut(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            case 3:
            {
                var star = f.GetRawString("star");
                var mag = 0.0;
                var rc = swe.swe_fixstar_mag(ref star, ref mag, ref _serr);
                var ctx = new CheckContext(f, scope);
                ctx.CheckD("mag", mag);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", _serr);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 4:
            {
                // "Mercury, then Betelgeuze": priming swe_calc(Mercury) call is
                // discarded, only the swe_fixstar result is compared.
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = _xx;
                swe.swe_calc(jd, 2 /* SE_MERCURY */, _iflag, xx, ref _serr);
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            case 5:
            {
                // "Algol, then Betelgeuze": priming swe_fixstar("Algol") call is
                // discarded, only the second (real) star is compared.
                var jd = f.GetDouble("jd");
                var xx = _xx;
                var algol = "Algol";
                swe.swe_fixstar(ref algol, jd, _iflag, xx, ref _serr);
                var star = f.GetRawString("star");
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            case 6:
            {
                var jd = f.GetDouble("jd");
                _iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = _xx;
                var rc = swe.swe_fixstar2(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            case 7:
            {
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = _xx;
                var rc = swe.swe_fixstar2_ut(ref star, jd, _iflag, xx, ref _serr);
                return CheckCalc(f, scope, rc, xx, _serr);
            }

            default:
                return DispatchOutcome.Error($"Suite 2 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckCalc(ExpFields f, TestCaseScope scope, int rc, double[] xx, string serr)
    {
        var ctx = new CheckContext(f, scope);
        ctx.CheckDD("xx", xx);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }
}
