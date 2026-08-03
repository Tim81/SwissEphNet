using System;
using System.Globalization;
using System.Reflection;
using Xunit;

namespace SwissEphNet.Tests
{

    public partial class SwissEphTest : IDisposable
    {
        CultureInfo _OldCulture;

        public SwissEphTest()
        {
            _OldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = _OldCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = _OldCulture;
        }

        [Fact]
        public void TestConstructor() {
            // No assertion at all: this passed whether the constructor actually ran any of
            // SwissEph.cs's setup (the nine internal component properties, event wiring, etc.)
            // or threw away all its work. FileProvider's accessor throws ObjectDisposedException
            // on a disposed instance (SwissEph.cs), so reading it back as its documented default
            // of null both confirms the constructor set that state up and that this instance is
            // not somehow born already disposed.
            using (var target = new SwissEph()) {
                Assert.Null(target.FileProvider);
            }
        }

        [Fact]
        public void TestVersion() {
            using (var target = new SwissEph()) {
                Assert.Equal("2.10.03", target.swe_version());
                Assert.Equal("2.10.03-net-0000", target.swe_dotnet_version());
            }
        }

        [Fact]
        public void TestOnLoadFile() {
            // sweph.c:7044-7065 splits the not-found message in two:
            // "(asteroid)" when ipl > SE_AST_OFFSET, "(planetary moon)"
            // otherwise. SE_AST_OFFSET + 100 takes the asteroid branch. The
            // previous "100: not found" (no suffix) predates that split; the
            // port now matches, and fixing it resolved the last differing
            // row (NAME|10005) in the file-backed oracle grid.

            // No FileProvider configured: SwissEph.OpenBinary falls back to the real
            // filesystem, which does not have a "[ephe]/..." directory, so this still ends
            // up at the same not-found path as before.
            using (var target = new SwissEph()) {
                Assert.Equal("100: not found (asteroid)", target.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 100));
            }

            // FileProvider configured, but reports every file not found
            using (var target = new SwissEph()) {
                target.FileProvider = new DelegateFileProvider(path => null);
                Assert.Equal("100: not found (asteroid)", target.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 100));
            }

            // FileProvider configured
            using (var target = new SwissEph()) {
                target.FileProvider = new DelegateFileProvider(path => {
                    if (ResourceFileHelpers.GetPortableFileName(path) == "seasnam.txt") {
                        return new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(@"
000096  Aegle
000097  Klotho
000098  Ianthe
000099  Dike
000100  Hekate
000101  Helena
000102  Miriam
000103  Hera
"));
                    }
                    return null;
                });
                Assert.Equal("Hekate", target.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 100));
            }

        }

        [Fact]
        public void TestDefaultEncodingAppliesToProviderSuppliedStreams() {
            // The multicast OnLoadFile event used to expose a per-file LoadFileEventArgs.Encoding
            // escape hatch a handler could overwrite before returning. The single-valued
            // IEphemerisFileProvider that replaces it (docs/known-issues.md's OnLoadFile entry)
            // only ever returns a Stream (see THE RESOLVER's fixed shape), so per-file encoding
            // control is gone; the one remaining lever is the static SwissEph.DefaultEncoding,
            // which applies to every file SwissEph.OpenBinary opens for the lifetime of the
            // process (or until changed again) -- unlike CFile's own Encoding constructor
            // parameter (see CFileTest's TestExplicitEncodingOverridesUtf8Default), which no
            // IEphemerisFileProvider consumer can reach either: OpenBinary always passes
            // DefaultEncoding to the CFile it constructs.
            var savedEncoding = SwissEph.DefaultEncoding;
            try {
                SwissEph.DefaultEncoding = System.Text.Encoding.GetEncoding("ISO-8859-1");
                using (var target = new SwissEph()) {
                    target.FileProvider = new DelegateFileProvider(path => {
                        if (ResourceFileHelpers.GetPortableFileName(path) == "seasnam.txt") {
                            // 0xE9 is Windows-1252 (and Latin-1) for é; decoded as UTF-8 on its
                            // own it is an invalid lead byte, so if DefaultEncoding were ignored
                            // (falling back to UTF-8), this would read back as "Kor�", not "Koré".
                            var bytes = new System.Collections.Generic.List<byte>();
                            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("000200  Kor"));
                            bytes.Add(0xE9);
                            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("\n"));
                            return new System.IO.MemoryStream(bytes.ToArray());
                        }
                        return null;
                    });
                    Assert.Equal("Koré", target.swe_get_planet_name(SwissEph.SE_AST_OFFSET + 200));
                }
            } finally {
                SwissEph.DefaultEncoding = savedEncoding;
            }
        }

        // The 36 API surfaces below (swe_heliacal_ut through swe_degnorm, further down this
        // file) used to have a [Fact(Skip = "")] stub here: a method whose body was only a
        // commented-out signature. xUnit v2 gates skipping on a non-empty Skip reason, so an
        // empty string does not skip at all -- these 36 counted as passing, alongside every
        // other unit test, without ever calling the method they were named for. Deleted rather
        // than implemented or given a real skip reason, because each is already exercised with
        // real assertions elsewhere:
        //  swe_heliacal_ut, swe_heliacal_pheno_ut, swe_vis_limit_mag, swe_rise_trans,
        //   swe_rise_trans_true_hor: Tests/SwissEphNet.Conformance.Tests/Dispatch/Suite09Rise.cs
        //   (testcases 1-5), against the setest oracle corpus.
        //  swe_houses, swe_houses_ex, swe_houses_armc, swe_house_pos, swe_house_name,
        //   swe_gauquelin_sector: Suite06Houses.cs (testcases 1-7), likewise oracle-checked.
        //  swe_sol_eclipse_where, swe_lun_occult_where, swe_sol_eclipse_how,
        //   swe_sol_eclipse_when_loc, swe_lun_occult_when_loc, swe_sol_eclipse_when_glob,
        //   swe_lun_eclipse_how, swe_lun_eclipse_when: Suite08Eclipses.cs, oracle-checked.
        //   NOT swe_lun_eclipse_when_loc -- see Test_swe_lun_eclipse_when_loc below for why
        //   that one is implemented instead, despite Suite08Eclipses.cs having a testcase
        //   with that name.
        //  swe_nod_aps, swe_nod_aps_ut: Suite07Apsides.cs, oracle-checked.
        //  swe_time_equ, swe_lmt_to_lat, swe_lat_to_lmt: Suite05DateTime.cs, oracle-checked.
        //  swe_set_sid_mode, swe_get_ayanamsa, swe_get_ayanamsa_ut, swe_get_ayanamsa_name:
        //   Suite03Misc.cs / Suite04Ayanamsa.cs, oracle-checked.
        //  swe_sidtime, swe_degnorm: Suite06Houses.cs's own armc computation (testcases 4/6/9),
        //   which the oracle check on the resulting armc/cusps would catch a defect in.
        //  swe_set_ephe_path, swe_set_jpl_file: issued by every conformance suite run via
        //   Dispatch/EphemerisFileResolver.cs before dispatching, so a defect here would derail
        //   every file-dependent row in the corpus; swe_set_jpl_file additionally now has its
        //   own direct regression coverage in Issue29Test.cs.
        //  swe_close: DisposeTest.cs's Dispose_ReleasesFileHandle_RatherThanReopeningOnNextCall
        //   exercises the file-release behavior Dispose() reaches it through.
        //  swe_pheno, swe_pheno_ut: PlaDiamCoverageTest.cs, which pins attr[3] for six bodies.
        //  swe_set_tid_acc: TransliterationFidelityTest.cs's astro-models tests, as setup whose
        //   effect the tests' own assertions depend on.

        [Fact]
        public void Test_swe_lun_occult_when_glob() {
            //public Int32 swe_lun_occult_when_glob(double tjd_start, Int32 ipl, string starname, Int32 ifl, Int32 ifltype, double[] tret, bool backward, ref string serr);
            using (var target = new SwissEph()) {
                Double[] tret = new double[12];
                String serr = null;
                var r = target.swe_lun_occult_when_glob(0.0, 0, null, 0, 0, tret, false, ref serr);
                Assert.Equal(-1, r);
            }
        }

        // (swe_sol_eclipse_when_loc, swe_lun_occult_when_loc, swe_sol_eclipse_when_glob,
        // swe_lun_eclipse_how, swe_lun_eclipse_when, swe_pheno, swe_pheno_ut,
        // swe_rise_trans_true_hor, swe_rise_trans, swe_nod_aps, swe_nod_aps_ut, swe_time_equ,
        // swe_lmt_to_lat, swe_lat_to_lmt, swe_degnorm, swe_set_tid_acc: see the comment block
        // above TestConstructor's neighbours for where each is really covered.)

        // Despite its name, Suite08Eclipses.cs testcase 9 -- t.exp's own recorded values, not a
        // dispatch mistake this port introduced -- calls swe_sol_eclipse_when_loc a second time,
        // not swe_lun_eclipse_when_loc (see that testcase's own comment: upstream's
        // suite_08_eclipses.c TESTCASE(9), despite being titled "swe_lun_eclipse_when_loc()",
        // has the same bug and t.exp was generated from it, so the substitution is reproduced
        // verbatim rather than "fixed"). swe_lun_eclipse_when_loc itself is therefore never
        // actually invoked anywhere in the conformance corpus, unlike its eight eclipse/occult
        // siblings above -- implemented here instead of deleted.
        //
        // swehel.c/swecl.c: dgeo[2] outside [SEI_ECL_GEOALT_MIN, SEI_ECL_GEOALT_MAX] must return
        // ERR with this exact message, the same contract swe_heliacal_angle below has.
        [Fact]
        public void Test_swe_lun_eclipse_when_loc_InvalidGeoAlt_ReturnsErrWithMessage() {
            using (var target = new SwissEph()) {
                double tjd = target.swe_julday(2000, 1, 21, 0.0, SwissEph.SE_GREG_CAL);
                double[] geopos = { 5.333889, 47.853333, 99999 };
                double[] tret = new double[10];
                double[] attr = new double[20];
                string serr = null;

                int rc = target.swe_lun_eclipse_when_loc(tjd, SwissEph.SEFLG_MOSEPH, geopos, tret, attr, false, ref serr);

                Assert.Equal(SwissEph.ERR, rc);
                Assert.Equal("location for eclipses must be between -500 and 25000 m above sea", serr);
            }
        }

        // swe_lun_eclipse_when_loc finds the same eclipse swe_lun_eclipse_when finds globally
        // (already oracle-checked, see the comment block above) and then reports its local
        // circumstances; the moment of greatest eclipse (tret[0]) is a geometric fact about the
        // Earth-Moon-Sun alignment, not about the observer, so it must agree exactly with plain
        // swe_lun_eclipse_when for the same start date -- a relational check against an
        // already-trusted sibling function, not a magic number pinned from a single run.
        [Fact]
        public void Test_swe_lun_eclipse_when_loc() {
            using (var target = new SwissEph()) {
                double tjd = target.swe_julday(2000, 1, 21, 0.0, SwissEph.SE_GREG_CAL);

                double[] tretGlobal = new double[10];
                string serrGlobal = null;
                int rcGlobal = target.swe_lun_eclipse_when(tjd, SwissEph.SEFLG_MOSEPH, 0, tretGlobal, false, ref serrGlobal);
                Assert.True(rcGlobal >= 0, serrGlobal);

                double[] geopos = { 5.333889, 47.853333, 468 };
                double[] tretLoc = new double[10];
                double[] attrLoc = new double[20];
                string serrLoc = null;
                int rcLoc = target.swe_lun_eclipse_when_loc(tjd, SwissEph.SEFLG_MOSEPH, geopos, tretLoc, attrLoc, false, ref serrLoc);

                Assert.True(rcLoc >= 0, serrLoc);
                Assert.Equal(tretGlobal[0], tretLoc[0], 9);
                // attr[0] (fraction of lunar diameter covered) is only meaningful once an
                // eclipse was actually located; confirms tretLoc[0] is not a leftover zero.
                Assert.True(attrLoc[0] > 0, $"attr[0] ({attrLoc[0]}) should be positive for a located eclipse");
            }
        }

        // swehel.c: dgeo[2] outside [SEI_ECL_GEOALT_MIN, SEI_ECL_GEOALT_MAX] must return ERR
        // with this exact message, not clamp the altitude or silently proceed.
        [Fact]
        public void Test_swe_heliacal_angle_InvalidGeoAlt_ReturnsErrWithMessage() {
            using (var target = new SwissEph()) {
                double[] dgeo = { 5.333889, 47.853333, 99999 };
                double[] datm = new double[4];
                double[] dobs = new double[6];
                double[] dret = new double[3];
                string serr = null;

                int rc = target.swe_heliacal_angle(SwissEph.J2000, dgeo, datm, dobs, SwissEph.SEFLG_MOSEPH,
                    -3.0, 100.0, 90.0, 95.0, 10.0, dret, ref serr);

                Assert.Equal(SwissEph.ERR, rc);
                Assert.Equal("location for heliacal events must be between -500 and 25000 m above sea", serr);
            }
        }

        // SweHel.cs's HeliacalAngle bisects x over [2, 20] (the domain minx/maxx loop
        // establishes) for the value that minimizes Arc, then sets dangret = [Xm, Ym, Xm - Ym].
        // dangret[2] == dangret[0] - dangret[1] is an algebraic invariant straight out of that
        // assignment, not a value pinned from a single run: any implementation that computed a
        // different Xm or Ym but forgot to keep dangret[2] in sync would fail this immediately,
        // as would one that stopped searching partway and never assigned dangret[0] into [2, 20].
        //
        // dret[0]/dret[1] below are characterization values, not independently derived: they were
        // captured from this test's own inputs run through the current (believed-correct)
        // HeliacalAngle bisection, not hand-computed from the Snellen/atmospheric-extinction
        // formulas the way Test_swe_refrac's constants were. Their job is to pin the bisection's
        // actual behavior so a mutation that discards it (e.g. hard-coding Xm/Ym, or replacing the
        // search with any other fixed pair) is caught -- InRange(2, 20) alone accepts any constant
        // in that domain, and the dret[2] identity above holds even for a hard-coded Xm/Ym pair
        // that never ran the search at all.
        [Fact]
        public void Test_swe_heliacal_angle() {
            using (var target = new SwissEph()) {
                double[] dgeo = { 5.333889, 47.853333, 468 };
                double[] datm = new double[4];
                double[] dobs = new double[6];
                double[] dret = new double[3];
                string serr = null;

                int rc = target.swe_heliacal_angle(SwissEph.J2000, dgeo, datm, dobs, SwissEph.SEFLG_MOSEPH,
                    -3.0, 100.0, 90.0, 95.0, 10.0, dret, ref serr);

                Assert.Equal(SwissEph.OK, rc);
                Assert.InRange(dret[0], 2.0, 20.0);
                Assert.Equal(dret[0] - dret[1], dret[2], 9);
                // Characterization values -- see comment above.
                Assert.Equal(2.65625, dret[0], 6);
                Assert.Equal(8.623237609863281, dret[1], 6);
            }
        }

        // SweHel.cs's TopoArcVisionis ends with "if (Xm < AltO) Xm = AltO; dret = Xm;" -- dret
        // can never come back below the object's own altitude. That is a structural guarantee
        // of the algorithm, not a magic number, but it is not enough on its own: gutting the
        // bisection to "Xm = AltO" (skip the search, always return the floor) also satisfies
        // "dret >= altObj" for every input, since it returns exactly altObj. The pinned value
        // below is a characterization value captured from this test's own inputs run through the
        // current (believed-correct) TopoArcVisionis, not independently derived -- its job is to
        // catch precisely that mutation, which the range check cannot.
        [Fact]
        public void Test_swe_topo_arcus_visionis() {
            using (var target = new SwissEph()) {
                double[] dgeo = { 5.333889, 47.853333, 468 };
                double[] datm = new double[4];
                double[] dobs = new double[6];
                double dret = 0;
                string serr = null;
                const double altObj = 5.0;

                int rc = target.swe_topo_arcus_visionis(SwissEph.J2000, dgeo, datm, dobs, SwissEph.SEFLG_MOSEPH,
                    -3.0, 100.0, altObj, 90.0, 95.0, 10.0, ref dret, ref serr);

                Assert.Equal(SwissEph.OK, rc);
                Assert.True(dret >= altObj, $"dret ({dret}) must never drop below the object's own altitude ({altObj})");
                // Characterization value -- see comment above. Well clear of altObj (5.0), so a
                // mutant that collapses the bisection to the floor is caught even at a loose
                // tolerance.
                Assert.Equal(9.374427795410156, dret, 6);
            }
        }

        // swecl.c's Meeus/Bennett-derived formula (SweCl.cs's swe_refrac): pt_factor =
        // atpress/1010*283/(273+attemp), then for trualt > 15 a plain tangent term. Independently
        // re-derived in Python from external/swisseph/swecl.c (not from the port's own output)
        // and cross-checked against the port to 15 significant digits for both branches used
        // here (trualt > 15 and -5 < trualt <= 15).
        [Fact]
        public void Test_swe_refrac() {
            using (var target = new SwissEph()) {
                Assert.Equal(45.015935361137466, target.swe_refrac(45.0, 1013.25, 15.0, SwissEph.SE_TRUE_TO_APP), 9);
                Assert.Equal(10.088848271817637, target.swe_refrac(10.0, 1013.25, 15.0, SwissEph.SE_TRUE_TO_APP), 9);
                Assert.Equal(45.00020204789334, target.swe_refrac(45.015935361137466, 1013.25, 15.0, SwissEph.SE_APP_TO_TRUE), 9);
            }
        }

        // calc_dip (SweCl.cs), the one part of swe_refrac_extended with a closed form (the
        // apparent-to-true altitude itself is a 5-step Newton iteration, not hand-derivable):
        // krefr = (0.0342 + lapse_rate) / (0.154*0.0238); d = 1 - 1.8480*krefr*atpress /
        // (273.15+attemp)^2; dip = -180/pi * acos(1/(1+geoalt/EARTH_RADIUS)) * sqrt(d). Computed
        // independently in Python from that formula and external/swisseph's EARTH_RADIUS =
        // 6378136.6 (Sweph.h.cs), then cross-checked against the port.
        [Fact]
        public void Test_swe_refrac_extended() {
            using (var target = new SwissEph()) {
                double[] dret = new double[4];
                double trualt = target.swe_refrac_extended(0.5, 1000.0, 1013.25, 15.0, 0.0065, SwissEph.SE_APP_TO_TRUE, dret);

                // dret[0] is a characterization value (captured from this test's own inputs run
                // through the current, believed-correct implementation -- SE_APP_TO_TRUE takes
                // SweCl.cs's non-iterative "trualt = inalt - calc_astronomical_refr(...)" branch,
                // whose closed form is not hand-derived here either, unlike the comment above's
                // dip formula), not the self-comparison "Assert.Equal(dret[0], trualt, 9)" this
                // replaced: that compared the return value
                // to itself (trualt *is* dret[0], SweCl.cs returns it directly), so it passed for
                // any return value including a deleted iteration.
                Assert.Equal(0.03236058205291237, trualt, 9);
                Assert.Equal(trualt, dret[0], 12); // return value mirrors dret[0] (true altitude)
                Assert.Equal(-0.8783545107438215, dret[3], 9); // dip
                // Below the geometric horizon (dret[3], negative) and below dip, an apparent
                // altitude has no valid true altitude: apparent and true both come back as the
                // input, and refraction is reported as zero (SweCl.cs's "else" branch).
                double[] dretBelowDip = new double[4];
                target.swe_refrac_extended(-5.0, 1000.0, 1013.25, 15.0, 0.0065, SwissEph.SE_APP_TO_TRUE, dretBelowDip);
                Assert.Equal(-5.0, dretBelowDip[0], 9);
                Assert.Equal(-5.0, dretBelowDip[1], 9);
                Assert.Equal(0.0, dretBelowDip[2], 9);
            }
        }

        // swe_set_lapse_rate has no getter, and its only consumer is swe_azalt's own internal
        // use of this same private field (SweCl.cs's swe_azalt calls
        // swe_refrac_extended(..., const_lapse_rate, ...)) -- reflection on that field is the
        // only way to observe the setter took effect, the same convention DisposeTest.cs uses
        // for SwissEph's own internal component properties (no InternalsVisibleTo is declared
        // for this assembly).
        [Fact]
        public void Test_swe_set_lapse_rate() {
            using (var target = new SwissEph()) {
                var sweCLProperty = typeof(SwissEph).GetProperty("SweCL", BindingFlags.NonPublic | BindingFlags.Instance);
                var sweCL = sweCLProperty.GetValue(target);
                var lapseRateField = sweCL.GetType().GetField("const_lapse_rate", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.Equal(0.0065, (double)lapseRateField.GetValue(sweCL), 10); // Sweph.h.cs's SE_LAPSE_RATE default

                target.swe_set_lapse_rate(0.5);

                Assert.Equal(0.5, (double)lapseRateField.GetValue(sweCL), 10);
            }
        }

        // swephlib.c documents swe_sidtime(tjd_ut) as swe_sidtime0(tjd_ut, eps + nutlo[1],
        // nutlo[0]) for the mean obliquity/nutation swe_calc(SE_ECL_NUT) computes for that
        // date -- SwephLib.cs's own swe_sidtime body is exactly this call. swe_sidtime is
        // already indirectly oracle-checked (Suite06Houses.cs's armc computation), so
        // reproducing its result through the documented public building blocks is a real check
        // of swe_sidtime0 specifically, not a self-comparison -- but only once "sidtime" itself is
        // pinned to something outside this test: swe_sidtime(tjd_ut) *is* implemented as
        // "return swe_sidtime0(tjd_ut, ...)" (SwephLib.cs), so a mutation to swe_sidtime0's own
        // return value moves both operands of the comparison below together and the comparison
        // alone would never see it. The pinned value is a characterization value (captured from
        // this test's own input run through the current, believed-correct implementation), not
        // independently re-derived from the IAU sidereal-time formula.
        [Fact]
        public void Test_swe_sidtime0() {
            using (var target = new SwissEph()) {
                double tjd_ut = SwissEph.J2000;
                double sidtime = target.swe_sidtime(tjd_ut);
                Assert.Equal(18.697138162535065, sidtime, 6);

                string serr = null;
                double[] xx = new double[6];
                target.swe_calc(tjd_ut, SwissEph.SE_ECL_NUT, 0, xx, ref serr);
                double trueEps = xx[0];       // true obliquity == mean obliquity + nutlo[1]
                double nutationInLongitude = xx[2];

                double sidtime0 = target.swe_sidtime0(tjd_ut, trueEps, nutationInLongitude);

                Assert.Equal(sidtime, sidtime0, 6);
            }
        }

        // swephlib.c's swe_sidtime, already oracle-checked (Suite06Houses.cs), computes armc
        // from swe_degnorm(swe_sidtime0(...) * 15 + geolon) -- a wrong swe_degnorm would move
        // every house-system cusp the corpus checks. Directly: reduction modulo 360 degrees,
        // with the "snap values within 1e-13 to exactly 0" fix noted at SwephLib.cs's
        // swe_degnorm (Alois, 11-dec-1999).
        [Fact]
        public void Test_swe_degnorm() {
            using (var target = new SwissEph()) {
                Assert.Equal(0.0, target.swe_degnorm(0.0), 12);
                Assert.Equal(0.0, target.swe_degnorm(360.0), 12);
                Assert.Equal(270.0, target.swe_degnorm(-90.0), 12);
                Assert.Equal(180.0, target.swe_degnorm(900.0), 12);
            }
        }

        // Reduction modulo 2*pi radians, the radian-domain sibling of swe_degnorm above, with
        // the same "snap near-zero to exactly zero" behavior (SwephLib.cs's swe_radnorm).
        [Fact]
        public void Test_swe_radnorm() {
            using (var target = new SwissEph()) {
                Assert.Equal(0.0, target.swe_radnorm(0.0), 12);
                Assert.Equal(0.0, target.swe_radnorm(2 * Math.PI), 12);
                Assert.Equal(3 * Math.PI / 2, target.swe_radnorm(-Math.PI / 2), 12);
                Assert.Equal(Math.PI, target.swe_radnorm(3 * Math.PI), 12);
            }
        }

        // swe_deg_midp(x1, x0) = swe_degnorm(x0 + swe_difdeg2n(x1, x0) / 2): the midpoint of the
        // shorter arc from x0 to x1. difdeg2n(350, 10) is -20 (the arc from 10 to 350 going
        // backward through 0 is shorter than forward through 180), so the midpoint of 350 and 10
        // through 0 is 0. difdeg2n(100, 80) is +20, so their midpoint is 90 directly.
        [Fact]
        public void Test_swe_deg_midp() {
            using (var target = new SwissEph()) {
                Assert.Equal(0.0, target.swe_deg_midp(350.0, 10.0), 9);
                Assert.Equal(90.0, target.swe_deg_midp(100.0, 80.0), 9);
            }
        }

        // swe_rad_midp is swe_deg_midp with its two arguments and its result converted through
        // DEGTORAD/RADTODEG (SwephLib.cs) -- the same two cases as Test_swe_deg_midp above,
        // converted.
        [Fact]
        public void Test_swe_rad_midp() {
            using (var target = new SwissEph()) {
                Assert.Equal(0.0, target.swe_rad_midp(350.0 * SwissEph.DEGTORAD, 10.0 * SwissEph.DEGTORAD), 9);
                Assert.Equal(Math.PI / 2, target.swe_rad_midp(100.0 * SwissEph.DEGTORAD, 80.0 * SwissEph.DEGTORAD), 9);
            }
        }

        // swe_cotrans (SwephLib.cs) rotates an ecliptic/equatorial polar position by eps about
        // the x-axis. At eps = 90 degrees, a point at (lon=0, lat=45) has cartesian (cos45, 0,
        // sin45); rotating y and z by a quarter turn gives (cos45, sin45, 0), whose polar form is
        // exactly (lon=45, lat=0) -- hand-derived from the rotation formula documented at
        // SwephLib.cs's swi_coortrf, not read off the port's own output.
        [Fact]
        public void Test_swe_cotrans() {
            using (var target = new SwissEph()) {
                double[] xpo = { 0.0, 45.0, 1.0 };
                double[] xpn = new double[3];

                target.swe_cotrans(xpo, xpn, 90.0);

                Assert.Equal(45.0, xpn[0], 9);
                Assert.Equal(0.0, xpn[1], 9);
                Assert.Equal(1.0, xpn[2], 9); // radius/distance passes through unchanged
            }
        }

        // swe_cotrans_sp (SwephLib.cs) is swe_cotrans plus a speed vector. Its own code passes
        // xpn[2] and xpn[5] through unchanged (radial distance and radial speed are not affected
        // by a rotation about the origin) and must agree with plain swe_cotrans on the position
        // it computes from the same lon/lat/eps -- both checked directly against the code's own
        // guarantees rather than a pinned velocity vector.
        //
        // xpn[3]/xpn[4] (the rotated speed in longitude/latitude) are checked too, against values
        // hand-derived independently from first principles (a rotating position vector's
        // cartesian velocity, x = r cos(lat)cos(lon) etc., differentiated and re-projected onto
        // the rotated frame's own lon/lat axes -- not read off the port's own output or reverse
        // engineered from swi_cartpol_sp/swi_polcart_sp's code). At eps=90 deg the quarter turn
        // sends this test's starting point (lon=0, lat=45 deg) to (lon=45 deg, lat=0): its new
        // z-axis is the old frame's -y-axis, and dz/dt at lat=0 equals r*dlat/dt directly, so the
        // new latitude speed is minus the old longitude speed's y-component, i.e.
        // -sin(45deg)*xpo[3]; symmetrically the new longitude speed at lat=0 recovers the old
        // z-axis component of velocity, which reduces to exactly xpo[4] here because sin(45deg) ==
        // cos(45deg) cancels the two frames' trig factors. (These exact identities -- xpn[3] ==
        // xpo[4], xpn[4] == -sin(45deg)*xpo[3] -- are specific to this 45-degree starting latitude
        // and this 90-degree eps, not general facts about the transform.) Without this pair,
        // deleting the two swi_coortrf calls on x[3..5] (the speed-vector rotation) left
        // xpn[3]/xpn[4] equal to the *un-rotated* speed (0.1, 0.2 after DEGTORAD/RADTODEG
        // round-trip), and nothing here caught it.
        [Fact]
        public void Test_swe_cotrans_sp() {
            using (var target = new SwissEph()) {
                double[] xpoPosOnly = { 0.0, 45.0, 1.0 };
                double[] xpnPosOnly = new double[3];
                target.swe_cotrans(xpoPosOnly, xpnPosOnly, 90.0);

                double[] xpo = { 0.0, 45.0, 1.0, 0.1, 0.2, 0.0 };
                double[] xpn = new double[6];

                target.swe_cotrans_sp(xpo, xpn, 90.0);

                Assert.Equal(xpnPosOnly[0], xpn[0], 9);
                Assert.Equal(xpnPosOnly[1], xpn[1], 9);
                Assert.Equal(xpo[2], xpn[2], 12); // radial distance passes through unchanged
                Assert.Equal(xpo[5], xpn[5], 12); // radial speed passes through unchanged
                Assert.Equal(xpo[4], xpn[3], 9);
                Assert.Equal(-Math.Sin(45.0 * SwissEph.DEGTORAD) * xpo[3], xpn[4], 9);
            }
        }

        // swe_get_tid_acc has no assertion-worthy behavior of its own beyond returning what was
        // last written -- a roundtrip through swe_set_tid_acc (already covered elsewhere, see
        // the comment block above) is the direct way to exercise it.
        [Fact]
        public void Test_swe_get_tid_acc() {
            using (var target = new SwissEph()) {
                target.swe_set_tid_acc(3.25);
                Assert.Equal(3.25, target.swe_get_tid_acc(), 12);
            }
        }

        // ideg/imin/isec/isgn from a decimal degree value (SwephLib.cs's swe_split_deg): 123.5
        // degrees is exactly 123 degrees 30 minutes (0.5*60 = 30.0 has an exact binary
        // representation, so no rounding ambiguity), positive sign. -45.25 is exactly 45 degrees
        // 15 minutes, negative sign.
        [Fact]
        public void Test_swe_split_deg() {
            using (var target = new SwissEph()) {
                target.swe_split_deg(123.5, 0, out int ideg1, out int imin1, out int isec1, out double dsecfr1, out int isgn1);
                Assert.Equal(123, ideg1);
                Assert.Equal(30, imin1);
                Assert.Equal(0, isec1);
                Assert.Equal(1, isgn1);

                target.swe_split_deg(-45.25, 0, out int ideg2, out int imin2, out int isec2, out double dsecfr2, out int isgn2);
                Assert.Equal(45, ideg2);
                Assert.Equal(15, imin2);
                Assert.Equal(0, isec2);
                Assert.Equal(-1, isgn2);
            }
        }

        [Fact]
        public void Test_swe_csnorm() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_csnorm(0 * SwissEph.DEG));
                Assert.Equal(3 * SwissEph.DEG, target.swe_csnorm(3 * SwissEph.DEG));
                Assert.Equal(357 * SwissEph.DEG, target.swe_csnorm(-3 * SwissEph.DEG));
                Assert.Equal(3 * SwissEph.DEG, target.swe_csnorm(363 * SwissEph.DEG));
                Assert.Equal(357 * SwissEph.DEG, target.swe_csnorm(-363 * SwissEph.DEG));
            }
        }

        [Fact]
        public void Test_swe_difcsn() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_difcsn(0, 0));
                Assert.Equal(129599997, target.swe_difcsn(0, 3));
                Assert.Equal(3, target.swe_difcsn(3, 0));
                Assert.Equal(0, target.swe_difcsn(3, 3));
            }
        }

        [Fact]
        public void Test_swe_difdegn() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_difdegn(0, 0));
                Assert.Equal(357, target.swe_difdegn(0, 3));
                Assert.Equal(3, target.swe_difdegn(3, 0));
                Assert.Equal(0, target.swe_difdegn(3, 3));
            }
        }

        [Fact]
        public void Test_swe_difcs2n() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_difcs2n(0, 0));
                Assert.Equal(-3, target.swe_difcs2n(0, 3));
                Assert.Equal(3, target.swe_difcs2n(3, 0));
                Assert.Equal(0, target.swe_difcs2n(3, 3));
            }
        }

        [Fact]
        public void Test_swe_difdeg2n() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_difdeg2n(0, 0));
                Assert.Equal(-3, target.swe_difdeg2n(0, 3));
                Assert.Equal(3, target.swe_difdeg2n(3, 0));
                Assert.Equal(0, target.swe_difdeg2n(3, 3));
            }
        }

        [Fact]
        public void Test_swe_difrad2n() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_difrad2n(0, 0));
                Assert.Equal(-3, target.swe_difrad2n(0, 3));
                Assert.Equal(3, target.swe_difrad2n(3, 0));
                Assert.Equal(0, target.swe_difrad2n(3, 3));
            }
        }

        [Fact]
        public void Test_swe_csroundsec() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, target.swe_csroundsec(0));
                Assert.Equal(10440000, target.swe_csroundsec(29 * SwissEph.DEG));
                Assert.Equal(10799900, target.swe_csroundsec(10800000 - 40));
                Assert.Equal(10800000, target.swe_csroundsec(30 * SwissEph.DEG));
                Assert.Equal(-10439900, target.swe_csroundsec(-29 * SwissEph.DEG));
                Assert.Equal(-10799900, target.swe_csroundsec(-(10800000 - 40)));
                Assert.Equal(-10799900, target.swe_csroundsec(-30 * SwissEph.DEG));
                
                Assert.Equal(1200, target.swe_csroundsec(1234));
                Assert.Equal(9900, target.swe_csroundsec(9876));
                Assert.Equal(-1100, target.swe_csroundsec(-1234));
                Assert.Equal(-9800, target.swe_csroundsec(-9876));
            }
        }

        [Fact]
        public void Test_swe_d2l() {
            using (var target = new SwissEph()) {
                Assert.Equal(0, SwissEph.swe_d2l(0));
                Assert.Equal(123, SwissEph.swe_d2l(123.45));
                Assert.Equal(124, SwissEph.swe_d2l(123.987));
                Assert.Equal(-123, SwissEph.swe_d2l(-123.45));
                Assert.Equal(-124, SwissEph.swe_d2l(-123.987));
            }
        }

        [Fact]
        public void Test_swe_cs2timestr() {
            using (var target = new SwissEph()) {
                Assert.Equal("00-00-00", target.swe_cs2timestr(0, '-', false));
                Assert.Equal("00-00", target.swe_cs2timestr(0, '-', true));
                Assert.Equal("00:00:00", target.swe_cs2timestr(0, ':', false));
                Assert.Equal("00:00", target.swe_cs2timestr(0, ':', true));

                Assert.Equal("03:25:46", target.swe_cs2timestr(1234567, ':', false));
                Assert.Equal("03:25:46", target.swe_cs2timestr(1234567, ':', true));

                Assert.Equal("0-:.+:,+", target.swe_cs2timestr(-1234567, ':', false));
                Assert.Equal("0-:.+:,+", target.swe_cs2timestr(-1234567, ':', true));
            }
        }

        [Fact]
        public void Test_swe_cs2lonlatstr() {
            using (var target = new SwissEph()) {
                // swephlib.c:3906-3911 places pchar between the degrees' units digit and the
                // minutes (a[2]=h%10, a[3]=pchar, a[4..5]=m), not before the units digit. These
                // three values were verified against pyswisseph 2.10.03 directly.
                Assert.Equal("0p00", target.swe_cs2lonlatstr(0, 'p', 'm'));
                Assert.Equal("3p25'46", target.swe_cs2lonlatstr(1234567, 'p', 'm'));
                Assert.Equal("3m25'46", target.swe_cs2lonlatstr(-1234567, 'p', 'm'));
            }
        }

        [Fact]
        public void Test_swe_cs2degstr() {
            using (var target = new SwissEph()) {
                Assert.Equal(" 0°00'00", target.swe_cs2degstr(0));
                Assert.Equal(" 3°25'45", target.swe_cs2degstr(1234567));
            }
        }
    }
}
