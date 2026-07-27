using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 5: "Date and time functions" (external/swisseph/setest/suite_05_date_time.c).</summary>
internal static class Suite05DateTime
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;
        var ctx = new CheckContext(f, precision);

        switch (testCaseId)
        {
            case 1:
            {
                var jd = swe.swe_julday(f.GetInt("year"), f.GetInt("month"), f.GetInt("day"), f.GetDouble("hour"), f.GetInt("gregflag"));
                ctx.CheckD("jd", jd);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 2:
            {
                int year = 0, month = 0, day = 0;
                double ut = 0;
                swe.swe_revjul(f.GetDouble("jd"), f.GetInt("gregflag"), ref year, ref month, ref day, ref ut);
                ctx.CheckI("year", year);
                ctx.CheckI("month", month);
                ctx.CheckI("day", day);
                ctx.CheckD("ut", ut);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 3:
            {
                var iephe = f.GetInt("iephe");
                var jd = f.GetDouble("jd");
                double deltat;
                if (iephe > 0)
                {
                    string serr = "";
                    deltat = swe.swe_deltat_ex(jd, iephe, ref serr);
                    ctx.CheckS("serr", serr);
                }
                else
                {
                    deltat = swe.swe_deltat(jd);
                }

                ctx.CheckD("deltat", deltat);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                string serr = "";
                var rc = swe.swe_time_equ(f.GetDouble("jd"), out var e, ref serr);
                ctx.CheckD("E", e);
                ctx.CheckI("rc", rc);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 5:
            {
                var tjdLmt = f.GetDouble("tjd_lmt");
                var geolon = f.GetDouble("geolon");
                string serr = "";
                var rc = swe.swe_lmt_to_lat(tjdLmt, geolon, out var tjdLat, ref serr);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckD("tjd_lat", tjdLat);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 6:
            {
                var tjdLat = f.GetDouble("tjd_lat");
                var geolon = f.GetDouble("geolon");
                string serr = "";
                var rc = swe.swe_lat_to_lmt(tjdLat, geolon, out var tjdLmt, ref serr);
                ctx.CheckI("rc", rc);
                ctx.CheckS("serr", serr);
                ctx.CheckD("tjd_lmt", tjdLmt);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 5 has no testcase {testCaseId}.");
        }
    }
}
