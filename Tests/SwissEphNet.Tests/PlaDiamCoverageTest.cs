using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Pins swe_pheno/swe_pheno_ut's apparent-diameter output (attr[3]) for the six
    /// bodies whose pla_diam[] entry the port's upstream 2.10.03 delta will change
    /// (SwissEphNet/CPort/Sweph.h.cs, upstream sweph.h): Chiron, Pholus, Ceres,
    /// Pallas, Juno and Vesta. See swe_pheno in SwissEphNet/CPort/SweCl.cs -- dd is
    /// read from pla_diam[ipl] and attr[3] is asin(dd / 2 / AUNIT / lbr[2]) * 2 *
    /// RADTODEG (external/swisseph/swecl.c has the same formula), so attr[3] is
    /// the one field in attr[] that depends on pla_diam[] for these bodies. No
    /// other attr[] index reads pla_diam[] for a body outside SE_SUN/SE_MOON.
    ///
    /// The characterization baseline (Tests/baseline/) never subscribes to
    /// OnLoadFile, and swe_calc's dispatch for these six bodies (Sweph.cs, the
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
    /// test's OnLoadFile handler ever has to serve is seas_18.se1 -- the exact
    /// blind spot described above, and nothing more.
    ///
    /// Precision: 11 decimal places on the asserted attr[3] values, the same
    /// order as the AbsoluteEpsilon (1e-12) Tools/BaselineVerify/Comparer.cs uses
    /// for the characterization gate. Loose enough to survive the platform-level
    /// floating-point noise CLAUDE.md documents (Windows vs. Linux), tight enough
    /// that the 2.10.03 diameter change -- Ceres 913000 -> 939400 m and similar
    /// double-digit-percent moves for the other five bodies -- fails it by many
    /// orders of magnitude, not by a rounding hair.
    /// </summary>
    public class PlaDiamCoverageTest
    {
        // pla_diam[] is indexed by planet number for these bodies -- see
        // Sweph.h.cs's pla_diam initializer and swe_pheno's `dd = Sweph.pla_diam[ipl]`.
        public static IEnumerable<object[]> AsteroidBodiesWithZeroDiameterAt208()
        {
            yield return new object[] { SwissEph.SE_CHIRON, "Chiron" };
            yield return new object[] { SwissEph.SE_PHOLUS, "Pholus" };
        }

        // Expected attr[3] values (apparent diameter of disk, in degrees), pinned
        // from an actual run of this test against seas_18.se1 with pla_diam[] at
        // its current (port 2.08) values. swe_pheno and swe_pheno_ut give
        // slightly different values for the same nominal date, because
        // swe_pheno_ut treats tjd_ut as UT and adds delta T internally before
        // calling swe_pheno with the resulting ET jd.
        public static IEnumerable<object[]> AsteroidBodiesWithNonZeroDiameterAt208ForPheno()
        {
            yield return new object[] { SwissEph.SE_CERES, "Ceres", 0.00017245277280960968 };
            yield return new object[] { SwissEph.SE_PALLAS, "Pallas", 7.6410450404817862E-05 };
            yield return new object[] { SwissEph.SE_JUNO, "Juno", 6.5030133919340833E-05 };
            yield return new object[] { SwissEph.SE_VESTA, "Vesta", 8.0798214411929842E-05 };
        }

        public static IEnumerable<object[]> AsteroidBodiesWithNonZeroDiameterAt208ForPhenoUt()
        {
            yield return new object[] { SwissEph.SE_CERES, "Ceres", 0.00017245295476092759 };
            yield return new object[] { SwissEph.SE_PALLAS, "Pallas", 7.6410374539874547E-05 };
            yield return new object[] { SwissEph.SE_JUNO, "Juno", 6.5030313175367223E-05 };
            yield return new object[] { SwissEph.SE_VESTA, "Vesta", 8.0798039304210556E-05 };
        }

        static void SubscribeSeasFixture(SwissEph swe)
        {
            swe.OnLoadFile += (s, e) =>
            {
                string fn = ResourceFileHelpers.GetPortableFileName(e.FileName);
                if (File.Exists(e.FileName))
                {
                    e.File = new FileStream(e.FileName, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    e.File = ResourceFileHelpers.OpenResourceFile(fn);
                }
            };
        }

        [Theory]
        [MemberData(nameof(AsteroidBodiesWithZeroDiameterAt208))]
        public void TestPhenoApparentDiameterIsZeroForBodiesWithNoPla208Diameter(int ipl, string name)
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
                Assert.Equal(0.0, attr[3]);
            }
        }

        [Theory]
        [MemberData(nameof(AsteroidBodiesWithNonZeroDiameterAt208ForPheno))]
        public void TestPhenoApparentDiameterMatchesPla208Diameter(int ipl, string name, double expectedApparentDiameterDegrees)
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
        [MemberData(nameof(AsteroidBodiesWithZeroDiameterAt208))]
        public void TestPhenoUtApparentDiameterIsZeroForBodiesWithNoPla208Diameter(int ipl, string name)
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
                Assert.Equal(0.0, attr[3]);
            }
        }

        [Theory]
        [MemberData(nameof(AsteroidBodiesWithNonZeroDiameterAt208ForPhenoUt))]
        public void TestPhenoUtApparentDiameterMatchesPla208Diameter(int ipl, string name, double expectedApparentDiameterDegrees)
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
    }
}
