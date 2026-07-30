using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{
    
    public class Issue15Test
    {
        [Fact]
        public void TestSideral()
        {
            using (var sweph = new SwissEph())
            {
                int jday = 1, jmon = 1, jyear = 2001;
                double jut = 0;
                double[] x2 = new double[6];
                Int32 iflag, iflgret;
                iflag = SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_SIDEREAL | SwissEph.SEFLG_NONUT;
                string snam = string.Empty, serr = String.Empty;

                var tjd = sweph.swe_julday(jyear, jmon, jday, jut, SwissEph.SE_GREG_CAL);
                var te = tjd + sweph.swe_deltat(tjd);

                int delta = 6;

                Assert.Equal(2451910.5, tjd);
                Assert.Equal(2451910.500742, te, delta);

                // SE_SIDM_LAHIRI is one of the four ayanamsa[] rows whose value changed in
                // 2.10.03, and its row also carries prec_offset = SEMOD_PREC_IAU_1976.
                //
                // These are libswe 2.10.03 values, verified against pyswisseph 2.10.3.2 at
                // this jd and these flags: all twelve agree to within 5e-7.
                //
                // They arrived in two steps. Porting the table alone moved the ayanamsa
                // 23.871032 -> 23.871035 -- an underlying +0.000002187 deg, the ayan_t0 change
                // on its own (0.004660222 - 0.004658035) -- and the constants
                // stage pinned that intermediate value while recording that it should move
                // again. Porting get_aya_correction, which reads prec_offset, supplies the
                // remaining 0.000036 and lands on the reference.
                sweph.swe_set_sid_mode(SwissEph.SE_SIDM_LAHIRI, 0, 0);
                double ayanamsa = sweph.swe_get_ayanamsa(te);

                Assert.Equal(23.871071, ayanamsa, delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_SUN, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(256.766806, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_MOON, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(324.839200, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_MERCURY, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(260.404911, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_VENUS, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(303.100151, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_MARS, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(191.071212, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_JUPITER, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(38.323450, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_SATURN, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(30.724458, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_URANUS, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(294.783472, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_NEPTUNE, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(281.460604, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_PLUTO, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(229.901488, x2[0], delta);

                iflgret = sweph.swe_calc(te, SwissEph.SE_MEAN_NODE, iflag, x2, ref serr);
                Assert.Equal(iflgret, iflag);
                Assert.Equal(81.818844, x2[0], delta);
            }

        }
    }
}
