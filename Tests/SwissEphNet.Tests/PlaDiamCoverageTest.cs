using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Despite the class name, this is not full coverage of pla_diam[] -- that array is
    /// read at eight sites total in SweCl.cs (lines 725, 1029, 1707, 2525, 3956, 3971,
    /// 4276 and 4568). The tests below (TestPheno*) reach exactly one of those, line 3956
    /// (swe_pheno's attr[3]); TestRiseTrans_SunDiscRadius_UsesPlaDiam near the bottom of
    /// this file adds a second, line 4276's rise/transit disc-radius consumer. The rest
    /// remain unreached by this class; the claim below is scoped to attr[] specifically,
    /// not to pla_diam[] as a whole.
    ///
    /// Pins swe_pheno/swe_pheno_ut's apparent-diameter output (attr[3]) for the six
    /// bodies whose pla_diam[] entry the port's upstream 2.10.03 delta will change
    /// (SwissEphNet/CPort/Sweph.h.cs, upstream sweph.h): Chiron, Pholus, Ceres,
    /// Pallas, Juno and Vesta. See swe_pheno in SwissEphNet/CPort/SweCl.cs -- dd is
    /// read from pla_diam[ipl] and attr[3] is asin(dd / 2 / AUNIT / lbr[2]) * 2 *
    /// RADTODEG (external/swisseph/swecl.c has the same formula), so attr[3] is
    /// the one field in attr[] that depends on pla_diam[] for these bodies. No
    /// other attr[] index reads pla_diam[] for a body outside SE_SUN/SE_MOON.
    ///
    /// The characterization baseline (Tests/baseline/) always configures
    /// SwissEph.DefaultFileProvider to a no-op provider (Tools/BaselineGen/Program.cs),
    /// and swe_calc's dispatch for these six bodies (Sweph.cs, the
    /// "minor planets" branch) always requests the seas_18.se1 asteroid file
    /// regardless of the ephemeris flag passed in -- SEFLG_MOSEPH included -- so
    /// without a file these calls return ERR before swe_pheno ever reads
    /// pla_diam[]. Nothing in this repository currently exercises the code path
    /// this test pins. This file closes that gap.
    ///
    /// Fixture: Tests/SwissEphNet.Tests/files/seas_18.se1, a byte-identical copy of
    /// external/swisseph/ephe/seas_18.se1 (sha256
    /// df4e9a08186f91e2c91f454ee2d404bf5ecbe61500b2324c28d26e6da2076dc6), embedded
    /// as a resource the same way se00005s.se1 already is for Issue18Test. A
    /// fixture copy, not a submodule read, because Tests/SwissEphNet.Tests is
    /// built and run by .github/workflows/ci.yml, whose checkout step does not
    /// fetch submodules -- only .github/workflows/conformance.yml does that, for
    /// Tests/SwissEphNet.Conformance.Tests specifically. A test here that read
    /// external/swisseph directly would fail for every contributor who has not
    /// run the submodule-init recipe in CONTRIBUTING.md.
    ///
    /// 16 August 1974 is the date already used by Issue18Test, Issue27Test and
    /// Issue41Test in this project; reusing it keeps the century-file math
    /// (SwephLib.swi_gen_filename, NCTIES=6) landing on the same seas_18.se1 this
    /// fixture provides. It also sits inside CHIRON_START..CHIRON_END
    /// (Sweph.h.cs: JD 1967601.5 .. JD 3419437.5, roughly AD 675 to AD 4650), the
    /// restricted range Sweph.cs enforces for SE_CHIRON before dispatch.
    ///
    /// SEFLG_MOSEPH, not SEFLG_SWIEPH: main_planet (Sweph.cs) computes Earth and
    /// the Sun analytically under SEFLG_MOSEPH, so the only ephemeris file this
    /// test's FileProvider ever has to serve is seas_18.se1 -- the exact
    /// blind spot described above, and nothing more.
    ///
    /// Precision: 11 decimal places on the asserted attr[3] values, the same
    /// order as the AbsoluteEpsilon (1e-12) Tools/BaselineVerify/Comparer.cs uses
    /// for the characterization gate. Loose enough to survive the platform-level
    /// floating-point noise measured in docs/known-issues.md's "Cross-platform
    /// divergence" section (Windows vs. Linux), tight enough that the 2.10.03
    /// diameter change -- Ceres 913000 -> 939400 m and similar double-digit-percent
    /// moves for the other five bodies -- fails it by many orders of magnitude, not
    /// by a rounding hair.
    /// </summary>
    public class PlaDiamCoverageTest
    {
        // pla_diam[] is indexed by planet number for these bodies -- see
        // Sweph.h.cs's pla_diam initializer and swe_pheno's `dd = Sweph.pla_diam[ipl]`.
        // Expected attr[3] (apparent diameter of disk, in degrees) at 2.10.03's pla_diam[]
        // values. Derived from libswe 2.10.03 itself (pyswisseph 2.10.3.2) at the same jd
        // and flags this test uses, not from this port's own output, so the assertion is
        // against the reference rather than against ourselves.
        //
        // All six bodies now have a diameter. Chiron and Pholus were 0.0 at 2.08, which is
        // what the deleted zero-diameter theories pinned; 2.10.03 gives them 271370 m and
        // 290000 m, so attr[3] is small but nonzero and there is no longer any body in this
        // set whose apparent diameter is zero.
        //
        // swe_pheno and swe_pheno_ut differ slightly for the same nominal date because
        // swe_pheno_ut treats tjd as UT and adds delta T before calling swe_pheno.
        public static IEnumerable<object[]> AsteroidBodiesForPheno()
        {
            yield return new object[] { SwissEph.SE_CHIRON, "Chiron", 5.754055783607351E-06 };
            yield return new object[] { SwissEph.SE_PHOLUS, "Pholus", 5.222978703825473E-06 };
            yield return new object[] { SwissEph.SE_CERES, "Ceres", 0.00017743935901133728 };
            yield return new object[] { SwissEph.SE_PALLAS, "Pallas", 7.962465673159846E-05 };
            yield return new object[] { SwissEph.SE_JUNO, "Juno", 6.572201190153193E-05 };
            yield return new object[] { SwissEph.SE_VESTA, "Vesta", 8.47332971098369E-05 };
        }

        public static IEnumerable<object[]> AsteroidBodiesForPhenoUt()
        {
            yield return new object[] { SwissEph.SE_CHIRON, "Chiron", 5.754058300036313E-06 };
            yield return new object[] { SwissEph.SE_PHOLUS, "Pholus", 5.222979514275479E-06 };
            yield return new object[] { SwissEph.SE_CERES, "Ceres", 0.0001774395462238981 };
            yield return new object[] { SwissEph.SE_PALLAS, "Pallas", 7.962457767539558E-05 };
            yield return new object[] { SwissEph.SE_JUNO, "Juno", 6.57221930647249E-05 };
            yield return new object[] { SwissEph.SE_VESTA, "Vesta", 8.47331134739173E-05 };
        }

        static void SubscribeSeasFixture(SwissEph swe)
        {
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                string fn = ResourceFileHelpers.GetPortableFileName(path);
                if (File.Exists(path))
                {
                    return new FileStream(path, FileMode.Open, FileAccess.Read);
                }
                return ResourceFileHelpers.OpenResourceFile(fn);
            });
        }

        [Theory]
        [MemberData(nameof(AsteroidBodiesForPheno))]
        public void TestPhenoApparentDiameterMatchesPlaDiam(int ipl, string name, double expectedApparentDiameterDegrees)
        {
            // Arrange
            using (var swe = new SwissEph())
            {
                SubscribeSeasFixture(swe);
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] attr = new double[20];
                string serr = null;

                // Act
                int res = swe.swe_pheno(tjd, ipl, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                // Assert
                Assert.False(res == SwissEph.ERR, $"{name}: {serr}");
                Assert.Equal(expectedApparentDiameterDegrees, attr[3], 11);
            }
        }

        [Theory]
        [MemberData(nameof(AsteroidBodiesForPhenoUt))]
        public void TestPhenoUtApparentDiameterMatchesPlaDiam(int ipl, string name, double expectedApparentDiameterDegrees)
        {
            // Arrange
            using (var swe = new SwissEph())
            {
                SubscribeSeasFixture(swe);
                double tjd_ut = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] attr = new double[20];
                string serr = null;

                // Act
                int res = swe.swe_pheno_ut(tjd_ut, ipl, SwissEph.SEFLG_MOSEPH, attr, ref serr);

                // Assert
                Assert.False(res == SwissEph.ERR, $"{name}: {serr}");
                Assert.Equal(expectedApparentDiameterDegrees, attr[3], 11);
            }
        }

        // ---------------------------------------------------------------------------------
        // Coverage gap this class's own docstring above used to overclaim past: it documents
        // swe_pheno/swe_pheno_ut's attr[3] read of pla_diam[] (SweCl.cs, around line 3956),
        // which is real, but pla_diam[] is read at eight sites total in SweCl.cs (725, 1029,
        // 1707, 2525, 3956, 3971, 4276, 4568); this class only ever reached that one. The
        // rise/transit disc-radius consumer at SweCl.cs get_sun_rad_plus_refr (around line
        // 4276: rdi = asin(pla_diam[ipl] / 2.0 / AUNIT / dd) * RADTODEG) had no coverage
        // anywhere in the suite: swe_rise_trans's "fast" path (rise_set_fast) reaches it for
        // any body in [SE_SUN, SE_TRUE_NODE] whenever SE_BIT_DISC_CENTER is not requested, and
        // SEFLG_MOSEPH needs no ephemeris file for the Sun. Inverting "/ 2.0" to "* 2.0" there
        // roughly doubles the Sun's assumed apparent radius, shifting the moment its upper limb
        // crosses the horizon -- confirmed to move tret well outside the pinned precision below.
        // ---------------------------------------------------------------------------------
        [Fact]
        public void TestRiseTrans_SunDiscRadius_UsesPlaDiam()
        {
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(2000, 6, 1, 0.0, SwissEph.SE_GREG_CAL);
                double[] geopos = { 5.333889, 47.853333, 468 }; // lon, lat, alt (m); |lat| <= 60
                double tret = 0;
                string serr = null;

                // rsmi = SE_CALC_RISE only: no SE_BIT_DISC_CENTER (which would skip the
                // pla_diam read entirely, rdi = 0) and no SE_BIT_FIXED_DISC_SIZE (which would
                // override dd to a hardcoded 1.0 for the Sun before pla_diam is applied).
                int rc = swe.swe_rise_trans(tjd, SwissEph.SE_SUN, null, SwissEph.SEFLG_MOSEPH,
                    SwissEph.SE_CALC_RISE, geopos, 0, 0, ref tret, ref serr);

                Assert.Equal(SwissEph.OK, rc);
                Assert.Equal(2451696.655453675, tret, 9);
            }
        }
    }
}
