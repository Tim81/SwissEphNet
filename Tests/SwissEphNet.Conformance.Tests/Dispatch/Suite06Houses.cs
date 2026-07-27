using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 6: "Houses functions" (external/swisseph/setest/suite_06_houses.c).</summary>
internal static class Suite06Houses
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        // SETUP: read every iteration, regardless of testcase.
        var jdUt = f.GetDouble("jd") + f.GetDouble("ut") / 24.0;
        var hsys = HouseSystemCodec.DecodeHsys(f.GetInt("ihsy"));
        var geolat = f.GetDouble("geolat");
        var geolon = f.GetDouble("geolon");
        var cuspCount = hsys is 'G' or 'g' ? 37 : 13;

        switch (testCaseId)
        {
            case 1:
            {
                var cusps = new double[cuspCount];
                var ascmc = new double[10];
                var rc = swe.swe_houses(jdUt, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc);
            }

            case 2:
            {
                var iflag = f.GetInt("iflag");
                var cusps = new double[cuspCount];
                var ascmc = new double[10];
                var rc = swe.swe_houses_ex(jdUt, iflag, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc);
            }

            case 3:
            {
                var isid = f.GetInt("isid");
                var iflag = f.GetInt("iflag");
                swe.swe_set_sid_mode(isid, 0, 0);
                var cusps = new double[cuspCount];
                var ascmc = new double[10];
                var rc = swe.swe_houses_ex(jdUt, iflag, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc);
            }

            case 4:
            {
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                var eps = xx[0];
                var armc = swe.swe_degnorm(swe.swe_sidtime(jdUt) + geolon);
                var cusps = new double[cuspCount];
                var ascmc = new double[10];
                var rc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusps, ascmc);
                return CheckHousesArmc(f, precision, rc, armc, cusps, ascmc);
            }

            case 5:
            {
                var sp = swe.swe_house_name(hsys);
                var ctx5 = new CheckContext(f, precision);
                ctx5.CheckS("sp", sp);
                return DispatchOutcome.FromMismatches(ctx5.Mismatches);
            }

            case 6:
            {
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                var eps = xx[0];
                var armc = swe.swe_degnorm(swe.swe_sidtime(jdUt) * 15 + geolon);
                swe.swe_calc(jdUt, SwissEph.SE_SUN, 0, xx, ref serr);
                var hp = swe.swe_house_pos(armc, geolat, eps, hsys, xx, ref serr);
                var ctx6 = new CheckContext(f, precision);
                ctx6.CheckD("armc", armc);
                ctx6.CheckD("xx[0]", xx[0]);
                ctx6.CheckD("hp", hp);
                return DispatchOutcome.FromMismatches(ctx6.Mismatches);
            }

            case 7:
            {
                var imeth = f.GetInt("imeth");
                var geopos = new[] { geolon, geolat, 100.0 };
                string serr = "";
                double gp = 0;
                var rc = swe.swe_gauquelin_sector(jdUt, SwissEph.SE_SUN, null!, 0, imeth, geopos, 0.0, 20.0, ref gp, ref serr);
                var ctx7 = new CheckContext(f, precision);
                ctx7.CheckD("jd_ut", jdUt);
                ctx7.CheckD("gp", gp);
                ctx7.CheckI("rc", rc);
                ctx7.CheckS("serr", serr);
                return DispatchOutcome.FromMismatches(ctx7.Mismatches);
            }

            case 8:
                return DispatchOutcome.NotImplemented("swe_houses_ex2 is not implemented in SwissEphNet (port is at 2.08; added in 2.10).");

            case 9:
                return DispatchOutcome.NotImplemented("swe_houses_armc_ex2 is not implemented in SwissEphNet (port is at 2.08; added in 2.10).");

            default:
                return DispatchOutcome.Error($"Suite 6 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckHouses(ExpFields f, Precision precision, int rc, double jdUt, double[] cusps, double[] ascmc)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("jd_ut", jdUt);
        ctx.CheckDD("cusps", cusps);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }

    private static DispatchOutcome CheckHousesArmc(ExpFields f, Precision precision, int rc, double armc, double[] cusps, double[] ascmc)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("armc", armc);
        ctx.CheckDD("cusps", cusps);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }
}
