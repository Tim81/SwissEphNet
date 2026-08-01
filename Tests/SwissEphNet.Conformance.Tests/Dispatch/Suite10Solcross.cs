using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Suite 10: "Various swe_*cross calls" (external/swisseph/setest/suite_10_solcross.c).
/// Ported at sweph.c:8308-8615. iephe doubles as the iflag argument in every
/// testcase here (the reference C's SETUP does "iflag = iephe;"), and the
/// suite carries no separate "iflag" field in t.exp.
/// </summary>
internal static class Suite10Solcross
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
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
                var xx = new double[6];
                var rc = swe.swe_calc(jx, SwissEph.SE_SUN, iephe, xx, ref serr);
                return CheckCross(f, precision, rc, jx, serr);
            }

            case 2: // swe_solcross_ut
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_solcross_ut(xcross, jd, iephe, ref serr);
                var xx = new double[6];
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_SUN, iephe, xx, ref serr);
                return CheckCross(f, precision, rc, jx, serr);
            }

            case 3: // swe_mooncross
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_mooncross(xcross, jd, iephe, ref serr);
                var xx = new double[6];
                var rc = swe.swe_calc(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCross(f, precision, rc, jx, serr);
            }

            case 4: // swe_mooncross_ut
            {
                var xcross = f.GetDouble("xcross");
                string serr = "";
                var jx = swe.swe_mooncross_ut(xcross, jd, iephe, ref serr);
                var xx = new double[6];
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCross(f, precision, rc, jx, serr);
            }

            case 5: // swe_mooncross_node
            {
                double xlon = 0, xlat = 0;
                string serr = "";
                var jx = swe.swe_mooncross_node(jd, iephe, ref xlon, ref xlat, ref serr);
                var xx = new double[6];
                var rc = swe.swe_calc(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCrossNode(f, precision, rc, jx, xlon, xlat, serr);
            }

            case 6: // swe_mooncross_node_ut
            {
                double xlon = 0, xlat = 0;
                string serr = "";
                var jx = swe.swe_mooncross_node_ut(jd, iephe, ref xlon, ref xlat, ref serr);
                var xx = new double[6];
                var rc = swe.swe_calc_ut(jx, SwissEph.SE_MOON, iephe, xx, ref serr);
                return CheckCrossNode(f, precision, rc, jx, xlon, xlat, serr);
            }

            case 7: // swe_helio_cross
            {
                var xcross = f.GetDouble("xcross");
                var ipl = f.GetInt("ipl");
                var dir = f.GetInt("dir");
                double jx = 0;
                string serr = "";
                var rc = swe.swe_helio_cross(ipl, xcross, jd, iephe, dir, ref jx, ref serr);
                return CheckHelioCross(f, precision, jx, rc, serr);
            }

            case 8: // swe_helio_cross_ut
            {
                var xcross = f.GetDouble("xcross");
                var ipl = f.GetInt("ipl");
                var dir = f.GetInt("dir");
                double jx = 0;
                string serr = "";
                var rc = swe.swe_helio_cross_ut(ipl, xcross, jd, iephe, dir, ref jx, ref serr);
                return CheckHelioCross(f, precision, jx, rc, serr);
            }

            default:
                return DispatchOutcome.Error($"Suite 10 has no testcase {testCaseId}.");
        }
    }

    private static DispatchOutcome CheckCross(ExpFields f, Precision precision, int rc, double jx, string serr)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckI("rc", rc);
        ctx.CheckD("jx", jx);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }

    private static DispatchOutcome CheckCrossNode(ExpFields f, Precision precision, int rc, double jx, double xlon, double xlat, string serr)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckI("rc", rc);
        ctx.CheckD("jx", jx);
        ctx.CheckD("xlon", xlon);
        ctx.CheckD("xlat", xlat);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }

    private static DispatchOutcome CheckHelioCross(ExpFields f, Precision precision, double jx, int rc, string serr)
    {
        var ctx = new CheckContext(f, precision);
        ctx.CheckD("jx", jx);
        ctx.CheckI("rc", rc);
        ctx.CheckS("serr", serr);
        return DispatchOutcome.FromMismatches(ctx);
    }
}
