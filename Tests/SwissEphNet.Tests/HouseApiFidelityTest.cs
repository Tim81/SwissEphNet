using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Regression tests for three house-API fidelity defects. Each is documented at its
    /// fix site with the C file/line it diverged from; see also docs/known-issues.md.
    ///
    /// Defect 1: SweHouse.cs passes hsys to C.sprintf under %c at several sites.
    /// C.printf.cs's %c handler boxes a char and calls Convert.ToChar(int) when the
    /// boxed value happens to be int (see C.printf.cs IsNumericType, which lists int
    /// but not char), which throws OverflowException outside [0,65535]. C's %c has no
    /// such limitation: it converts the int vararg to unsigned char (swehouse.c:2872).
    /// Fixed by narrowing to (char)(hsys &amp; 0xFF) before formatting.
    ///
    /// Defect 2: swehouse.c:661's CalcH(..., (char)hsys, ...) truncates to a *signed*
    /// char (plain char is signed on the reference platforms, x86-64 Windows and
    /// Linux), so a low byte &gt;= 0x80
    /// becomes negative there. SweHouse.cs previously reproduced only the width of that
    /// truncation, not its signedness ("&amp; 0xFF", which is always non-negative), so
    /// CalcH's `hsy &gt; 95` sign check (SweHouse.cs, used to fold lower-case letters to
    /// upper-case) took the wrong branch for such inputs. Fixed by narrowing via
    /// (sbyte)hsys -- C#'s (sbyte) cast on an int matches C's (char) cast on a signed
    /// char platform -- and widening CalcH's parameter to int so the sign survives.
    ///
    /// Defect 3: swe_house_pos declared its internal hcusp array one element short.
    /// swe_houses_armc writes cusp[36] for hsys = 'G' (Gauquelin, ito = 36), so every
    /// swe_house_pos call with 'G' threw IndexOutOfRangeException, as did every caller
    /// reaching the same path indirectly (swe_gauquelin_sector). The port was faithful
    /// to C 2.08, which has the same off-by-one; upstream 2.10.03 (swehouse.c:2224)
    /// declares hcusp[37]. Fixed here ahead of the full 2.10.03 re-transliteration.
    /// </summary>
    public class HouseApiFidelityTest
    {
        // 65611 = 0x1004B; low byte 0x4B = 'K'. Chosen so that raw-int (C-faithful)
        // comparisons -- which never match any house-system letter, since 65611 is far
        // outside char range -- and a wrongly-truncating comparison against the low
        // byte alone ('K') produce different, observable results.
        const int RawKLowByte = 65611;

        // ---------------------------------------------------------------------------
        // Defect 1 (and general int-widening correctness): swe_house_name
        // ---------------------------------------------------------------------------

        [Fact]
        public void TestHouseName_RawOutOfRangeInt_FallsThroughToPlacidusDefault()
        {
            using (var swe = new SwissEph())
            {
                // swehouse.c:829-830: toupper() is applied to the raw, untruncated int
                // and compared against case labels without narrowing, so 65611 (which
                // equals no house-system letter, raw or truncated-to-16-bits) falls to
                // the default: case (Placidus). A wrongly-truncating implementation
                // that narrows hsys to its low byte before comparing would instead
                // match case 'K' and return "Koch" -- so this assertion can fail.
                string name = swe.swe_house_name(RawKLowByte);
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

        [Fact]
        public void TestHouseName_NegativeInt_DoesNotThrowAndFallsThroughToPlacidusDefault()
        {
            using (var swe = new SwissEph())
            {
                string name = null;
                var ex = Record.Exception(() => name = swe.swe_house_name(-1));
                Assert.Null(ex);
                Assert.Equal("Placidus", name);
            }
        }

        [Fact]
        public void TestHouseName_AboveUInt16Max_DoesNotThrowAndFallsThroughToPlacidusDefault()
        {
            using (var swe = new SwissEph())
            {
                string name = null;
                var ex = Record.Exception(() => name = swe.swe_house_name(70000));
                Assert.Null(ex);
                Assert.Equal("Placidus", name);
            }
        }

        // ---------------------------------------------------------------------------
        // Defect 1: the %c sites in swe_houses_armc (trace) and swe_house_pos crash
        // on out-of-range hsys before the fix. These must not throw.
        // ---------------------------------------------------------------------------

        [Fact]
        public void TestHousesArmc_NegativeInt_DoesNotThrow()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            double[] cusp = new double[37];
            double[] ascmc = new double[10];

            using (var swe = new SwissEph())
            {
                var ex = Record.Exception(() => swe.swe_houses_armc(armc, geolat, eps, -1, cusp, ascmc));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void TestHousesArmc_AboveUInt16Max_DoesNotThrow()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            double[] cusp = new double[37];
            double[] ascmc = new double[10];

            using (var swe = new SwissEph())
            {
                var ex = Record.Exception(() => swe.swe_houses_armc(armc, geolat, eps, 70000, cusp, ascmc));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void TestHousePos_NegativeInt_DoesNotThrowAndTakesSimplifiedDefaultBranch()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            // A position that does not land exactly on a cusp, so the function falls
            // through the "no calculation required" shortcut and reaches the switch on
            // hsys, exercising the default: branch's %c site (swehouse.c:2872).
            double[] xpin = { 55.0, 0.0 };

            using (var swe = new SwissEph())
            {
                string serr = null;
                double hpos = 0;
                var ex = Record.Exception(() => hpos = swe.swe_house_pos(armc, geolat, eps, -1, xpin, ref serr));

                Assert.Null(ex);
                Assert.Contains("simplified algorithm", serr ?? string.Empty, StringComparison.Ordinal);
                // C's %c converts the int vararg to unsigned char, so the message must
                // carry the low byte: -1 & 0xFF = 0xFF. Pinning the character, not just
                // "did not throw", is what distinguishes the C-faithful narrowing from
                // any other in-range one (& 0xFFFF, Math.Abs, a placeholder).
                Assert.Contains("system ÿ", serr ?? string.Empty, StringComparison.Ordinal);
                Assert.InRange(hpos, 0.0, 13.0);
            }
        }

        [Fact]
        public void TestHousePos_AboveUInt16Max_DoesNotThrowAndTakesSimplifiedDefaultBranch()
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;
            double[] xpin = { 55.0, 0.0 };

            using (var swe = new SwissEph())
            {
                string serr = null;
                double hpos = 0;
                var ex = Record.Exception(() => hpos = swe.swe_house_pos(armc, geolat, eps, 70000, xpin, ref serr));

                Assert.Null(ex);
                Assert.Contains("simplified algorithm", serr ?? string.Empty, StringComparison.Ordinal);
                // 70000 & 0xFF = 112 = 'p'. See the sibling test for why the character
                // itself is asserted rather than only the absence of an exception.
                Assert.Contains("system p", serr ?? string.Empty, StringComparison.Ordinal);
                Assert.InRange(hpos, 0.0, 13.0);
            }
        }

        [Fact]
        public void TestHousesEx_NegativeInt_DoesNotThrow()
        {
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(2000, 6, 1, 12.0, SwissEph.SE_GREG_CAL);
                double[] cusp = new double[37];
                double[] ascmc = new double[10];

                var ex = Record.Exception(() => swe.swe_houses_ex(tjd, 0, 40.0, -70.0, -1, cusp, ascmc));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void TestHouses_AboveUInt16Max_DoesNotThrow()
        {
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(2000, 6, 1, 12.0, SwissEph.SE_GREG_CAL);
                double[] cusp = new double[37];
                double[] ascmc = new double[10];

                var ex = Record.Exception(() => swe.swe_houses(tjd, 40.0, -70.0, 70000, cusp, ascmc));
                Assert.Null(ex);
            }
        }

        // ---------------------------------------------------------------------------
        // Defect 2: low byte 0x89 (137). CalcH's C-faithful signed narrowing yields
        // sbyte -119, which fails `hsy > 95` and falls to CalcH's own default: case
        // (Placidus, swehouse.c: default /* Placidus houses */). An unsigned "& 0xFF"
        // narrowing yields 137, which passes `hsy > 95` and is folded (137 - 32 = 105
        // = 'i') to the Sunshine/Makransky house system -- a materially different,
        // wrong result, not just a formatting artifact.
        // ---------------------------------------------------------------------------

        [Fact]
        public void TestHousesArmc_LowByte0x89_ResolvesToPlacidusNotSunshine()
        {
            const int rawLowByte0x89 = 0x189; // low byte 0x89 = 137
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;

            double[] cuspRaw = new double[13];
            double[] ascmcRaw = new double[10];
            double[] cuspPlacidus = new double[13];
            double[] ascmcPlacidus = new double[10];
            double[] cuspSunshineI = new double[13];
            double[] ascmcSunshineI = new double[10];

            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, rawLowByte0x89, cuspRaw, ascmcRaw);
            }
            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, 'P', cuspPlacidus, ascmcPlacidus);
            }
            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, 'i', cuspSunshineI, ascmcSunshineI);
            }

            Assert.Equal(cuspPlacidus, cuspRaw);
            Assert.Equal(ascmcPlacidus, ascmcRaw);
            Assert.NotEqual(cuspSunshineI, cuspRaw);
        }

        // ---------------------------------------------------------------------------
        // Defect 2, char-path pin: (char)331's low byte is 0x4B = 'K'. Routing a char
        // through the int overload now applies the same signed-8-bit narrowing C
        // applies, which this port did not do before this branch (a `char` argument
        // used to be forwarded directly, unnarrowed). Measured: (char)331 resolved to
        // Placidus before this branch and to Koch after -- i.e. the fix moved this
        // case from silently ignoring the low byte to reproducing it, matching C.
        // ---------------------------------------------------------------------------

        [Fact]
        public void TestHousesArmc_CharAboveLatin1_ResolvesByLowByteLikeC()
        {
            const char charAboveLatin1 = (char)331; // low byte 0x4B = 'K'
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;

            double[] cuspFromChar = new double[13];
            double[] ascmcFromChar = new double[10];
            double[] cuspKoch = new double[13];
            double[] ascmcKoch = new double[10];

            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, charAboveLatin1, cuspFromChar, ascmcFromChar);
            }
            using (var swe = new SwissEph())
            {
                swe.swe_houses_armc(armc, geolat, eps, 'K', cuspKoch, ascmcKoch);
            }

            Assert.Equal(cuspKoch, cuspFromChar);
            Assert.Equal(ascmcKoch, ascmcFromChar);
        }

        // ---------------------------------------------------------------------------
        // Defect 3: swe_house_pos with hsys = 'G' (Gauquelin) must not throw
        // (hcusp[36] -> hcusp[37], swehouse.c:2224).
        // ---------------------------------------------------------------------------

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

        // --- Defect 3: reached via swe_gauquelin_sector (SweCL.cs), per docs/known-issues.md ---

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
