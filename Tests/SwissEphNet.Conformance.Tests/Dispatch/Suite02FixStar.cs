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
    private int _iflag;

    public DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                _iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar_ut(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 3:
            {
                var star = f.GetRawString("star");
                var mag = 0.0;
                string serr = "";
                var rc = swe.swe_fixstar_mag(ref star, ref mag, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckD("mag", mag);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                // "Mercury, then Betelgeuze": priming swe_calc(Mercury) call is
                // discarded, only the swe_fixstar result is compared.
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jd, 2 /* SE_MERCURY */, _iflag, xx, ref serr);
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 5:
            {
                // "Algol, then Betelgeuze": priming swe_fixstar("Algol") call is
                // discarded, only the second (real) star is compared.
                var jd = f.GetDouble("jd");
                var xx = new double[6];
                string serr = "";
                var algol = "Algol";
                swe.swe_fixstar(ref algol, jd, _iflag, xx, ref serr);
                var star = f.GetRawString("star");
                var rc = swe.swe_fixstar(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 6:
            {
                var jd = f.GetDouble("jd");
                _iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar2(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 7:
            {
                var jd = f.GetDouble("jd");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar2_ut(ref star, jd, _iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            default:
                return DispatchOutcome.Error($"Suite 2 has no testcase {testCaseId}.");
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
