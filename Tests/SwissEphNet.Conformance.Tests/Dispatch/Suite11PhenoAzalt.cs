using SwissEphNet.Conformance.Tests.Corpus;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Suite 11: "Phenomena, horizon coordinates, refraction, coordinate transformation"
/// (external/swisseph/setest/suite_11_pheno_azalt.c, added in v2.10.04 by ce6b5c08).
/// </summary>
/// <remarks>
/// <para>
/// suite_11_pheno_azalt.c:9 declares its working arrays once, as file-scope statics shared by
/// every testcase, exactly like suite 8's. They are instance fields here for the same reason
/// <see cref="Suite08Eclipses"/>'s are: one instance per corpus run reproduces whatever an
/// earlier call left in an element a later call does not write, instead of a fresh zero.
/// (swe_pheno writes only the first five of xpheno's twenty; the C checks only those five.)
/// </para>
/// <para>
/// Three testcases end in CHECK_EQUALS_D round trips -- swe_azalt then swe_azalt_rev (3, 4) and
/// swe_cotrans(_sp) with eps then -eps (7, 8). Those compare two computed values and are not in
/// t.exp, so they run through <see cref="CheckContext.CheckEqualsD"/>. swe_refrac has no round
/// trip: SE_APP_TO_TRUE is not the exact inverse of SE_TRUE_TO_APP (suite_11_pheno_azalt.c:7-9).
/// </para>
/// </remarks>
internal sealed class Suite11PhenoAzalt
{
    private readonly double[] _geopos = new double[3];
    private readonly double[] _xin = new double[3];
    private readonly double[] _xpheno = new double[20];
    private readonly double[] _xaz = new double[3];
    private readonly double[] _xpo = new double[6];
    private readonly double[] _xpn = new double[6];
    private readonly double[] _xback = new double[6];
    private readonly double[] _xrefr = new double[4];

    public DispatchOutcome Dispatch(SwissEph swe, TestCaseScope scope, ExpIteration iteration)
    {
        var testCaseId = scope.TestCaseId;
        var f = iteration.Fields;
        var ctx = new CheckContext(f, scope);

        // SETUP: *serr = '\0'; a fresh empty string per iteration.
        string serr = "";

        switch (testCaseId)
        {
            case 1: // swe_pheno
            {
                var ipl = f.GetInt("ipl");
                var iflag = f.GetInt("iephe") | f.GetInt("iflag");
                var jd = f.GetDouble("jd");
                var rc = swe.swe_pheno(jd, ipl, iflag, _xpheno, ref serr);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xpheno", _xpheno[..5]);
                ctx.CheckS("serr", serr);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 2: // swe_pheno_ut
            {
                var ipl = f.GetInt("ipl");
                var iflag = f.GetInt("iephe") | f.GetInt("iflag");
                var jd = f.GetDouble("jd");
                var rc = swe.swe_pheno_ut(jd, ipl, iflag, _xpheno, ref serr);
                ctx.CheckI("rc", rc);
                ctx.CheckDD("xpheno", _xpheno[..5]);
                ctx.CheckS("serr", serr);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 3: // swe_azalt(SE_ECL2HOR) then swe_azalt_rev(SE_HOR2ECL)
            {
                var jd = f.GetDouble("jd");
                _geopos[0] = f.GetDouble("geolon");
                _geopos[1] = f.GetDouble("geolat");
                _geopos[2] = f.GetDouble("geoalt");
                _xin[0] = f.GetDouble("lon");
                _xin[1] = f.GetDouble("lat");
                _xin[2] = 1;
                swe.swe_azalt(jd, SwissEph.SE_ECL2HOR, _geopos, f.GetDouble("atpress"), f.GetDouble("attemp"), _xin, _xaz);
                ctx.CheckDD("xaz", _xaz);
                // from azimuth and true altitude back to the ecliptic position
                swe.swe_azalt_rev(jd, SwissEph.SE_HOR2ECL, _geopos, _xaz, _xback);
                ctx.CheckEqualsD("xback[0]", _xback[0], _xin[0]);
                ctx.CheckEqualsD("xback[1]", _xback[1], _xin[1]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 4: // swe_azalt(SE_EQU2HOR) then swe_azalt_rev(SE_HOR2EQU)
            {
                var jd = f.GetDouble("jd");
                _geopos[0] = f.GetDouble("geolon");
                _geopos[1] = f.GetDouble("geolat");
                _geopos[2] = f.GetDouble("geoalt");
                _xin[0] = f.GetDouble("ra");
                _xin[1] = f.GetDouble("decl");
                _xin[2] = 1;
                swe.swe_azalt(jd, SwissEph.SE_EQU2HOR, _geopos, f.GetDouble("atpress"), f.GetDouble("attemp"), _xin, _xaz);
                ctx.CheckDD("xaz", _xaz);
                // from azimuth and true altitude back to the equatorial position
                swe.swe_azalt_rev(jd, SwissEph.SE_HOR2EQU, _geopos, _xaz, _xback);
                ctx.CheckEqualsD("xback[0]", _xback[0], _xin[0]);
                ctx.CheckEqualsD("xback[1]", _xback[1], _xin[1]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 5: // swe_refrac
            {
                var alt = swe.swe_refrac(f.GetDouble("inalt"), f.GetDouble("atpress"), f.GetDouble("attemp"), f.GetInt("calc_flag"));
                ctx.CheckD("alt", alt);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 6: // swe_refrac_extended
            {
                var alt = swe.swe_refrac_extended(
                    f.GetDouble("inalt"), f.GetDouble("geoalt"), f.GetDouble("atpress"), f.GetDouble("attemp"),
                    f.GetDouble("lapse_rate"), f.GetInt("calc_flag"), _xrefr);
                ctx.CheckD("alt", alt);
                ctx.CheckDD("xrefr", _xrefr);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 7: // swe_cotrans with eps, and back with -eps
            {
                var eps = f.GetDouble("eps");
                _xpo[0] = f.GetDouble("lon");
                _xpo[1] = f.GetDouble("lat");
                _xpo[2] = f.GetDouble("dist");
                swe.swe_cotrans(_xpo, _xpn, eps);
                ctx.CheckDD("xpn", _xpn[..3]);
                swe.swe_cotrans(_xpn, _xback, -eps);
                ctx.CheckEqualsD("xback[0]", _xback[0], _xpo[0]);
                ctx.CheckEqualsD("xback[1]", _xback[1], _xpo[1]);
                ctx.CheckEqualsD("xback[2]", _xback[2], _xpo[2]);
                return DispatchOutcome.FromMismatches(ctx);
            }

            case 8: // swe_cotrans_sp with eps, and back with -eps
            {
                var eps = f.GetDouble("eps");
                _xpo[0] = f.GetDouble("lon");
                _xpo[1] = f.GetDouble("lat");
                _xpo[2] = f.GetDouble("dist");
                _xpo[3] = f.GetDouble("lonspeed");
                _xpo[4] = f.GetDouble("latspeed");
                _xpo[5] = f.GetDouble("distspeed");
                swe.swe_cotrans_sp(_xpo, _xpn, eps);
                ctx.CheckDD("xpn", _xpn);
                swe.swe_cotrans_sp(_xpn, _xback, -eps);
                for (var i = 0; i < 6; i++)
                {
                    ctx.CheckEqualsD($"xback[{i}]", _xback[i], _xpo[i]);
                }

                return DispatchOutcome.FromMismatches(ctx);
            }

            default:
                return DispatchOutcome.Error($"Suite 11 has no testcase {testCaseId}.");
        }
    }
}
