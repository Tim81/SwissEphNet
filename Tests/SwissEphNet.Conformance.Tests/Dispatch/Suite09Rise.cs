using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>Suite 9: "risings, heliacal risings" (external/swisseph/setest/suite_09_rise.c).</summary>
internal static class Suite09Rise
{
    public static DispatchOutcome Dispatch(SwissEph swe, int testCaseId, ExpIteration iteration, Precision precision)
    {
        var f = iteration.Fields;

        // SETUP: read every iteration.
        var geolat = f.GetDouble("geolat");
        var geolon = f.GetDouble("geolon");
        var altitude = f.GetDouble("altitude");
        var atpress = f.GetDouble("atpress");
        var attemp = f.GetDouble("attemp");
        var athumid = f.GetDouble("athumid");
        var atktot = f.GetDouble("atktot");
        var obsage = f.GetDouble("obsage");
        var obsSN = f.GetDouble("obsSN");

        switch (testCaseId)
        {
            case 1:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var rsmi = f.GetInt("ifltype") | f.GetInt("method");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                double tret = 0;
                string serr = "";
                var rc = swe.swe_rise_trans(jd, ipl, star, f.GetInt("iephe"), rsmi, geopos, atpress, attemp, ref tret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckD("tret", tret);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 2:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var rsmi = f.GetInt("ifltype") | f.GetInt("method");
                var jd = f.GetDouble("jd");
                var ipl = f.GetInt("ipl");
                var star = ipl == -1 ? f.GetRawString("star") : "";
                double tret = 0;
                string serr = "";
                var rc = swe.swe_rise_trans_true_hor(jd, ipl, star, f.GetInt("iephe"), rsmi, geopos, atpress, attemp, f.GetDouble("horhgt"), ref tret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckD("tret", tret);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 3:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var datm = new[] { atpress, attemp, athumid, atktot };
                // "double dobs[5] = {obsage, obsSN,};" in the reference C, but
                // default_heliacal_parameters unconditionally writes dobs[5]
                // (SweHel.cs:1330-ish: "for (i = 2; i <= 5; i++) dobs[i] = 0;")
                // -- a one-element buffer overrun the reference tool and the
                // real C library both get away with (undefined behavior,
                // silently scribbling adjacent stack memory) and .NET does
                // not. Must be 6, not 5: the callee's write length sets the
                // requirement, not the declared/checked size.
                var dobs = new[] { obsage, obsSN, 0.0, 0.0, 0.0, 0.0 };
                var jd = f.GetDouble("jd");
                // swe_heliacal_ut's dret writes top out at index 8 (verified
                // against every dret[N] write reachable from it); only the
                // first 3 are checked.
                var xxtret = new double[10];
                string serr = "";
                var rc = swe.swe_heliacal_ut(jd, geopos, datm, dobs, f.GetRawString("object"), f.GetInt("evtype"), f.GetInt("helflag"), xxtret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret[..3]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 4:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var datm = new[] { atpress, attemp, athumid, atktot };
                var dobs = new[] { obsage, obsSN, 0.0, 0.0, 0.0, 0.0 }; // see case 3's remark: must be 6
                var jd = f.GetDouble("jd");
                // swe_heliacal_pheno_ut unconditionally writes darr[0..27]
                // (SweHel.cs, swe_heliacal_pheno_ut body: darr[27] = illum;
                // is the highest live write) -- needs at least 28, not 10.
                // Only the first 3 are checked; the callee's write length,
                // not the checked count, sets the buffer size.
                var xxtret = new double[28];
                string serr = "";
                var rc = swe.swe_heliacal_pheno_ut(jd, geopos, datm, dobs, f.GetRawString("object"), f.GetInt("evtype"), f.GetInt("helflag"), xxtret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret[..3]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            case 5:
            {
                var geopos = new[] { geolon, geolat, altitude };
                var datm = new[] { atpress, attemp, athumid, atktot };
                var dobs = new[] { obsage, obsSN, 0.0, 0.0, 0.0, 0.0 }; // see case 3's remark: must be 6
                var jd = f.GetDouble("jd");
                var xxtret = new double[10]; // swe_vis_limit_mag's dret tops out at index 7
                string serr = "";
                var rc = swe.swe_vis_limit_mag(jd, geopos, datm, dobs, f.GetRawString("object"), f.GetInt("helflag"), xxtret, ref serr);
                var ctx = new CheckContext(f, precision);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xxtret", xxtret[..3]);
                return DispatchOutcome.FromMismatches(ctx.Mismatches);
            }

            default:
                return DispatchOutcome.Error($"Suite 9 has no testcase {testCaseId}.");
        }
    }
}
