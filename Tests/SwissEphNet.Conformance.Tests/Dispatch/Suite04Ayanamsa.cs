using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 4: "Some computations in sidereal mode" (external/swisseph/setest/suite_04_ayanamsa.c).</summary>
internal static class Suite04Ayanamsa
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;
        var ctx = new CheckContext(f, precision);

        SetSidMode(swe, f);

        switch (testCaseId)
        {
            case 1:
            {
                var jd = f.GetDouble("jd");
                var iflag = f.GetInt("iflag");
                var ipl = f.GetInt("ipl");
                var xx = new double[6];
                string serr = "";
                var rc = swe.swe_calc(jd, ipl, iflag, xx, ref serr);
                ctx.CheckDD("xx", xx);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 2:
            {
                var jd = f.GetDouble("jd");
                var iephe = f.GetInt("iephe");

                string serrExUt = "";
                var rcAyaExUt = swe.swe_get_ayanamsa_ex_ut(jd, iephe, out var dayaExUt, ref serrExUt);
                ctx.CheckI("rc_aya_ex_ut", rcAyaExUt);
                ctx.CheckD("daya_ex_ut", dayaExUt);
                ctx.CheckS("serr_ex_ut", serrExUt);

                string serrEx = "";
                var rcAyaEx = swe.swe_get_ayanamsa_ex(jd, iephe, out var dayaEx, ref serrEx);
                ctx.CheckI("rc_aya_ex", rcAyaEx);
                ctx.CheckD("daya_ex", dayaEx);
                ctx.CheckS("serr_ex", serrEx);

                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 3:
            {
                var jd = f.GetDouble("jd");
                var dayaUt = swe.swe_get_ayanamsa_ut(jd);
                ctx.CheckD("daya_ut", dayaUt);
                var daya = swe.swe_get_ayanamsa(jd);
                ctx.CheckD("daya", daya);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                var jd = f.GetDouble("jd");
                var daya = swe.swe_get_ayanamsa_ut(jd);
                ctx.CheckD("daya", daya);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 4 has no testcase {testCaseId}.");
        }
    }

    private static void SetSidMode(SwissEph swe, ExpFields f)
    {
        var t0 = f.GetDouble("t0");
        var ayanT0 = f.GetDouble("ayan_t0");
        var sidMode = f.GetInt("sid_mode");
        swe.swe_set_sid_mode(sidMode, t0, ayanT0);
    }
}
