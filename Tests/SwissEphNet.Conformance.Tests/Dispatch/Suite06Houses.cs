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
        // "cusps[13]" is present -- rather than from ihsy=='G': at least one
        // recorded iteration (a failure case, rc=-1, in the polar circle)
        // has ihsy='G' yet only 13 cusps were written, so the reference
        // tool's own CHECK_DD(cusps,37)-vs-13 branch does not purely track
        // the ihsy character the way check_swehouses_results' source alone
        // would suggest. Deriving the count from what is actually present
        // matches every iteration by construction.
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
                // swe_house_pos(armc, geolat, eps, ihsy, xpin, serr) is also
                // called with the raw int (suite_06_houses.c:58), but unlike
                // swe_house_name it is not a simple lookup: the reference C's
                // swe_house_pos performs an early toupper()+comparison against
                // the *untruncated* hsys (falling through to a default branch
                // for our garbage-encoded values, exactly as in 6.5), and then
                // -- for the actual position computation -- calls
                // swe_houses_armc(armc, geolat, eps, hsys, ...) passing that
                // *same* still-untruncated hsys, which only becomes truncated
                // to a char deep inside swe_houses_armc's own CalcH call
                // (swehouse.c:659). One C variable therefore serves two
                // different effective values at two points in the reference
                // call: raw at the early check, truncated at the inner one.
                //
                // SwissEphNet's swe_house_pos takes a single `char hsys`
                // parameter. Whatever we pass arrives already-narrowed at
                // *both* points inside the port with no distinction --
                // passing the decoded/truncated char reproduces the inner
                // CalcH-equivalent behavior but not the early fall-through;
                // passing the raw value cast to char (as in 6.5) reproduces
                // the early fall-through but does not truncate for the inner
                // computation (C#'s (char) is a 16-bit reinterpretation, not
                // C's 8-bit-truncating one, so a further internal cast inside
                // the port does not recover the low byte either way). No
                // single argument reproduces the reference behavior for every
                // iteration. This is a structural C-vs-C# representational
                // gap, not a bug in the port or in this harness: classified
                // Unreproducible rather than dispatched with a guessed
                // argument that would make some iterations pass for the
                // wrong reason (as the pre-fix harness did for ~621 of them,
                // at suite 6's loose tolerance).
                return DispatchOutcome.Unreproducible(
                    "swe_house_pos is called with a raw (untruncated) ihsy in the reference C, which affects an early " +
                    "toupper()/switch check differently from the truncated value used by the inner swe_houses_armc/CalcH " +
                    "computation it delegates to. SwissEphNet's swe_house_pos takes one `char hsys` parameter that both " +
                    "reads see identically, so no single argument reproduces both reference code paths. See suite_06_houses.c:58, " +
                    "external/swisseph/swehouse.c's swe_house_pos and swe_houses_armc.");

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
}
