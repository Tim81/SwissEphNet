using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// The " \ntrying Swiss Eph; " suffix is appended to serr only when strlen(serr) + 30 &lt; AS_MAXCH
    /// (sweph.c:709-710, 1708-1709, 1732-1733, 1836-1837 at e308765d: "if (serr != NULL &amp;&amp; strlen(serr) + 30
    /// &lt; AS_MAXCH) strcat(serr, ...)"). The port appended it unconditionally.
    ///
    /// Reference strings: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD):
    ///   swe_set_ephe_path(P); swe_set_jpl_file("missing.eph");
    ///   char serr[256] = "";  rc = swe_calc(2451546, ipl, SEFLG_JPLEPH, x, serr);   // twice; the second one is checked
    /// with P = the submodule's ephe directory: serr
    ///   "SwissEph file 'missing.eph' not found in PATH '&lt;P&gt;\' \ntrying Swiss Eph; "
    /// (SE_MOON, SE_SUN, SE_MERCURY with SEFLG_JPLEPH, rc 2; SE_MERCURY with SEFLG_JPLEPH|SEFLG_HELCTR, rc 1546;
    /// swe_fixstar2_ut("Aldebaran", 2451546, SEFLG_JPLEPH, ...), rc 1, the same serr, issued by main_planet_bary),
    /// and with P = "C:\" + 150 x 'q' + ";" + the ephe directory: serr is the same file-not-found text, cut at 255
    /// characters, with no suffix.
    /// </summary>
    public class TryingSwissEphGuardTest
    {
        private const string Suffix = " \ntrying Swiss Eph; ";

        private static SwissEph Open(bool longPath)
        {
            var swe = RepoEphe.Open();
            if (longPath)
                swe.swe_set_ephe_path("C:\\" + new string('q', 150) + ";" + RepoEphe.Dir);
            swe.swe_set_jpl_file("missing.eph");
            return swe;
        }

        private static string SecondCalc(SwissEph swe, int ipl, int flags)
        {
            var x = new double[6];
            string serr = null;
            swe.swe_calc(2451546, ipl, flags, x, ref serr);
            serr = null;
            swe.swe_calc(2451546, ipl, flags, x, ref serr);
            return serr;
        }

        private static string SecondFixstar(SwissEph swe)
        {
            var x = new double[6];
            string serr = null;
            string star = "Aldebaran";
            swe.swe_fixstar2_ut(ref star, 2451546, SwissEph.SEFLG_JPLEPH, x, ref serr);
            serr = null;
            star = "Aldebaran";
            swe.swe_fixstar2_ut(ref star, 2451546, SwissEph.SEFLG_JPLEPH, x, ref serr);
            return serr;
        }

        public static TheoryData<int, int> Calls => new TheoryData<int, int>
        {
            { SwissEph.SE_MOON, SwissEph.SEFLG_JPLEPH },                                   // sweph.c:709
            { SwissEph.SE_MERCURY, SwissEph.SEFLG_JPLEPH },                                // sweph.c:1708
            { SwissEph.SE_MERCURY, SwissEph.SEFLG_JPLEPH | SwissEph.SEFLG_HELCTR },        // sweph.c:1708
        };

        [EpheTheory]
        [MemberData(nameof(Calls))]
        public void ShortPath_AppendsTheSuffix(int ipl, int flags)
        {
            using var swe = Open(false);

            string serr = SecondCalc(swe, ipl, flags);

            Assert.StartsWith("SwissEph file 'missing.eph' not found in PATH '", serr, StringComparison.Ordinal);
            Assert.EndsWith(Suffix, serr, StringComparison.Ordinal);
        }

        [EpheTheory]
        [MemberData(nameof(Calls))]
        public void LongPath_LeavesTheMessageWithoutTheSuffix(int ipl, int flags)
        {
            using var swe = Open(true);

            string serr = SecondCalc(swe, ipl, flags);

            Assert.StartsWith("SwissEph file 'missing.eph' not found in PATH 'C:\\qqqq", serr, StringComparison.Ordinal);
            Assert.Equal(255, serr.Length);
            Assert.DoesNotContain("trying Swiss Eph", serr, StringComparison.Ordinal);
        }

        [EpheFact]
        public void Sun_ShortAndLongPath()
        {
            using (var swe = Open(false))
                Assert.EndsWith(Suffix, SecondCalc(swe, SwissEph.SE_SUN, SwissEph.SEFLG_JPLEPH), StringComparison.Ordinal);
            using (var swe = Open(true))
            {
                string serr = SecondCalc(swe, SwissEph.SE_SUN, SwissEph.SEFLG_JPLEPH);
                Assert.Equal(255, serr.Length);
                Assert.DoesNotContain("trying Swiss Eph", serr, StringComparison.Ordinal);
            }
        }

        [EpheFact]
        public void Fixstar_ShortAndLongPath()
        {
            using (var swe = Open(false))
                Assert.EndsWith(Suffix, SecondFixstar(swe), StringComparison.Ordinal);
            using (var swe = Open(true))
            {
                string serr = SecondFixstar(swe);
                Assert.Equal(255, serr.Length);
                Assert.DoesNotContain("trying Swiss Eph", serr, StringComparison.Ordinal);
            }
        }
    }
}
