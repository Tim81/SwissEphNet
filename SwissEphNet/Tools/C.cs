using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SwissEphNet
{

    /// <summary>
    /// C tools
    /// </summary>
    public static partial class C
    {
        static readonly char[] fchars = "0123456789.+-Ee".ToCharArray();
        static readonly char[] ichars = "0123456789".ToCharArray();

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// C's strtod, which atof is defined in terms of, converts the *longest initial
        /// subsequence that has the expected form* and stops there. Narrowing to the first
        /// character outside fchars is not the same thing, because '.' is in that set: the
        /// whole of "2.10.03" survived the narrowing, TryParse then rejected it, and the
        /// result was 0 where C gives 2.10. That reached swe_set_astro_models, whose version
        /// branch (swephlib.c:4207) selects a different model bundle and a different tidal
        /// acceleration for 0 than for 2.10.
        ///
        /// The style must be Float, not Any. Any adds AllowTrailingSign, AllowParentheses,
        /// AllowThousands and AllowCurrencySymbol, none of which strtod accepts, and the first
        /// of those silently flips a sign. On "47.787931-1670.056*T" -- the shape swemplan.c's
        /// check_t_terms hands to atof for Vulcan's node in seorbel.txt -- fchars keeps
        /// "47.787931-1670.056", the full string fails to parse, and the back-off loop reaches
        /// "47.787931-", which NumberStyles.Any reads as a trailing minus and returns
        /// -47.787931. C's strtod stops at the '-' and returns +47.787931. Float allows a
        /// leading sign only, which is what strtod does.
        /// </remarks>
        public static double atof(string s) {
            s = (s ?? string.Empty).Trim();
            int i = s.IndexOfFirstNot(fchars);
            if (i >= 0)
                s = s.Substring(0, i);
            /* Longest parseable prefix, as strtod takes it. */
            for (int len = s.Length; len > 0; len--) {
                string candidate = s.Substring(0, len);
                double result;
                if (double.TryParse(candidate, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
                    return result;
                // strtod returns HUGE_VAL (infinity) on overflow, never a
                // smaller finite number. double.TryParse agrees on net8.0/
                // net10.0 -- it succeeds on the first iteration above,
                // returning double.PositiveInfinity/NegativeInfinity -- but
                // on netstandard2.0/net48 it returns false for an overflowing
                // literal instead. Left unguarded, this loop then backs off
                // one character at a time until it finds a shorter substring
                // that parses as a finite value ("1e999" -> "1e99" -> 1E+99),
                // trading an overflow for a plausible-looking but wrong
                // finite number. Catch that only on the first (full-length)
                // iteration, before any backing off has happened: if the
                // whole candidate is already syntactically a complete float
                // literal and still failed to parse, that failure can only be
                // overflow, not a malformed string like "2.10.03" that still
                // needs the backoff loop to find its longest valid prefix.
                if (len == s.Length && IsWellFormedFloatLiteral(candidate))
                    return candidate.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;
            }
            return 0;
        }

        static readonly System.Text.RegularExpressions.Regex wellFormedFloatLiteral =
            new System.Text.RegularExpressions.Regex(
                @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        static bool IsWellFormedFloatLiteral(string s) => wellFormedFloatLiteral.IsMatch(s);

        /// <summary>
        /// 
        /// </summary>
        public static int atoi(string s)
        {
            s = (s ?? string.Empty).Trim();
            // A leading sign is not one of the digit characters in ichars, so
            // IndexOfFirstNot(ichars) would stop at position 0 for "-5" or
            // "+5", leaving an empty digit string and making the whole call
            // return 0 instead of the signed value C's atoi returns. Strip a
            // single leading sign first, apply the digit-only truncation to
            // the remainder, then re-attach the sign for parsing.
            string sign = string.Empty;
            string digits = s;
            if (digits.Length > 0 && (digits[0] == '+' || digits[0] == '-'))
            {
                sign = digits.Substring(0, 1);
                digits = digits.Substring(1);
            }
            int i = digits.IndexOfFirstNot(ichars);
            if (i >= 0)
                digits = digits.Substring(0, i);
            int result = 0;
            // Integer, not Any, for the same reason atof above uses Float: Any would accept a
            // trailing sign, thousands separators and parentheses, none of which strtol takes.
            // Unlike atof this is not a live defect -- digits is already narrowed to ichars and
            // the sign is re-attached explicitly, so nothing Any allows can reach here -- but the
            // narrowing is the only thing preventing it, and that is one edit away from changing.
            if (int.TryParse(sign + digits, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double fmod(double numer, double denom)
        {
            return numer % denom;
        }

        public static void qsort<T>(CPointer<T> array, int n, Comparison<T> compare)
        {
            // The real C qsort(3) does nothing when nmemb (n) is 0, without ever
            // dereferencing base -- so a possibly-NULL base pointer is harmless in that case. This
            // used to call array.ToArray() unconditionally first, and CPointer<T>.ToArray() returns
            // null when the pointer has no backing array (Tools/CPointer.cs), so Take(n) on that
            // null threw instead of doing nothing. Reached from
            // SwissEphNet/CPort/Sweph.cs's load_all_fixed_stars (sweph.c:6392) by swe_fixstar2 on any
            // SwissEph with no OnLoadFile handler attached (the default), where no star data is ever
            // loaded and n is 0.
            if (n <= 0) return;
            var arr = array.ToArray();
            if (arr == null) return;
            var list = new List<T>(arr.Take(n));
            list.Sort(compare);
            for (int i = 0; i < list.Count; i++)
                array[i] = list[i];
        }

        class BComparer<TKey, TVal> : IComparer<TVal>
        {
            public BComparer(TKey key, Func<TKey, TVal, int> compare)
            {
                Key = key;
                Comparer = compare;
            }
            public int Compare(TVal x, TVal y)
            {
                bool xIsDefault = x == null || x.Equals(default(TVal));
                bool yIsDefault = y == null || y.Equals(default(TVal));
                if (yIsDefault && !xIsDefault)
                {
                    int c = Comparer(Key, x);
                    if (c != 0) return -c;
                    return c;
                }
                else if (xIsDefault && !yIsDefault)
                {
                    return Comparer(Key, y);
                }
                else
                    return -1;
            }
            public TKey Key { get; }
            public Func<TKey, TVal, int> Comparer { get; }
        }

        public static CPointer<TVal> bsearch<TKey, TVal>(TKey key, CPointer<TVal> array, int n, Func<TKey, TVal, int> compare)
        {
            // The real C bsearch(3) returns NULL immediately when nmemb (n) is 0, without ever
            // dereferencing base -- so a possibly-NULL base pointer is harmless in that case. This
            // used to call array.ToArray() unconditionally first, and CPointer<T>.ToArray() returns
            // null when the pointer has no backing array (Tools/CPointer.cs), so Take(n) on that
            // null threw instead of returning "not found". Reached from
            // SwissEphNet/CPort/Sweph.cs's search_star_in_list (sweph.c:6735) by swe_fixstar2 on any
            // SwissEph with no OnLoadFile handler attached (the default), where no star data is ever
            // loaded and n is 0.
            if (n <= 0) return new CPointer<TVal>();
            var arr = array.ToArray();
            if (arr == null) return new CPointer<TVal>();
            var list = new List<TVal>(arr.Take(n));
            var idx = list.BinarySearch(default(TVal), new BComparer<TKey, TVal>(key, compare));
            return idx >= 0 ? array + idx : new CPointer<TVal>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int strlen(string s) => s?.Length ?? 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string strcpy(out string a, string b) => a = b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void strncpy(out string a, string b, int n)
            => a = b != null ? b.Substring(0, Math.Min(n, b.Length)) : null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void strcat(ref string a, string b) => a = string.Concat(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void strncat(ref string a, string b, int n) {
            n = Math.Min(n, b?.Length ?? 0);
            if (n > 0)
                a = string.Concat(a, b.Substr(0, n));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int strcmp(string a, string b)
        {
            return string.CompareOrdinal(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int strncmp(string a, string b, int n)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return string.CompareOrdinal(a, b);
            return string.CompareOrdinal(a.Substring(0, Math.Min(a.Length, n)), b.Substring(0, Math.Min(b.Length, n)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int strstr(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return -1;
            return a.IndexOf(b, StringComparison.Ordinal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int strchr(string s, char c)
        {
            if (string.IsNullOrEmpty(s))
                return -1;
            // string.IndexOf(char) (no StringComparison) was already ordinal
            // here -- that overload is documented as performing an ordinal
            // comparison unconditionally, unlike IndexOf(string)/Compare/
            // StartsWith, so strchr was never actually culture-sensitive.
            // This is a manual loop instead of s.IndexOf(c) purely because
            // string.IndexOf(char, StringComparison), the explicit overload,
            // is not part of the netstandard2.0 API surface (one of this
            // project's three target frameworks) -- a char-to-char ==
            // comparison is inherently ordinal (it compares UTF-16 code unit
            // values, not linguistic weight) either way, so this is exactly
            // the same semantics as before, on every TFM.
            for (int i = 0; i < s.Length; i++)
                if (s[i] == c) return i;
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void rewind(CFile file)
        {
            if (file != null)
                file.Seek(0, System.IO.SeekOrigin.Begin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void fclose(CFile file)
            => file?.Dispose();

    }

}
