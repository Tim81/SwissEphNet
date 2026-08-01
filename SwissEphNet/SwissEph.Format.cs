using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SwissEphNet
{
    /// <summary>
    /// Some value formats
    /// </summary>
    partial class SwissEph
    {

        public const int BIT_ROUND_SEC = 1;
        public const int BIT_ROUND_MIN = 2;
        public const int BIT_ZODIAC = 4;
        public const int BIT_LZEROES = 8;

        static string ZodiacSymbols = "♈♉♊♋♌♍♎♏♐♑♒♓";
        static string[] ZodiacShortNames = new String[]{
            "ar", "ta", "ge", "cn", "le", "vi", 
            "li", "sc", "sa", "cp", "aq", "pi"
        };
        static string[] ZodiacNames = new String[]{
            "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo", 
            "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
        };

        /// <summary>
        /// Format to Degrees Minutes Seconds like dms() function in swewin.exe and swetest.exe.
        /// </summary>
        /// <remarks>
        /// This is not a ported libswe function -- <c>dms()</c> is <c>static</c> inside
        /// swetest.c and appears nowhere in swephexp.h -- so there is no library
        /// transliteration to delegate to. It is modeled directly on swetest.c:2642-2731
        /// (the already-correct <c>dms()</c> transliteration in Programs/SweTest/Program.cs
        /// is the same source, and its comments cover the two divergences the C itself has:
        /// the zodiac-field sign loss and the leading-space fix for triple-digit degrees).
        /// </remarks>
        public String DMS(double value, int iFlag, bool outputExtraPrecision = false) {
            if (double.IsNaN(value)) return "nan";
            // Infinity for the same reason as NaN, and in the same printf spelling. Without
            // this the (int) casts below saturate at Int32.MinValue/MaxValue and the method
            // returns "2147483647{degree}2147483647'2147483647.2147483647", which reads as a
            // real measurement rather than as the absence of one. The C cannot be copied here:
            // swetest.c's dms() casts a double to int too, and doing that with an infinity is
            // undefined behaviour in C rather than a defined result worth matching.
            if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
            int izod = 0;
            Int32 k, kdeg, kmin, ksec;
            string c = SwissEph.ODEGREE_STRING;
            string s1 = string.Empty;
            string s = string.Empty;
            int sgn;
            // swetest.c:2657. This port's DMS has no BIT_ALLOW_361 bit, so it always
            // clamps, matching every swetest.c caller that does not set that bit. Besides
            // matching the C, this also keeps izod (below) out of range and keeps `value`
            // itself from reaching a magnitude where the sign-insertion code further down
            // could index before the start of the string.
            if (value >= 360)
                value = 0;
            if ((iFlag & SwissEph.SEFLG_EQUATORIAL) != 0)
                c = "h";
            if (value < 0) {
                value = -value;
                sgn = -1;
            } else
                sgn = 1;
            // swetest.c:2668-2680: if/else-if/else, not two independent ifs -- ROUND_SEC only
            // applies when ROUND_MIN is not set, and the rounding nudge below only applies
            // when neither is set (it used to not exist here at all; its absence meant a
            // value that should have carried into the next field over, e.g. 0.99999999999
            // degrees, rounded to 0 seconds without ever carrying into a whole degree).
            if ((iFlag & BIT_ROUND_MIN) != 0) {
                value = SwephLib.swe_degnorm(value + 0.5 / 60);
            } else if ((iFlag & BIT_ROUND_SEC) != 0) {
                value = SwephLib.swe_degnorm(value + 0.5 / 3600);
            } else {
                /* rounding 0.9999999999 to 1 */
                if (outputExtraPrecision)
                    value += (value < 0 ? -1 : 1) * 0.000000005 / 3600.0;
                else
                    value += (value < 0 ? -1 : 1) * 0.00005 / 3600.0;
            }
            if ((iFlag & BIT_ZODIAC) != 0) {
                izod = (int)(value / 30);
                // swetest.c:2683. Reachable once the >= 360 clamp above is bypassed by a
                // caller-supplied value of exactly 360 minus a rounding nudge that carries
                // it back up to 30 * 12; without this, ZodiacShortNames[izod] below throws.
                if (izod == 12) izod = 0;
                value = (value % 30.0);
                kdeg = (Int32)value;
                // swetest.c:2686: no leading spaces here (the C's sprintf format is
                // "%2d %s ", not "  %2d %s ").
                s = C.sprintf("%2d %s ", kdeg, ZodiacShortNames[izod]);
            } else {
                kdeg = (Int32)value;
                s = C.sprintf(" %3d%s", kdeg, c);
            }
            value -= kdeg;
            value *= 60;
            kmin = (Int32)value;
            if ((iFlag & BIT_ZODIAC) != 0 && (iFlag & BIT_ROUND_MIN) != 0) {
                s1 = C.sprintf("%2d", kmin);
            } else {
                s1 = C.sprintf("%2d'", kmin);
            }
            s += s1;
            if ((iFlag & BIT_ROUND_MIN) != 0)
                goto return_dms;
            value -= kmin;
            value *= 60;
            ksec = (Int32)value;
            if ((iFlag & BIT_ROUND_SEC) != 0) {
                s1 = C.sprintf("%2d\"", ksec);
            } else {
                s1 = C.sprintf("%2d", ksec);
            }
            s += s1;
            if ((iFlag & BIT_ROUND_SEC) != 0)
                goto return_dms;
            value -= ksec;
            // swetest.c:2714-2719. No "+ 0.5" in the C on either branch -- rounding is
            // already handled by the nudge added to `value` above, before the degree/
            // minute/second split; adding it again here could not carry into a higher
            // field anyway (k is truncated straight into a fixed-width %0Nd). The extra-
            // precision branch is 8 digits, not 5.
            if (outputExtraPrecision) {
                k = (Int32)(value * 100000000);
                s1 = C.sprintf(".%08d", k);
            } else {
                k = (Int32)(value * 10000);
                s1 = C.sprintf(".%04d", k);
            }
            s += s1;
        return_dms:
            int spi;
            if (sgn < 0) {
                spi = s.IndexOfAny("0123456789".ToCharArray());
                // swetest.c:2723-2725: sp = strpbrk(s, "0123456789"); *(sp - 1) = '-';
                // overwrites the character immediately before the first digit. Under
                // BIT_ZODIAC, once kdeg reaches double digits "%2d" fills the field and the
                // first digit lands at index 0, so the C writes one byte before its own
                // buffer -- undefined behavior that loses the minus sign. Reproducing that
                // would print a positive number for a negative one, so prepend the sign
                // instead of splicing at index -1 when there is no character before the
                // digit to overwrite (see Programs/SweTest/Program.cs's dms(), which
                // resolves the same divergence the same way).
                if (spi == 0)
                    s = "-" + s;
                else
                    s = String.Concat(s.Substring(0, spi - 1), '-', s.Substring(spi));
            }
            if ((iFlag & BIT_LZEROES) != 0) {
                s = s.Substring(0, 2) + s.Substring(2).Replace(' ', '0');
            }
            return (s);
        }

        /// <summary>
        /// Format to Hour Minutes Seconds like hms() function in swewin.exe and swetest.exe.
        /// </summary>
        /// <remarks>
        /// Not a ported libswe function either -- see <see cref="DMS"/>'s remarks. Modeled on
        /// swetest.c:3925-3939, the same source Programs/SweTest/Program.cs's hms() already
        /// transliterates faithfully (including the guard on its second splice, cited there).
        /// </remarks>
        public String HMS(double value, int iFlag, bool outputExtraPrecision = false) {
            // swetest.c:3929: round to 0.1 sec before formatting. This was missing entirely;
            // without it HMS truncated the tenths-of-a-second digit instead of rounding it.
            value += 0.5 / 36000.0;
            var s = DMS(value, iFlag, outputExtraPrecision);
            // swetest.c:3926: `char *c = ODEGREE_STRING;` is fixed here regardless of iflag --
            // even when DMS used "h" instead (SEFLG_EQUATORIAL), hms() still searches for the
            // degree marker, not "h", so that combination falls through unconverted below,
            // matching the C exactly.
            var spi = s.IndexOf(SwissEph.ODEGREE_STRING, StringComparison.Ordinal);
            if (spi >= 0) {
                // swetest.c:3932-3935: *sp = ':'; strcpy(s2, sp + strlen(ODEGREE_STRING));
                // strcpy(sp + 1, s2); -- collapses the (possibly multi-byte) degree marker
                // down to a single ':'.
                s = String.Concat(s.Substring(0, spi), ":", s.Substring(spi + 1));
                var s2 = s.Substring(spi + SwissEph.ODEGREE_STRING.Length);
                s = String.Concat(s.Substring(0, spi + 1), s2);
                // swetest.c:3936: *(sp + 3) = ':'; writes a single byte into the static
                // AS_MAXCH buffer regardless of length. Substring(spi + 4) throws where C's
                // single-byte write would not, on a BIT_ROUND_MIN result ending at spi + 2
                // (s.Length == spi + 3); guarded the same way as the SweTest port.
                if (s.Length > spi + 4)
                    s = String.Concat(s.Substring(0, spi + 3), ":", s.Substring(spi + 4));
                else
                    s = String.Concat(s.Substring(0, spi + 3), ":");
                // swetest.c:3937: *(sp + 8) = '\0'; truncates the buffer after the seconds
                // field. The length guard is needed because the C writes into a static
                // AS_MAXCH buffer regardless of length, while Substring would throw here if
                // s were ever shorter than spi + 8.
                if (s.Length > spi + 8) s = s.Substring(0, spi + 8);
            }
            return s;
        }

        /// <summary>
        /// Format value to degrees/minutes/seconds
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>d : Degrees</item>
        /// <item>dd : Degrees leading space</item>
        /// <item>ddd : Degrees leading space</item>
        /// <item>dddd : Degrees leading space</item>
        /// <item>a : Absolute Degrees</item>
        /// <item>aa : Absolute Degrees leading space</item>
        /// <item>aaa : Absolute Degrees leading space</item>
        /// <item>n : Zodiac number</item>
        /// <item>nn : Zodiac number leading space</item>
        /// <item>g : Zodiac degrees (degrees % 30)</item>
        /// <item>gg : Zodiac degrees leading space</item>
        /// <item>m : minutes</item>
        /// <item>mm : minutes leading space</item>
        /// <item>s : seconds</item>
        /// <item>ss : seconds leading space</item>
        /// <item>p : seconds decimal part to 0.0 format</item>
        /// <item>pp : seconds decimal part to 0.00 format</item>
        /// <item>ppp : seconds decimal part to 0.000 format</item>
        /// <item>pppp : seconds decimal part to 0.0000 format</item>
        /// <item>ppppp : seconds decimal part to 0.00000 format</item>
        /// <item>z : Zodiac symbol</item>
        /// <item>zz : Zodiac short name</item>
        /// <item>zzz : Zodiac name</item>
        /// <item>- : Minus sign if value is negative</item>
        /// <item>+ : Minus sign if value is negative or space if positive</item>
        /// </list>
        /// <para>
        /// Standard formats are:
        /// <list type="bullet">
        /// <item>D1 : dddd°mm'ss.pppp</item>
        /// <item>D2 : dddd°mm'ss"</item>
        /// <item>Z1 : gg zz mm'ss.pppp</item>
        /// <item>Z2 : gg zz mm'ss"</item>
        /// </list>
        /// </para>
        /// <para>
        /// For d*, a*, n*, g*, m* and s*, the same uppercase format exists for leading 0 instead of space
        /// </para>
        /// </remarks>
        public static String FormatToDegreeMinuteSecond(double value, String format = null) {
            if (double.IsNaN(value)) return "nan";
            // See DMS above: same saturation, same spelling. Measured before this guard,
            // PositiveInfinity formatted as "2147483647{degree}2147483647'2147483647.2147483647".
            if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
            if (String.IsNullOrEmpty(format)) format = "dddd°mm'ss.pppp";
            switch (format) {
                case "D1": format = "dddd°mm'ss.pppp"; break;
                case "D2": format = "dddd°mm'ss\""; break;
                case "Z1": format = "gg zz mm'ss.pppp"; break;
                case "Z2": format = "gg zz mm'ss\""; break;
            }
            // Elements calculation
            var sgn = Math.Sign(value);
            double avalue = Math.Abs(value);
            int deg = (int)value;
            int adeg = (int)avalue;
            int znum = (int)((avalue % 360.0) / 30);
            int zdeg = (int)((avalue % 360.0) % 30.0);
            avalue -= adeg; avalue *= 60.0;
            int min = (int)avalue;
            avalue -= min; 
            double dsec = (avalue * 60.0);
            StringBuilder result = new StringBuilder();
            for (int i = 0, fmtLen = format.Length; i < fmtLen; i++) {
                char c = format[i];
                int l = 1;
                // Search length of segment
                char[] cf = null;
                switch (c) {
                    case 'd':
                    case 'D': cf = new char[] { 'd', 'D' }; break;
                    case 'a':
                    case 'A': cf = new char[] { 'a', 'A' }; break;
                    case 'n':
                    case 'N': cf = new char[] { 'n', 'N' }; break;
                    case 'g':
                    case 'G': cf = new char[] { 'g', 'G' }; break;
                    case 'm':
                    case 'M': cf = new char[] { 'm', 'M' }; break;
                    case 's':
                    case 'S': cf = new char[] { 's', 'S' }; break;
                    case 'p':
                    case 'P': cf = new char[] { 'p', 'P' }; break;
                    case 'z':
                    case 'Z': cf = new char[] { 'z', 'Z' }; break;
                }
                if (cf != null) {
                    while (i + 1 < fmtLen && (format[i + 1] == cf[0] || format[i + 1] == cf[1])) { i++; l++; }
                }
                // Format
                switch (c) {
                    case 'd': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), deg); break;
                    case 'D': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", sgn < 0 ? l - 1 : l), deg); break;
                    case 'a': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), adeg); break;
                    case 'A': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), adeg); break;
                    case 'n': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), znum); break;
                    case 'N': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), znum); break;
                    case 'g': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), zdeg); break;
                    case 'G': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), zdeg); break;
                    case 'm': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), min); break;
                    case 'M': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), min); break;
                    case 's': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0,{0}}}", l), (int)Math.Round(dsec, l)); break;
                    case 'S': result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), (int)Math.Round(dsec, l)); break;
                    case 'p':
                    case 'P':
                        var t = Math.Round(dsec, l);
                        var prec = t - (int)t;
                        prec = Math.Round(prec * Math.Pow(10, l));
                        result.AppendFormat(CultureInfo.InvariantCulture, String.Format(CultureInfo.InvariantCulture, "{{0:D{0}}}", l), (int)prec);
                        break;
                    case 'z':
                    case 'Z':
                        switch (l) {
                            case 1:
                                result.Append(ZodiacSymbols[znum % 12]);
                                break;
                            case 2:
                                result.Append(ZodiacShortNames[znum % 12]);
                                break;
                            default:
                                result.Append(ZodiacNames[znum % 12]);
                                break;
                        }
                        break;
                    case '-':
                        if (sgn < 0) result.Append('-');
                        break;
                    case '+':
                        result.Append((sgn < 0) ? '-' : ' ');
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }
            return result.ToString();
        }

    }
}
