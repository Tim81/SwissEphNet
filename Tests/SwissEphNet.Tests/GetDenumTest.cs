using SwissEphNet.CPort;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_get_denum, new upstream in 9a8b7592 (sweph.c:240): <c>return swi_get_denum(SEI_SUN, 0);</c>.
    /// With flags 0 that reads the planet file slot, swed.fidat[SEI_FILE_PLANET], and falls back
    /// to SE_DE_NUMBER when that slot has no DE number recorded.
    /// </summary>
    public class GetDenumTest
    {
        [Fact]
        public void NoPlanetFileOpen_ReturnsSeDeNumber()
        {
            using (var swe = new SwissEph())
            {
                Assert.Equal(SwissEph.SE_DE_NUMBER, swe.swe_get_denum());
            }
        }

        [Fact]
        public void MoshierCalculation_OpensNoPlanetFile_StillReturnsSeDeNumber()
        {
            using (var swe = new SwissEph())
            {
                double[] xx = new double[6];
                string serr = null;
                swe.swe_calc(swe.swe_julday(2000, 1, 1, 12, SwissEph.SE_GREG_CAL), SwissEph.SE_SUN, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.Equal(SwissEph.SE_DE_NUMBER, swe.swe_get_denum());
            }
        }

        // The test fixtures carry no sepl_*.se1 planet file to load for real, so the planet slot's
        // recorded DE number is set directly: this pins that swe_get_denum reads that slot, the
        // one swi_get_denum(SEI_SUN, 0) selects, rather than a constant or another slot.
        [Fact]
        public void ReturnsTheDeNumberRecordedForThePlanetFile()
        {
            using (var swe = new SwissEph())
            {
                swe.Sweph.swed.fidat[Sweph.SEI_FILE_PLANET].sweph_denum = 406;

                Assert.Equal(406, swe.swe_get_denum());
            }
        }
    }
}
