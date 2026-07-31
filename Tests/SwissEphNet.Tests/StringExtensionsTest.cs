using System;
using Xunit;

namespace SwissEphNet.Tests
{

    public class StringExtensionsTest
    {
        [Fact]
        public void TestContainsChar() {
            String s = null;
            // Called as a static method, not member-access syntax: `s?.Contains(...)` short-circuits
            // on a compile-time-null `s` without ever invoking anything, and instance member syntax
            // on a non-null string would bind to System.String's own Contains(char, StringComparison)
            // overload rather than this extension. The static call form is the only way to reach
            // StringExtensions.Contains(this string, char) itself and exercise its
            // String.IsNullOrEmpty(s) guard against a real null.
            Assert.False(StringExtensions.Contains(s, 'a'));

            Assert.False("".Contains('a', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('a', StringComparison.Ordinal));
            Assert.True("AbCd".Contains('b', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('c', StringComparison.Ordinal));
            Assert.True("AbCd".Contains('d', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('e', StringComparison.Ordinal));
            Assert.True("AbCd".Contains('A', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('B', StringComparison.Ordinal));
            Assert.True("AbCd".Contains('C', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('D', StringComparison.Ordinal));
            Assert.False("AbCd".Contains('E', StringComparison.Ordinal));
        }

        [Fact]
        public void TestContainsCharSet() {
            String s = null;
            Char[] charSet = new char[] { 'A', 'c' };

            Assert.False(s.Contains(charSet));
            Assert.False(s.Contains((Char[])null));
            Assert.False("--".Contains(charSet));
            Assert.False("--".Contains((Char[])null));

            Assert.False("".Contains(charSet));
            Assert.True("ABCD".Contains(charSet));
            Assert.True("abcd".Contains(charSet));
            Assert.False("xyz".Contains(charSet));

        }

        [Fact]
        public void TestIndexOfFirstNot() {
            String s = null;
            Char[] charSet = new char[] { 'A', 'c' };

            Assert.Equal(-1, s.IndexOfFirstNot(charSet));
            Assert.Equal(-1, s.IndexOfFirstNot());
            Assert.Equal(0, "--".IndexOfFirstNot(charSet));
            Assert.Equal(-1, "--".IndexOfFirstNot((Char[])null));

            Assert.Equal(-1, "".IndexOfFirstNot(charSet));
            Assert.Equal(1, "ABCD".IndexOfFirstNot(charSet));
            Assert.Equal(0, "abcd".IndexOfFirstNot(charSet));
            Assert.Equal(2, "AcEg".IndexOfFirstNot(charSet));
            Assert.Equal(4, "AcAcEg".IndexOfFirstNot(charSet));
            Assert.Equal(-1, "AcA".IndexOfFirstNot(charSet));
            Assert.Equal(0, "xyz".IndexOfFirstNot(charSet));

        }

        [Fact]
        public void TestSubstr()
        {
            Assert.Equal("bc", "abc".Substr(1));
            Assert.Equal("", "abc".Substr(10));
            Assert.Equal("abc", "abc".Substr(-1));

            Assert.Equal("bcd", "abcdef".Substr(1, 3));
            Assert.Equal("", "abcdef".Substr(10, 3));
            Assert.Equal("abc", "abcdef".Substr(-1, 3));

            Assert.Equal("cd", "abcdef".Substr(2, 2));
            Assert.Equal("cdef", "abcdef".Substr(2, 10));
            Assert.Equal("", "abcdef".Substr(2, -1));
        }
    }
}
