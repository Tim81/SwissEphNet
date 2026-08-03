using System;
using System.Globalization;
using Xunit;

namespace SwissEphNet.NetStandard20Smoke.Tests
{
    /// <summary>
    /// Exercises SwissEphNet's netstandard2.0 asset specifically (see the
    /// remarks in NetStandard20Smoke.Tests.csproj for why net48 is what forces
    /// that resolution). Each test here targets a call site that historically
    /// relied on StringExtensions.Contains(this string, char) resolving on
    /// netstandard2.0 -- where no instance string.Contains(char) exists, so
    /// the call binds to that extension method instead of a BCL method. A
    /// broken implementation of that extension (calling s.Contains(c) instead
    /// of s.Contains(c.ToString()) inside its own body) recurses into itself
    /// unboundedly there: a StackOverflowException, which is uncatchable and
    /// terminates the test process outright rather than failing a single test.
    /// If this project ever regresses to that, expect the whole run to crash,
    /// not a red test -- that is exactly the failure mode these tests exist
    /// to make visible during ordinary `dotnet test`, instead of only in
    /// whatever consumer eventually loads the shipped netstandard2.0 DLL under
    /// .NET Framework.
    /// </summary>
    public class NetStandard20SmokeTests
    {
        [Fact]
        public void StringExtensionsContainsChar_DoesNotRecurse()
        {
            // The extension method itself, called directly. This is the
            // most direct possible check: if its body ever regresses to
            // s.Contains(c) (recursing into itself on this TFM), this call
            // alone crashes the process before the assertion even runs.
            Assert.True("co-op".Contains('-'));
            Assert.False("coop".Contains('-'));
            Assert.True(StringExtensions.Contains("test", new[] { 'x', 't' }));
            Assert.False(StringExtensions.Contains("test", new[] { 'x', 'y' }));
        }

        [Fact]
        public void SweGetAstroModels_WithPlusFlag_DoesNotRecurse()
        {
            // SwephLib.cs:4497's swe_get_astro_models (public API, reached via
            // SwissEph) checks samod.Contains('+') as its very first operation
            // on the input -- CPort code, untouched by this PR's fix, that has
            // always depended on the same extension method for its
            // netstandard2.0 behavior.
            using (var swe = new SwissEph())
            {
                swe.swe_get_astro_models("+", out var sdet, 0);
                Assert.NotNull(sdet);
            }
        }

        [Theory]
        [InlineData("[%'u]", 65537)]   // flagGroupThousands: the ' flag
        [InlineData("[%#o]", 8)]       // flagAlternate: the # flag
        [InlineData("[%-10d]", 42)]    // flagLeft2Right: the - flag
        [InlineData("[%+d]", 42)]      // flagPositiveSign: the + flag
        public void CSprintf_FlagParsing_DoesNotRecurse(string format, int value)
        {
            // C.printf.cs's format-flag parsing (flagAlternate/flagLeft2Right/
            // flagPositiveSign/flagPositiveSpace/flagGroupThousands) used to
            // route through flags.Contains(char); it is back to
            // flags.IndexOf(char) >= 0, but this pins the whole flag set
            // (#, -, +, space, ') so any future regression back to Contains(char)
            // is caught immediately, on the TFM that actually manifests it.
            var result = C.sprintf(format, value);
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void CSprintf_PositiveSpaceFlag_DoesNotRecurse()
        {
            var result = C.sprintf("[% d]", 42);
            Assert.Equal("[ 42]", result);
        }

        [Fact]
        public void CSscanf_ScanSet_DoesNotRecurse()
        {
            // C.scanf.cs's ParseScanSet (a %[...] format specifier) used to
            // route through spec.ScanSet.Contains(char); it is back to
            // spec.ScanSet.IndexOf(char) >= 0.
            string result = null;
            C.sscanf("abcabc123", "%[abc]", ref result);
            Assert.Equal("abcabc", result);
        }

        [Fact]
        public void CAtof_Overflow_ReturnsInfinity()
        {
            // C.cs's atof: on this TFM, double.TryParse returns false for an
            // overflowing literal like "1e999" instead of true-with-Infinity
            // (net8.0/net10.0's behavior). Unguarded, the longest-parseable-
            // prefix backoff loop then found "1e99" -- a finite, wrong,
            // plausible-looking number -- instead of the double.PositiveInfinity
            // C's strtod (HUGE_VAL) returns on overflow. This is the TFM where
            // that divergence actually showed; see Tests/SwissEphNet.Tests'
            // CTest.TestAtofOverflowReturnsInfinity for the same assertion on
            // net8.0/net10.0, where it already passed before the fix.
            Assert.Equal(double.PositiveInfinity, C.atof("1e999"));
            Assert.Equal(double.NegativeInfinity, C.atof("-1e999"));
            Assert.Equal(2.10, C.atof("2.10.03"));
        }
    }
}
