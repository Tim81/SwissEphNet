using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SwissEphNet.Tests
{

    public class CTest
    {
    
        [Fact]
        public void TestAtof() {
            Assert.Equal(0.0, C.atof(null));
            Assert.Equal(0.0, C.atof(""));
            Assert.Equal(0.0, C.atof("test"));
            Assert.Equal(0.0, C.atof("0"));
            Assert.Equal(0.0, C.atof("0.0"));
            Assert.Equal(1.0, C.atof("1"));
            Assert.Equal(1.2, C.atof("1.2"));
            Assert.Equal(1.0, C.atof("+1"));
            Assert.Equal(1.2, C.atof("+1.2"));
            Assert.Equal(-1.0, C.atof("-1"));
            Assert.Equal(-1.2, C.atof("-1.2"));
        }

        [Fact]
        public void TestAtofOverflowReturnsInfinity()
        {
            // C's strtod (atof is defined in terms of it) returns HUGE_VAL,
            // i.e. infinity, on overflow -- never a smaller finite number.
            // netstandard2.0/net48's double.TryParse returns false for an
            // overflowing literal instead of true-with-Infinity the way
            // net8.0/net10.0's does; left unguarded, the longest-parseable-
            // prefix loop then backs "1e999" off to "1e99" (a finite, wrong,
            // and plausible-looking result) instead of matching HUGE_VAL. See
            // Tests/NetStandard20Smoke.Tests for the net462/net48 run of this
            // same assertion, which is where that divergence actually shows.
            Assert.Equal(double.PositiveInfinity, C.atof("1e999"));
            Assert.Equal(double.NegativeInfinity, C.atof("-1e999"));
            // Still malformed, not merely overflowing: the backoff loop must
            // still find "2.10" as the longest valid prefix, not treat the
            // second decimal point as evidence of overflow.
            Assert.Equal(2.10, C.atof("2.10.03"));
        }

        [Fact]
        public void TestAtoi() {
            Assert.Equal(0, C.atoi(null));
            Assert.Equal(0, C.atoi(""));
            Assert.Equal(0, C.atoi("test"));
            Assert.Equal(0, C.atoi("0"));
            Assert.Equal(1, C.atoi("1"));
            Assert.Equal(12, C.atoi("12"));
            Assert.Equal(1, C.atoi("1.2"));
            Assert.Equal(1, C.atoi("+1"));
            Assert.Equal(-1, C.atoi("-1"));
            Assert.Equal(-5, C.atoi("-5"));
            Assert.Equal(-12, C.atoi("-12abc"));
            Assert.Equal(0, C.atoi("-"));
            Assert.Equal(0, C.atoi("+"));
            Assert.Equal(0, C.atoi("-abc"));
        }

        [Fact]
        public void TestFmod()
        {
            Assert.Equal(1.0, C.fmod(3, 2), 8);
            Assert.Equal(1.3, C.fmod(5.3, 2), 8);
            Assert.Equal(1.7, C.fmod(18.5, 4.2), 8);
            Assert.Equal(0.5, C.fmod(18.5, 1), 8);
            Assert.Equal(0.5, C.fmod(5.7, 1.3), 8);
        }

        // Turkish culture is the standard, well-documented deterministic
        // demonstration of .NET's culture-sensitive string comparison,
        // reproducing identically across every OS/.NET version: dotless "i"
        // (U+0131) sorts *before* dotted "i" (U+0069) under Turkish collation
        // rules, but ordinally U+0131 (305) > U+0069 (105), so an ordinal
        // comparison gives the opposite sign. Every test below temporarily
        // swaps CurrentCulture to tr-TR and restores it in a finally block --
        // CurrentCulture is a per-thread property, and each test method runs
        // synchronously to completion on one thread, so this cannot leak into
        // another test even when xUnit runs test classes in parallel.
        const string DotlessI = "ı";

        static void WithTurkishCulture(Action action)
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                action();
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void TestStrcmpIsOrdinalNotCultureSensitive()
        {
            // C's strcmp is a byte-by-byte ordinal comparison, independent of
            // any locale. string.Compare(a, b) (no StringComparison) is
            // CurrentCulture-aware instead.
            WithTurkishCulture(() =>
            {
                Assert.True(C.strcmp(DotlessI, "i") > 0);
                Assert.True(C.strcmp("i", DotlessI) < 0);
            });

            Assert.Equal(0, C.strcmp("abc", "abc"));
            Assert.True(C.strcmp("abc", "abd") < 0);
            Assert.True(C.strcmp("abd", "abc") > 0);
        }

        [Fact]
        public void TestStrncmpIsOrdinalNotCultureSensitive()
        {
            WithTurkishCulture(() =>
            {
                Assert.True(C.strncmp(DotlessI, "i", 1) > 0);
                Assert.True(C.strncmp("i", DotlessI, 1) < 0);
            });

            Assert.Equal(0, C.strncmp("abcxxx", "abcyyy", 3));
            Assert.True(C.strncmp("abc", "abd", 3) < 0);
        }

        [Fact]
        public void TestStrstrIsOrdinal()
        {
            // string.IndexOf(string) with no StringComparison is documented
            // CurrentCulture-aware on every TFM this project targets, so
            // C.strstr uses StringComparison.Ordinal explicitly, which is the
            // only choice guaranteed byte-exact on every TFM. That said: this
            // specific assertion set is plain ASCII with no culture wrapper,
            // and passes against the pre-fix implementation too on the ICU
            // version this was verified against (repeated attempts at finding
            // an input where CurrentCulture-vs-Ordinal actually disagree on
            // *substring presence*, including Turkish dotless/dotted "i" and
            // ligature-folding cases that work for Compare, did not turn up
            // one here) -- so treat this as characterization of the current,
            // correct behavior, not a red-before/green-after regression test.
            // TestStrcmpIsOrdinalNotCultureSensitive/
            // TestStrncmpIsOrdinalNotCultureSensitive above are the ones with
            // an actual demonstrated pre-fix failure.
            Assert.Equal(1, C.strstr("xabc", "abc"));
            Assert.Equal(-1, C.strstr("xabc", "xyz"));
            Assert.Equal(0, C.strstr("abc", "abc"));
            Assert.Equal(-1, C.strstr("", "a"));
            Assert.Equal(-1, C.strstr("a", ""));
            Assert.Equal(-1, C.strstr(null, "a"));
        }

        [Fact]
        public void TestStrchrIsOrdinal()
        {
            // Unlike strcmp/strncmp/strstr, C.strchr was never actually
            // culture-sensitive: string.IndexOf(char) (no StringComparison)
            // is documented as performing an ordinal comparison already, on
            // every TFM. C.strchr moved off string.IndexOf(char) to a manual
            // loop only because string.IndexOf(char, StringComparison) --
            // the explicit overload -- is not part of the netstandard2.0 API
            // surface, not because the previous behavior was wrong. This is
            // a characterization test of already-correct behavior, not a
            // regression test: it passes against the pre-fix implementation
            // too.
            Assert.Equal(1, C.strchr("xabc", 'a'));
            Assert.Equal(-1, C.strchr("xabc", 'z'));
            Assert.Equal(-1, C.strchr("", 'a'));
            Assert.Equal(-1, C.strchr(null, 'a'));
        }

    }
}
