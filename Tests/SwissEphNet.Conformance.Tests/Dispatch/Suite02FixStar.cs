using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 2: "Fixed stars" (external/swisseph/setest/suite_02_fixstar.c).</summary>
internal static class Suite02FixStar
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar(ref star, jd, iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag"); // carried over from the previous iteration's local, as in upstream
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar_ut(ref star, jd, iflag, xx, ref serr);
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
                var iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jd, 2 /* SE_MERCURY */, iflag, xx, ref serr);
                var rc = swe.swe_fixstar(ref star, jd, iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 5:
            {
                // "Algol, then Betelgeuze": priming swe_fixstar("Algol") call is
                // discarded, only the second (real) star is compared.
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag");
                var xx = new double[6];
                string serr = "";
                var algol = "Algol";
                swe.swe_fixstar(ref algol, jd, iflag, xx, ref serr);
                var star = f.GetRawString("star");
                var rc = swe.swe_fixstar(ref star, jd, iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 6:
            {
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar2(ref star, jd, iflag, xx, ref serr);
                return CheckCalc(f, precision, rc, xx, serr);
            }

            case 7:
            {
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag");
                var star = f.GetRawString("star");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_fixstar2_ut(ref star, jd, iflag, xx, ref serr);
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
