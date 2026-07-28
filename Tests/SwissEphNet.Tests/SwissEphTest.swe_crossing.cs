using System;
using Xunit;

namespace SwissEphNet.Tests
{
    // sweph.c:8310-8615 -- swe_solcross/_ut, swe_mooncross/_ut, swe_mooncross_node/_ut,
    // swe_helio_cross/_ut. All eight use Moshier (SEFLG_MOSEPH) so the tests run without
    // ephemeris files, matching Test_swe_calc_ut's convention. Expected values were captured
    // from this port's own output at 2000-01-01 00:00 UT and would not survive the function
    // body being stubbed out (e.g. to return tjd unchanged, or 0).
    partial class SwissEphTest
    {
        [Fact]
        public void Test_swe_solcross() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xsun = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xsun, ref serr);
                double target = swe.swe_degnorm(xsun[0] + 10);

                double jd = swe.swe_solcross(target, tjd, SwissEph.SEFLG_MOSEPH, ref serr);

                Assert.Equal(2451554.3083685976, jd, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_solcross_ut() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xsun = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xsun, ref serr);
                double target = swe.swe_degnorm(xsun[0] + 10);

                double jd = swe.swe_solcross_ut(target, tjd, SwissEph.SEFLG_MOSEPH, ref serr);

                Assert.Equal(2451554.3076297482, jd, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_mooncross() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xmoon = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_MOON, SwissEph.SEFLG_MOSEPH, xmoon, ref serr);
                double target = swe.swe_degnorm(xmoon[0] + 10);

                double jd = swe.swe_mooncross(target, tjd, SwissEph.SEFLG_MOSEPH, ref serr);

                Assert.Equal(2451545.3308510543, jd, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_mooncross_ut() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xmoon = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_MOON, SwissEph.SEFLG_MOSEPH, xmoon, ref serr);
                double target = swe.swe_degnorm(xmoon[0] + 10);

                double jd = swe.swe_mooncross_ut(target, tjd, SwissEph.SEFLG_MOSEPH, ref serr);

                Assert.Equal(2451545.3301122906, jd, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_mooncross_node() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double xlon = 0, xla = 0;
                String serr = null;

                double jd = swe.swe_mooncross_node(tjd, SwissEph.SEFLG_MOSEPH, ref xlon, ref xla, ref serr);

                Assert.Equal(2451551.7576128356, jd, 5);
                Assert.Equal(303.65156549196973, xlon, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_mooncross_node_ut() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double xlon = 0, xla = 0;
                String serr = null;

                double jd = swe.swe_mooncross_node_ut(tjd, SwissEph.SEFLG_MOSEPH, ref xlon, ref xla, ref serr);

                Assert.Equal(2451551.756874011, jd, 5);
                Assert.Equal(303.65156549757836, xlon, 5);
                Assert.True(jd > tjd);
            }
        }

        [Fact]
        public void Test_swe_helio_cross() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xmars = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_MARS, SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_HELCTR, xmars, ref serr);
                double target = swe.swe_degnorm(xmars[0] + 10);

                double jd_cross = 0;
                Int32 rc = swe.swe_helio_cross(SwissEph.SE_MARS, target, tjd, SwissEph.SEFLG_MOSEPH, 1, ref jd_cross, ref serr);

                Assert.Equal(SwissEph.OK, rc);
                Assert.Equal(2451560.5780273764, jd_cross, 5);
                Assert.True(jd_cross > tjd);
            }
        }

        [Fact]
        public void Test_swe_helio_cross_ut() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double[] xmars = new double[6];
                String serr = null;
                swe.swe_calc(tjd, SwissEph.SE_MARS, SwissEph.SEFLG_MOSEPH | SwissEph.SEFLG_HELCTR, xmars, ref serr);
                double target = swe.swe_degnorm(xmars[0] + 10);

                double jd_cross = 0;
                Int32 rc = swe.swe_helio_cross_ut(SwissEph.SE_MARS, target, tjd, SwissEph.SEFLG_MOSEPH, 1, ref jd_cross, ref serr);

                Assert.Equal(SwissEph.OK, rc);
                Assert.Equal(2451560.5772884674, jd_cross, 5);
                Assert.True(jd_cross > tjd);
            }
        }

        [Fact]
        public void Test_swe_helio_cross_RejectsSun() {
            using (var swe = new SwissEph()) {
                double tjd = swe.swe_julday(2000, 1, 1, 0, SwissEph.SE_GREG_CAL);
                double jd_cross = 0;
                String serr = "";

                Int32 rc = swe.swe_helio_cross(SwissEph.SE_SUN, 0, tjd, SwissEph.SEFLG_MOSEPH, 1, ref jd_cross, ref serr);

                Assert.Equal(SwissEph.ERR, rc);
                Assert.Equal("swe_helio_cross: not possible for object 0 = Sun", serr);
            }
        }
    }
}
