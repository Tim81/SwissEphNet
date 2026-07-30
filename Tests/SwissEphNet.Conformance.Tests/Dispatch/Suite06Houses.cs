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

        // The reference tool's local `cusps` buffer is always sized 37
        // (external/swisseph/setest/suite_06_houses.c: "double cusps[37],...")
        // regardless of house system -- allocate the same here to avoid an
        // under-sized buffer inside SwissEphNet's own house calculations.
        //
        // How many of those 37 slots t.exp actually recorded (and so how many
        // this run compares) is derived from the data itself -- whether
        // "cusps[13]" is present -- rather than assumed from ihsy. Verified
        // against the full corpus: "cusps[13]" never appears in t.exp (the
        // highest recorded index across every iteration, every house system
        // including Gauquelin 'G', is cusps[12], 13 values total), so this
        // always resolves to 13 today. The check is kept data-driven rather
        // than hardcoded to 13 so that a future corpus revision recording
        // more cusps for some house system is picked up automatically instead
        // of silently under-comparing.
        var cuspCount = f.Contains("cusps[13]") ? 37 : 13;

        switch (testCaseId)
        {
            case 1:
            {
                var cusps = new double[37];
                var ascmc = new double[10];
                var rc = swe.swe_houses(jdUt, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc, cuspCount);
            }

            case 2:
            {
                var iflag = f.GetInt("iflag");
                var cusps = new double[37];
                var ascmc = new double[10];
                var rc = swe.swe_houses_ex(jdUt, iflag, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc, cuspCount);
            }

            case 3:
            {
                var isid = f.GetInt("isid");
                var iflag = f.GetInt("iflag");
                swe.swe_set_sid_mode(isid, 0, 0);
                var cusps = new double[37];
                var ascmc = new double[10];
                var rc = swe.swe_houses_ex(jdUt, iflag, geolat, geolon, hsys, cusps, ascmc);
                return CheckHouses(f, precision, rc, jdUt, cusps, ascmc, cuspCount);
            }

            case 4:
            {
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                var eps = xx[0];
                var armc = swe.swe_degnorm(swe.swe_sidtime(jdUt) + geolon);
                var cusps = new double[37];
                var ascmc = new double[10];
                var rc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusps, ascmc);
                return CheckHousesArmc(f, precision, rc, armc, cusps, ascmc, cuspCount);
            }

            case 5:
            {
                // swe_house_name(ihsy) is called with the *raw*, untruncated
                // int in the reference C (suite_06_houses.c:47:
                // "sp = (char*) swe_house_name(ihsy);"). Its internal
                // toupper()+switch never routes through the CalcH truncating
                // cast that 6.1-6.4 depend on, and glibc's toupper is a no-op
                // outside [-128,256), so every garbage-encoded ihsy value in
                // this corpus (see HouseSystemCodec's remarks) falls through
                // every case to the same default -- confirmed against the
                // corpus: all 17 recorded 6.5 iterations expect "Placidus".
                // A C# char holds the raw value losslessly (it's 16 bits, the
                // value never exceeds ushort range), so passing it un-decoded
                // reproduces the same fall-to-default here.
                var rawHsys = (char)f.GetInt("ihsy");
                var sp = swe.swe_house_name(rawHsys);
                var ctx5 = new CheckContext(f, precision);
                ctx5.CheckS("sp", sp);
                return DispatchOutcome.FromMismatches(ctx5.Mismatches);
            }

            case 6:
            {
                // swe_house_pos(armc, geolat, eps, ihsy, xpin, serr) is called with the raw,
                // untruncated ihsy in the reference C (suite_06_houses.c:58). Previously
                // classified Unreproducible because SwissEphNet's swe_house_pos took only a
                // single `char hsys` parameter, so a value outside char range could not be
                // constructed to reproduce the reference C's split behavior: an early
                // toupper()/switch in swe_house_pos itself compares the *untruncated* hsys
                // (falling through to the simplified-interpolation default for garbage-encoded
                // values, exactly as in 6.5's swe_house_name), while the inner
                // swe_houses_armc(armc, geolat, eps, hsys, ...) call it delegates to only
                // truncates to 8 bits deep inside its own CalcH (swehouse.c:659/2011/2019).
                //
                // The port's `int hsys` overload of swe_house_pos (SwissEphNet/SwissEph.swephexp.h.cs,
                // matching swephexp.h:832) now reproduces this exactly: it performs the same
                // early ToUpperAsciiHsys(hsys) on the untruncated int
                // (SwissEphNet/CPort/SweHouse.cs:2011) and then calls the int overload of
                // swe_houses_armc with that same untruncated value, which truncates internally
                // via CalcH -- the same one-variable-two-effective-values behavior as the C.
                // Passing the raw (undecoded) ihsy here, not HouseSystemCodec's truncated
                // `hsys`, is what makes this reproducible.
                var xx = new double[6];
                string serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                var eps = xx[0];
                // suite_06_houses.c:56 multiplies swe_sidtime by 15 (hours -> degrees) here,
                // unlike testcase 4's armc (suite_06_houses.c:42, no *15) -- reproduced as-is,
                // not normalized against testcase 4, since t.exp recorded this asymmetry.
                var armc = swe.swe_degnorm(swe.swe_sidtime(jdUt) * 15 + geolon);
                serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_SUN, 0, xx, ref serr);
                var rawIhsy = f.GetInt("ihsy");
                var hp = swe.swe_house_pos(armc, geolat, eps, rawIhsy, xx, ref serr);
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
            {
                var iflag = f.GetInt("iflag");
                var cusps = new double[37];
                var ascmc = new double[10];
                var cuspSpeed = new double[37];
                var ascmcSpeed = new double[10];
                var serr = "";
                var rc = swe.swe_houses_ex2(jdUt, iflag, geolat, geolon, hsys, cusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
                return CheckHousesEx2(f, precision, rc, jdUt, cusps, ascmc, cuspSpeed, ascmcSpeed, cuspCount);
            }

            case 9:
            {
                var xx = new double[6];
                var serr = "";
                swe.swe_calc(jdUt, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                var eps = xx[0];
                var armc = swe.swe_degnorm(swe.swe_sidtime(jdUt) + geolon);
                var cusps = new double[37];
                var ascmc = new double[10];
                var cuspSpeed = new double[37];
                var ascmcSpeed = new double[10];
                serr = "";
                var rc = swe.swe_houses_armc_ex2(armc, geolat, eps, hsys, cusps, ascmc, cuspSpeed, ascmcSpeed, ref serr);
                return CheckHousesArmcEx2(f, precision, rc, armc, cusps, ascmc, cuspSpeed, ascmcSpeed, cuspCount);
            }

            default:
                return DispatchOutcome.Error($"Suite 6 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckHouses(ExpFields f, Precision precision, int rc, double jdUt, double[] cusps, double[] ascmc, int cuspCount)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("jd_ut", jdUt);
        ctx.CheckDD("cusps", cusps[..cuspCount]);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }

    private static DispatchOutcome CheckHousesArmc(ExpFields f, Precision precision, int rc, double armc, double[] cusps, double[] ascmc, int cuspCount)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("armc", armc);
        ctx.CheckDD("cusps", cusps[..cuspCount]);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }

    // external/swisseph/setest/globals_suite.c: check_swehouses_ex2_results -- same fields as
    // CheckHouses, plus cusp_speed/ascmc_speed. serr is not checked (globals_suite.c omits
    // CHECK_S(serr) here, unlike testcase 7's swe_gauquelin_sector).
    private static DispatchOutcome CheckHousesEx2(ExpFields f, Precision precision, int rc, double jdUt, double[] cusps, double[] ascmc, double[] cuspSpeed, double[] ascmcSpeed, int cuspCount)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("jd_ut", jdUt);
        ctx.CheckDD("cusps", cusps[..cuspCount]);
        ctx.CheckDD("cusp_speed", cuspSpeed[..cuspCount]);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckDD("ascmc_speed", ascmcSpeed[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }

    // external/swisseph/setest/globals_suite.c: check_swehouses_armc_ex2_results -- same fields
    // as CheckHousesArmc, plus cusp_speed/ascmc_speed.
    private static DispatchOutcome CheckHousesArmcEx2(ExpFields f, Precision precision, int rc, double armc, double[] cusps, double[] ascmc, double[] cuspSpeed, double[] ascmcSpeed, int cuspCount)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("armc", armc);
        ctx.CheckDD("cusps", cusps[..cuspCount]);
        ctx.CheckDD("cusp_speed", cuspSpeed[..cuspCount]);
        ctx.CheckDD("ascmc", ascmc[..6]);
        ctx.CheckDD("ascmc_speed", ascmcSpeed[..6]);
        ctx.CheckI("rc", rc);
        return DispatchOutcome.FromMismatches(ctx.Mismatches);
    }
}
