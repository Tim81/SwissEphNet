using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_fixstar() reports an unknown star with a short form first and the long form only when
    /// strlen(serr) + strlen(star) &lt; AS_MAXCH (sweph.c:7888-7891 at e308765d):
    ///   sprintf(serr, "star  not found");
    ///   if (strlen(serr) + strlen(star) &lt; AS_MAXCH) sprintf(serr, "star %s not found", star);
    /// The port formatted the long form unconditionally.
    ///
    /// Reference strings: C probe against external/.c-reference/build-2.10.03/libswe-2.10.03.lib (built from
    /// e308765d = v2.10.04; MSVC /O2 /fp:precise /MD):
    ///   swe_set_ephe_path(&lt;submodule ephe dir&gt;); star = n x 'z';
    ///   rc = swe_fixstar(star, 2451545.0, SEFLG_MOSEPH, x, serr);
    /// n = 5   : rc -1, 20 characters, "star zzzzz not found"
    /// n = 240 : rc -1, 255 characters, "star " + 240 x 'z' + " not found"
    /// n = 241 : rc -1, 15 characters, "star  not found"
    /// n = 245 : rc -1, 15 characters, "star  not found"
    /// </summary>
    public class SerrLengthFallbackTest
    {
        private static string Fixstar(int nameLength, out int rc)
        {
            using var swe = RepoEphe.Open();
            var x = new double[6];
            string serr = null;
            string star = new string('z', nameLength);
            rc = swe.swe_fixstar(ref star, 2451545.0, SwissEph.SEFLG_MOSEPH, x, ref serr);
            return serr;
        }

        [EpheFact]
        public void ShortName_KeepsTheLongForm()
        {
            string serr = Fixstar(5, out int rc);

            Assert.Equal(SwissEph.ERR, rc);
            Assert.Equal("star zzzzz not found", serr);
        }

        [EpheFact]
        public void LongestNameThatFits_KeepsTheLongForm()
        {
            string serr = Fixstar(240, out int rc);

            Assert.Equal(SwissEph.ERR, rc);
            Assert.Equal(255, serr.Length);
            Assert.Equal("star " + new string('z', 240) + " not found", serr);
        }

        [EpheTheory]
        [InlineData(241)]
        [InlineData(245)]
        public void TooLongName_FallsBackToTheShortForm(int nameLength)
        {
            string serr = Fixstar(nameLength, out int rc);

            Assert.Equal(SwissEph.ERR, rc);
            Assert.Equal("star  not found", serr);
        }
    }
}
