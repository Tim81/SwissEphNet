using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_deltat_ex before 1 Jan -720 (JD 1458077.5, the start of dtcf20). Upstream re-tagged v2.10.04 at
    /// 24e47d42; its commit 27c94dee changed deltat_stephenson_etc_2020 (swephlib.c:3070-3077 at 24e47d42) so
    /// that for tjd &lt; 1458077.5 (with iyear &lt; -720) the long-term parabola is
    ///   dt = 65.62 + 31.351922682 * T^2,  T = (tjd - 2387627.5) / 36525
    /// (Julian centuries since 1825 Jan 1.0, as in JPL Horizons), replacing
    ///   dt = 65 + 31.351922682 * T^2 + 0.6754326,  T = (dyear - 1825) / 100.
    /// The default model (SEMOD_DELTAT_STEPHENSON_ETC_2020) is affected. Model 5 (Stephenson/Morrison/Hohenkerk
    /// 2016) is pinned too as a control: its own -720 branch is not touched by 27c94dee.
    ///
    /// Reference values: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// 24e47d42; MSVC /O2 /fp:precise /MD), a fresh process per model, %.17g:
    ///   [swe_set_astro_models(samod, 0);]            // only for model 5
    ///   for each tjd below, in this order:  dt = swe_deltat_ex(tjd, SEFLG_MOSEPH, serr);   // serr empty
    /// The values are in days.
    /// </summary>
    public class DeltaTBeforeMinus720Test
    {
        private static readonly (double Tjd, double Dt)[] Default =
        {
            (0, 1.5401253137190427),
            (625000.5, 0.83961147940805281),
            (1000000, 0.52059309083751182),
            (1173182.5, 0.3989063264502557),
            (1458077, 0.23397467471449818),
            (1458077.4999999991, 0.23397442372926897),
            (1458077.5, 0.23397443321488345),
            (1458085.5, 0.23397037425882727),
        };

        private static readonly (double Tjd, double Dt)[] Model5 =
        {
            (0, 1.5890235967335162),
            (625000.5, 0.86327250373517794),
            (1000000, 0.53276236039343861),
            (1173182.5, 0.40669265095534946),
            (1458077, 0.23582125418590866),
            (1458077.4999999991, 0.23582099416292202),
            (1458077.5, 0.23582099416292146),
            (1458085.5, 0.23581683381395249),
        };

        private static void Check((double Tjd, double Dt)[] table, SwissEph swe)
        {
            foreach (var (tjd, dt) in table)
            {
                string serr = null;
                double actual = swe.swe_deltat_ex(tjd, SwissEph.SEFLG_MOSEPH, ref serr);
                CReference.Equal(dt, actual, 1e-12, $"deltat_ex({tjd:R})");
                Assert.True(string.IsNullOrEmpty(serr), serr);
            }
        }

        [Fact]
        public void DefaultModel_BeforeMinus720_MatchesTheC()
        {
            using var swe = new SwissEph();
            Check(Default, swe);
        }

        [Fact]
        public void Model5_BeforeMinus720_MatchesTheC()
        {
            using var swe = new SwissEph();
            swe.swe_set_astro_models("5,9,9,4,3,0,0,4", 0);
            Check(Model5, swe);
        }

        // Not a C value. Upstream states the parabola meets the spline at -720 to within 0.001 s. The old
        // formula met it to 2.2e-8 s (the 0.6754326 term existed for that) and the new one leaves a step of
        // -0.00082 s, so this guards continuity within 0.001 s and does not by itself detect the change;
        // the change itself is guarded by DefaultModel_BeforeMinus720_MatchesTheC.
        [Fact]
        public void DefaultModel_MeetsTheSplineAtMinus720()
        {
            using var swe = new SwissEph();
            string serr = null;
            double below = swe.swe_deltat_ex(1458077.4999999991, SwissEph.SEFLG_MOSEPH, ref serr);
            double at = swe.swe_deltat_ex(1458077.5, SwissEph.SEFLG_MOSEPH, ref serr);
            Assert.True(string.IsNullOrEmpty(serr), serr);
            Assert.True(System.Math.Abs(at - below) * 86400 < 0.001,
                $"step at -720 is {System.Math.Abs(at - below) * 86400} s");
        }
    }
}
