using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Suite 10: "Various swe_*cross calls" (external/swisseph/setest/suite_10_solcross.c).
/// Ported at sweph.c:8308-8615. iephe doubles as the iflag argument in every
/// testcase here (the reference C's SETUP does "iflag = iephe;"), and the
/// suite carries no separate "iflag" field in t.exp.
/// </summary>
internal sealed class Suite10Solcross
{
    // suite_10_solcross.c:6 "static double xx[6],jd;" -- file scope, shared by every iteration. When the
    // swe_calc after a failed search returns without writing xx, the self-check below sees the
    // previous iteration's. jd, iephe and serr are rewritten by SETUP each iteration.
    private readonly double[] _xx = new double[6];

    public DispatchOutcome Dispatch(SwissEph swe, TestCaseScope scope, ExpIteration iteration)
    {
        var testCaseId = scope.TestCaseId;
        var f = iteration.Fields;
        var iephe = f.GetInt("iephe");
        var jd = f.GetDouble("jd");

        // Every testcase in this suite loops swe_calc(_ut) on dates that walk arbitrarily
        // far from the starting jd while searching for a crossing, so the era-file gap
        // documented in EphemerisFileResolver.NeedsEraFileWeDoNotShip (years outside
        // 1200-2399 need an era file this repo's 8-file core set does not ship) applies to
        // the whole suite, not just the corpus's own starting jd.
        if (iephe == SwissEph.SEFLG_SWIEPH && EphemerisFileResolver.NeedsEraFileWeDoNotShip(swe, jd))
        {
            return DispatchOutcome.DataMissing(
                "iephe is SEFLG_SWIEPH for a date outside the era this repo's shipped core ephemeris files " +
                "(sepl/semo/seas_12.se1 and _18.se1, years 1200-2399) cover; Swiss Ephemeris falls back to " +
                "Moshier internally and reports \"using Moshier eph.\" in serr, which the reference run " +
                "(built against the full file set) does not.");
        }

        switch (testCaseId)
        {
            case 1: // swe_solcross
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_solcross(xcross, jd, iephe, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc(jx, SwissEph.SE_SUN, iephe, xx, ref serr);
                return CheckCross(f, scope, rc, jx, xcross, xx, serr);
            }

            case 2: // swe_solcross_ut
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_solcross_ut(xcross, jd, iephe, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_SUN, iephe, xx, ref serr);
                return CheckCross(f, scope, rc, jx, xcross, xx, serr);
            }

            case 3: // swe_mooncross
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_mooncross(xcross, jd, iephe, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCross(f, scope, rc, jx, xcross, xx, serr);
            }

            case 4: // swe_mooncross_ut
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_mooncross_ut(xcross, jd, iephe, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCross(f, scope, rc, jx, xcross, xx, serr);
            }

            case 5: // swe_mooncross_node
            {
                double xlon = 0, xlat = 0;
                string serr = "";
                var jx = swe.swe_mooncross_node(jd, iephe, ref xlon, ref xlat, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCrossNode(f, scope, rc, jx, xlon, xlat, xx, serr);
            }

            case 6: // swe_mooncross_node_ut
            {
                double xlon = 0, xlat = 0;
                string serr = "";
                var jx = swe.swe_mooncross_node_ut(jd, iephe, ref xlon, ref xlat, ref serr);
                var xx = _xx;
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCrossNode(f, scope, rc, jx, xlon, xlat, xx, serr);
            }

            case 7: // swe_helio_cross
            {
                var xcross = f.GetDouble("xcross");
                var ipl = f.GetInt("ipl");
                var dir = f.GetInt("dir");
                double jx = 0;
                string serr = "";
                var rc = swe.swe_helio_cross(ipl, xcross, jd, iephe, dir, ref jx, ref serr);
                return CheckHelioCross(f, scope, jx, rc, serr);
            }

            case 8: // swe_helio_cross_ut
            {
                var xcross = f.GetDouble("xcross");
                var ipl = f.GetInt("ipl");
                var dir = f.GetInt("dir");
                double jx = 0;
                string serr = "";
                var rc = swe.swe_helio_cross_ut(ipl, xcross, jd, iephe, dir, ref jx, ref serr);
                return CheckHelioCross(f, scope, jx, rc, serr);
            }

            default:
                return DispatchOutcome.Error($"Suite 10 has no testcase {testCaseId}.");
        }
    }

    // suite_10_solcross.c:29-32 etc.: CHECK_EQUALS_D(xcross, xx[0]) -- the crossing function's result,
    // re-evaluated with swe_calc, must land on the target longitude. Not file-backed.
    private static DispatchOutcome CheckCross(ExpFields f, TestCaseScope scope, int rc, double jx, double xcross, double[] xx, string serr)
    {
        var ctx = new CheckContext(f, scope);
        ctx.CheckI("rc", rc);
        ctx.CheckD("jx", jx);
        ctx.CheckEqualsD("xcross", xcross, xx[0]);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }

    // suite_10_solcross.c:77: CHECK_EQUALS_D(xx[1], 0) -- the Moon's latitude at its node crossing.
    private static DispatchOutcome CheckCrossNode(ExpFields f, TestCaseScope scope, int rc, double jx, double xlon, double xlat, double[] xx, string serr)
    {
        var ctx = new CheckContext(f, scope);
        ctx.CheckI("rc", rc);
        ctx.CheckD("jx", jx);
        ctx.CheckD("xlon", xlon);
        ctx.CheckD("xlat", xlat);
        ctx.CheckEqualsD("xx[1]", xx[1], 0);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }

    private static DispatchOutcome CheckHelioCross(ExpFields f, TestCaseScope scope, double jx, int rc, string serr)
    {
        var ctx = new CheckContext(f, scope);
        ctx.CheckD("jx", jx);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }
}
