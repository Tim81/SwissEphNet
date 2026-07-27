using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SwissEphNet
{

    /// <summary>
    /// String extensions methods
    /// </summary>
    public static class StringExtensions
    {

        /// <summary>
        /// String.Contains() for Char
        /// </summary>
        public static bool Contains(this String s, Char c)
        {
            if (String.IsNullOrEmpty(s)) return false;
            // MUST NOT be s.Contains(c): netstandard2.0's System.String has
            // exactly one Contains overload, taking a string -- there is no
            // instance Contains(char) there at all, not just no
            // Contains(char, StringComparison) overload. On that TFM,
            // s.Contains(c) inside THIS extension method binds to this very
            // extension method itself (extension resolution only kicks in
            // when no instance method matches, and here none does), which is
            // unbounded recursion -- StackOverflowException, uncatchable,
            // terminates the process. Confirmed with a probe project
            // targeting netstandard2.0;net10.0 with no extension method in
            // scope: net10.0 compiles s.Contains(c) as a plain char argument,
            // netstandard2.0 fails with CS1503 (char does not convert to
            // string), which is exactly the signature that made the compiler
            // fall back to this extension instead once it type-checks.
            // s.Contains(c.ToString()) binds to the one Contains overload
            // that exists on every TFM and is already ordinal
            // (culture-insensitive) per its documented behavior, so the
            // CA1307 suggestion to add StringComparison explicitly is a false
            // positive here (that overload of Contains is not available on
            // netstandard2.0 either).
#pragma warning disable CA1307
            return s.Contains(c.ToString());
#pragma warning restore CA1307
        }

        /// <summary>
        /// String.Contains() for Char
        /// </summary>
        public static bool Contains(this String s, Char[] charSet)
        {
            if (charSet == null || String.IsNullOrWhiteSpace(s)) return false;
            foreach (var c in charSet)
            {
                // See the single-char Contains(Char) overload above: this
                // must stay on the string overload, not s.Contains(c), or it
                // recurses unboundedly on netstandard2.0.
#pragma warning disable CA1307
                if (s.Contains(c.ToString())) return true;
#pragma warning restore CA1307
            }
            return false;
        }

        /// <summary>
        /// Search index of first char that is not in chars
        /// </summary>
        public static int IndexOfFirstNot(this String s, params char[] chars)
        {
            if (String.IsNullOrEmpty(s) || chars == null || chars.Length == 0) return -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (!chars.Contains(s[i])) return i;
            }
            return -1;
        }

        /// <summary>
        /// Substring with check limits
        /// </summary>
        public static string Substr(this string s, int startIndex)
            => s == null ? null
            : startIndex >= s.Length ? string.Empty
            : s.Substring(Math.Max(0, startIndex))
            ;

        /// <summary>
        /// Substring with check limits
        /// </summary>
        public static string Substr(this string s, int startIndex, int length)
            => s == null ? null
            : startIndex >= s.Length ? string.Empty
            : s.Substring(Math.Max(0, startIndex), Math.Max(0, Math.Min(length, s.Length - startIndex)))
            ;
    }

}
