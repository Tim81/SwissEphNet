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
            // The original report needed a real C:\Temp\ephe\jplfiles\de430.eph, a machine-local
            // path no CI runner has, guarded by a File.Exists check that made the one assertion
            // below unreachable everywhere except the reporter's own machine. swe_rise_trans with
            // SEFLG_JPLEPH but no JPL file available does not need one to exercise the reported
            // crash: sweph.c's ephemeris selection falls back to Moshier and still calls all the
            // same code the NullReferenceException came from, just via a different underlying
            // ephemeris. Confirmed this reproduces the fallback path (rc=OK, serr carries a
            // "using Moshier eph." warning, a real risetime gets computed) without any ephemeris
            // file at all, so the regression is now covered on every CI run instead of never.
            string jplfolder = @"C:\Temp\ephe\jplfiles";
            string file = "de430.eph";

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

                // The issue raised a NullReferenceException on this instruction. The
                // regression is "does not throw", not the pos-array assertion below, which a
                // no-op implementation would satisfy just as well.
                var ex = Record.Exception(() => swe.swe_rise_trans(
                    jd,
                    SwissEph.SE_MOON,
                    null,
                    SwissEph.SEFLG_JPLEPH,
                    SwissEph.SE_CALC_SET,
                    pos,
                    1013.25,
                    20,
                    ref risetime,
                    ref serr));

                Assert.Null(ex);
                // geopos is an input the C never writes back through (swecl.c's rise/transit
                // search only reads it); confirms the call did not clobber the caller's array.
                Assert.Equal(new double[] { 0, 48, 0 }, pos);
                // Falls back to Moshier rather than failing outright, and actually computes a
                // risetime instead of leaving it at its zero default.
                Assert.NotEqual(0, risetime);
                Assert.Contains("Moshier", serr ?? string.Empty, StringComparison.Ordinal);
            }
        }

    }
}
