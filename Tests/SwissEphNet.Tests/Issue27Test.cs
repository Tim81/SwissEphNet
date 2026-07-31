using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Issue: https://github.com/ygrenier/SwissEphNet/issues/27
    /// </summary>
    public class Issue27Test
    {
        [Fact]
        public void Test()
        {
            string jplfolder = @"C:\Temp\ephe\jplfiles";
            string file = "de430.eph";

            // Skip test if the file not exists
            if (!File.Exists(Path.Combine(jplfolder, file)))
                return;

            int jday = 1, jmon = 1, jyear = 2017;
            double jut = 0.0;
            string serr = null;

            using (var swe = new SwissEph())
            {
                // OnLoadFile used to be wired to open real files off disk here, which is
                // exactly what SwissEph.OpenBinary does by default now that no FileProvider is
                // configured.
                swe.swe_set_ephe_path(jplfolder);
                swe.swe_set_jpl_file(file);

                var jd = swe.swe_julday(jyear, jmon, jday, jut, SwissEph.SE_GREG_CAL);

                double[] pos = new double[] { 0, 48, 0 };
                double risetime = 0;

                // The issue raise a NullReferenceException on this instruction
                swe.swe_rise_trans(
                    jd,
                    SwissEph.SE_MOON,
                    null,
                    SwissEph.SEFLG_JPLEPH,
                    SwissEph.SE_CALC_SET,
                    pos,
                    1013.25,
                    20,
                    ref risetime,
                    ref serr);

                Assert.Equal(new double[] { 0, 48, 0 }, pos);
            }
        }

    }
}
