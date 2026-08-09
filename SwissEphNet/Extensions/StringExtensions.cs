using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SwissEphNet
{

    /// <summary>
    /// String extensions methods.
    /// </summary>
    /// <remarks>
    /// Internal, not public: <c>Contains(this string, char)</c> is the only overload
    /// the BCL is missing on netstandard2.0/net4x, and putting it in scope for every
    /// consumer that writes <c>using SwissEphNet;</c> has two effects neither consumer
    /// wants. First, it changes null-guard semantics per TFM: on net8.0/net10.0 the BCL's
    /// own instance <c>Contains(char)</c> always wins (instance methods beat extension
    /// methods), so <c>((string)null).Contains('x')</c> throws NullReferenceException;
    /// on netstandard2.0/net4x, where no such instance method exists, this extension
    /// wins instead and silently returns false for a null receiver -- same source, same
    /// package, opposite behavior depending on which TFM resolved. Second, it is a hard
    /// compile break for any consumer who already has their own <c>Contains(this string,
    /// char)</c> helper (the ordinary way to get that method on .NET Framework): the
    /// call becomes ambiguous (CS0121) the moment this package is referenced. Confirmed
    /// both effects by building/running a consumer against both shipped assets before
    /// making this internal. <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>
    /// grants the test assemblies and SweTest access via SwissEphNet.csproj.
    /// </remarks>
    internal static class StringExtensions
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
            // s.IndexOf(c) is a distinct method name (not Contains), so it
            // carries none of that recursion risk, exists on every targeted
            // TFM, and needs no c.ToString() allocation per call. The
            // IndexOf(char, StringComparison) overload CA1307 suggests does
            // not exist on netstandard2.0 either -- same false positive as
            // the old Contains(string) call had.
#pragma warning disable CA1307
            return s.IndexOf(c) >= 0;
#pragma warning restore CA1307
        }

        /// <summary>
        /// String.Contains() for Char
        /// </summary>
        public static bool Contains(this String s, Char[] charSet)
        {
            // Must be IsNullOrEmpty, not IsNullOrWhiteSpace: a whitespace-only
            // string is a valid haystack (e.g. " ".Contains(new[]{' '}) is true),
            // and IsNullOrWhiteSpace was rejecting it before the search even ran.
            if (charSet == null || String.IsNullOrEmpty(s)) return false;
            foreach (var c in charSet)
            {
                // Ordinal is already correct char-search semantics here; the
                // StringComparison overload CA1307 suggests (present on
                // net8.0/net10.0, absent on netstandard2.0) would add no
                // behavior change, only noise.
#pragma warning disable CA1307
                if (s.Contains(c)) return true;
#pragma warning restore CA1307
            }
            return false;
        }

        /// <summary>
        /// Search index of first char that is not in chars
        /// </summary>
        public static int IndexOfFirstNot(this String s, params char[] chars)
        {
            // Must not early-return on chars.Length == 0: an empty set excludes
            // nothing, so the first character of a non-empty s already qualifies
            // and the answer is 0, not -1. Only a null/empty s or a null chars
            // array have no valid index to report.
            if (String.IsNullOrEmpty(s) || chars == null) return -1;
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
        {
            if (s == null) return null;
            // The length clamp must use the already-clamped start index, not the
            // raw (possibly negative) startIndex: with the raw value, a call like
            // Substr(-2, 5) computed s.Length - (-2), overshooting the string and
            // throwing ArgumentOutOfRangeException from Substring despite this
            // method's own "check limits" doc.
            var start = Math.Max(0, startIndex);
            if (start >= s.Length) return string.Empty;
            return s.Substring(start, Math.Max(0, Math.Min(length, s.Length - start)));
        }
    }

}
