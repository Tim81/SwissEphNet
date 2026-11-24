using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_deltat_ex under the Delta T models of v2.10.04: the default is now SEMOD_DELTAT_STEPHENSON_ETC_2020
    /// (model 6, Morrison/Stephenson/Hohenkerk/Zawilski 2020), and the earlier model 5 (Stephenson/Morrison/
    /// Hohenkerk 2016, SE2.06's) is selected with swe_set_astro_models("5,9,9,4,3,0,0,4", 0) or "SE2.06".
    /// Before 1 Jan 1962 (JD 2437665.5) model 6 blends into the observed values over the last 1000 days,
    /// which JD 2437000 lies in; model 5 blends there too, to different values.
    ///
    /// Reference values: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD), a fresh process per model, %.17g:
    ///   [swe_set_astro_models(samod, 0);]            // only for the models other than the default
    ///   for each tjd below, in this order:  dt = swe_deltat_ex(tjd, SEFLG_MOSEPH, serr);   // serr empty
    /// tjd are, in order: 1 Jan 1000, 1 Jan 1900, 2 Jul 1961 (1961.5), 1 Jan 1962, 1 Jan 1975,
    /// 2 Jul 2023 (2023.5), 1 Jan 2030, 1 Jan 2040 (all 0h Gregorian), then 2436665.5 (the start of the
    /// 1000-day blend), 2437000.0, 2437665.0 and 2437665.5 - 1e-9. The values are in days.
    /// </summary>
    public class DeltaTModelsTest
    {
        private static readonly (double Tjd, double Dt)[] Default =
        {
            (2086302.5, 0.018871612655972776),
            (2415020.5, -2.366114080476338e-05),
            (2437482.5, 0.00039113359533466557),
            (2437665.5, 0.00039352538493982983),
            (2442413.5, 0.00052635135602933968),
            (2460127.5, 0.00080078606819022175),
            (2462502.5, 0.00080984368666400927),
            (2466154.5, 0.00084145086010416691),
            (2436665.5, 0.00037941676165074691),
            (2437000, 0.00038491262480559865),
            (2437665, 0.00039350188058333302),
            (2437665.4999999991, 0.0003935086519666541),
        };

        private static readonly (double Tjd, double Dt)[] Model5 =
        {
            (2086302.5, 0.016726901931961108),
            (2415020.5, -2.375860521760907e-05),
            (2437482.5, 0.00039112981012362637),
            (2437665.5, 0.00039352538493982983),
            (2442413.5, 0.00052635135602933968),
            (2460127.5, 0.00080078606819022175),
            (2462502.5, 0.00080984368666400927),
            (2466154.5, 0.00084145086010416691),
            (2436665.5, 0.00037971100470305145),
            (2437000, 0.00038462621426793987),
            (2437665, 0.00039351851851851852),
            (2437665.4999999991, 0.00039352538493981736),
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
        public void DefaultModel_MatchesTheC()
        {
            using var swe = new SwissEph();
            Check(Default, swe);
        }

        [Fact]
        public void Model5ByNumbers_MatchesTheC()
        {
            using var swe = new SwissEph();
            swe.swe_set_astro_models("5,9,9,4,3,0,0,4", 0);
            Check(Model5, swe);
        }

        [Theory]
        [InlineData("SE2.06")]
        [InlineData("5,9,9,4,3,0,0,4")]
        public void Se206SelectsModel5(string samod)
        {
            using var swe = new SwissEph();
            swe.swe_set_astro_models(samod, 0);
            Check(Model5, swe);
        }

        [Theory]
        [InlineData("")]
        [InlineData("SE2.10.04")]
        [InlineData("6,9,9,4,3,0,0,4")]
        public void CurrentVersionOrEmptySelectsModel6(string samod)
        {
            using var swe = new SwissEph();
            swe.swe_set_astro_models("5,9,9,4,3,0,0,4", 0);   // move off the default first
            swe.swe_set_astro_models(samod, 0);
            Check(Default, swe);
        }

        [Fact]
        public void BlendIsContinuousAtTheStartOf1962()
        {
            // Not a C value: 1 Jan 1962 itself is outside the blend, the day before is inside it, and the
            // two models differ in between but agree at both ends (see the tables above).
            using var swe6 = new SwissEph();
            using var swe5 = new SwissEph();
            swe5.swe_set_astro_models("5,9,9,4,3,0,0,4", 0);
            string serr = null;
            double a6 = swe6.swe_deltat_ex(2437000.0, SwissEph.SEFLG_MOSEPH, ref serr);
            double a5 = swe5.swe_deltat_ex(2437000.0, SwissEph.SEFLG_MOSEPH, ref serr);
            Assert.NotEqual(a6, a5);
            Assert.Equal(
                swe6.swe_deltat_ex(2437665.5, SwissEph.SEFLG_MOSEPH, ref serr),
                swe5.swe_deltat_ex(2437665.5, SwissEph.SEFLG_MOSEPH, ref serr));
        }
    }
}
