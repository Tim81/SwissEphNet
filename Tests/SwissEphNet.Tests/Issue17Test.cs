using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SwissEphNet.Tests
{

    public class Issue17Test
    {
        [Fact]
        public void strcpy_VBsafe()
        {
            using (var sweph = new SwissEph())
            {
                double[] dummy = new double[12];
                String serr = null;

                // The issue raised an IndexOutOfRangeException. The all-zero dummy array (reused
                // for geopos, datm, dobs and dret alike, as the original report did) is not a
                // physically meaningful observing site, so this legitimately fails to find a
                // heliacal event rather than succeeding -- the point is that it fails cleanly
                // with a determinate rc/serr instead of throwing.
                int rc = sweph.swe_heliacal_ut(SwissEph.J2000, dummy, dummy, dummy, "moon", 0, 0, dummy, ref serr);

                Assert.Equal(-2, rc);
                Assert.Equal("heliacal event does not happen", serr);
            }
        }
    }
}
