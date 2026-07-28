using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Regression tests for two house-API fidelity defects found by the conformance
    /// oracle. Each is documented at its fix site with the C file/line it diverged
    /// from; see also docs/known-issues.md.
    ///
    /// Defect 1: swephexp.h:812-835 declares "int hsys" on every house entry point.
    /// The port had narrowed all of them to "char hsys" (SwissEphNet/CPort/SweHouse.cs).
    /// C truncates hsys to a char only at the CalcH call inside swe_houses_armc
    /// (swehouse.c:661); the outer functions swe_house_name (swehouse.c:829) and
    /// swe_house_pos (swehouse.c:2233/:2835) compare the raw, untruncated int, so an
    /// out-of-range value falls through to their default: branch instead of being
    /// truncated to a char first. A char parameter cannot express that.
    ///
    /// Defect 2: SweHouse.cs used "double[] hcusp = new double[36]" inside
    /// swe_house_pos. Upstream 2.10.03 swehouse.c:2224 declares "double hcusp[37]"
    /// (C 2.08 declared hcusp[36], which this port originally matched faithfully --
    /// 2.10.03 fixed a real bug there). swe_houses_armc writes cusp[36] for
    /// hsys == 'G' (Gauquelin, ito = 36), which needs length 37, so swe_house_pos
    /// threw IndexOutOfRangeException for Gauquelin before this fix.
    /// </summary>
    public class HouseApiFidelityTest
    {
        // --- Defect 1: swe_house_name ---

        [Fact]
        public void TestHouseName_RawOutOfRangeInt_FallsThroughToPlacidusDefault()
        {
            using (var swe = new SwissEph())
            {
                // 32592 never equals any house-system letter (all < 128), whether raw
                // or ASCII-uppercased, so it must hit the default: branch, exactly like
                // C's switch on the untruncated int (swehouse.c:829).
                string name = swe.swe_house_name(32592);
                Assert.Equal("Placidus", name);
            }
        }

        [Fact]
        public void TestHouseName_CharP_ReturnsPlacidus()
        {
            using (var swe = new SwissEph())
            {
                Assert.Equal("Placidus", swe.swe_house_name('P'));
            }
        }

        [Fact]
        public void TestHouseName_CharK_ReturnsKoch()
        {
            using (var swe = new SwissEph())
            {
                Assert.Equal("Koch", swe.swe_house_name('K'));
            }
        }

        // --- Defect 1: swe_houses_armc internal 8-bit truncation (swehouse.c:661) ---

        [Fact]
        public void TestHousesArmc_RawOutOfRangeInt_ResolvesToPlacidusInternallyViaTruncation()
        {
            // 32592 = 0x7F50; 0x7F50 & 0xFF = 0x50 = 'P'. The *outer* ito/sunshine
            // checks in swe_houses_armc compare the raw, untruncated int (never equal
            // 'G' or 'I'), but the CalcH call at the bottom truncates to (char) via
            // "& 0xFF", so the cusps computed must be identical to an explicit
            // hsys = 'P' call.
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;

            double[] cuspPlacidus = new double[13];
            double[] ascmcPlacidus = new double[10];
            double[] cuspRaw = new double[13];
            double[] ascmcRaw = new double[10];

            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, 'P', cuspPlacidus, ascmcPlacidus);
            }
            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, 32592, cuspRaw, ascmcRaw);
            }

            Assert.Equal(cuspPlacidus, cuspRaw);
            Assert.Equal(ascmcPlacidus, ascmcRaw);
        }

        // --- Defect 1: swe_house_pos default: branch for a raw out-of-range hsys ---

        [Fact]
        public void TestHousePos_RawOutOfRangeInt_TakesSimplifiedDefaultBranch()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            // A position that does not land exactly on a Placidus cusp, so the
            // function falls through the "no calculation required" shortcut and
            // reaches the switch on hsys.
            double[] xpin = { 55.0, 0.0 };

            using (var swe = new SwissEph())
            {
                string serr = null;
                double hpos = swe.swe_house_pos(armc, geolat, eps, 32592, xpin, ref serr);

                // The default: branch (swehouse.c:2233/:2835) sets this exact
                // diagnostic; it is only reached when no named case matched.
                Assert.Contains("simplified algorithm", serr ?? string.Empty, StringComparison.Ordinal);
                // A house position is documented to lie in [1, 13).
                Assert.InRange(hpos, 0.0, 13.0);
            }
        }

        // --- Defect 2: swe_house_pos with hsys = 'G' (Gauquelin) must not throw ---

        [Fact]
        public void TestHousePos_Gauquelin_DoesNotThrowAndReturnsValueInDocumentedRange()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            double[] xpin = { 55.0, 0.0 };

            using (var swe = new SwissEph())
            {
                string serr = null;
                double hpos = 0;
                var ex = Record.Exception(() => hpos = swe.swe_house_pos(armc, geolat, eps, 'G', xpin, ref serr));

                Assert.Null(ex);
                // Gauquelin sectors: hpos = xp[0] / 10.0 + 1, xp[0] in [0, 360), so
                // hpos is documented to lie in [1, 37).
                Assert.InRange(hpos, 1.0, 37.0);
            }
        }

        [Fact]
        public void TestHousesArmc_Gauquelin_WritesAllThirtySixCuspsWithoutThrowing()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            double[] cusp = new double[37];
            double[] ascmc = new double[10];

            using (var swe = new SwissEph())
            {
                var ex = Record.Exception(() => swe.swe_houses_armc(armc, geolat, eps, 'G', cusp, ascmc));
                Assert.Null(ex);
            }

            for (int i = 1; i <= 36; i++)
            {
                Assert.InRange(cusp[i], 0.0, 360.0);
            }
        }

        // --- Defect 2: reached via swe_gauquelin_sector (SweCL.cs), per docs/known-issues.md ---

        [Fact]
        public void TestGauquelinSector_DoesNotThrow()
        {
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(2000, 6, 1, 12.0, SwissEph.SE_GREG_CAL);
                double[] geopos = { 5.333889, 47.853333, 468 };
                double dgsect = 0;
                string serr = null;

                var ex = Record.Exception(() => swe.swe_gauquelin_sector(
                    tjd, SwissEph.SE_SUN, null, SwissEph.SEFLG_MOSEPH, 0, geopos, 0, 0, ref dgsect, ref serr));

                Assert.Null(ex);
                Assert.InRange(dgsect, 1.0, 37.0);
            }
        }
    }
}
