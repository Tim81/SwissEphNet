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
        public static double atof(string s) {
            s = (s ?? string.Empty).Trim();
            int i = s.IndexOfFirstNot(fchars);
            if (i >= 0)
                s = s.Substring(0, i);
            double result = 0;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return 0;
        }

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
            if (int.TryParse(sign + digits, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result))
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
            var list = new List<T>(array.ToArray().Take(n));
            list.Sort(compare);
            for (int i = 0; i < list.Count; i++)
                array[i] = list[i];
        }

        class bcomparer<TKey, TVal> : IComparer<TVal>
        {
            public bcomparer(TKey key, Func<TKey, TVal, int> compare)
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
            var list = new List<TVal>(array.ToArray().Take(n));
            var idx = list.BinarySearch(default(TVal), new bcomparer<TKey, TVal>(key, compare));
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
            // string.IndexOf(char, StringComparison) is not part of the
            // netstandard2.0 API surface (one of this project's three target
            // frameworks), so a plain value comparison loop is used instead
            // of that overload. A char-to-char == comparison is inherently
            // ordinal (it compares UTF-16 code unit values, not linguistic
            // weight), so this is already exactly the semantics C's strchr
            // needs, on every TFM.
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
