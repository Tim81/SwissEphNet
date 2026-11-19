using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// The C tests bytes with isdigit/isalnum, which accept only ASCII 0-9 / A-Z a-z in the "C"
    /// locale. Char.IsDigit and Char.IsLetterOrDigit accept every Unicode digit and letter, so a
    /// star name starting with a fullwidth digit was taken for a catalogue number
    /// (sweph.c:6696, 7571, 7990, 8080), and a heliacal object name containing a non-ASCII
    /// letter was not cut there (swehel.c:1819, strcpy_VBsafe).
    /// </summary>
    public class AsciiCtypeTest
    {
        private static DelegateFileProvider Provider() => new DelegateFileProvider(path =>
        {
            string fn = ResourceFileHelpers.GetPortableFileName(path);
            return fn.Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase)
                ? ResourceFileHelpers.OpenResourceFile("sefstars.txt")
                : null;
        });

        // U+FF11 FULLWIDTH DIGIT ONE: first byte 0xEF in UTF-8, not an isdigit() byte.
        private const string FullwidthOne = "１";

        [Fact]
        public void Test_swe_fixstar2_FullwidthDigitIsNotACatalogueNumber()
        {
            using var swe = new SwissEph();
            swe.FileProvider = Provider();
            double[] xx = new double[6];
            string serr = null;
            // ",alTau" after a comma is a Bayer designation; the C never reads the leading
            // character as a digit, so it searches by name and finds Aldebaran.
            string star = FullwidthOne + ",alTau";
            int rc = swe.swe_fixstar2(ref star, 2451545.0, SwissEph.SEFLG_MOSEPH, xx, ref serr);
            Assert.True(rc >= 0, serr);
            Assert.StartsWith("Aldebaran", star, StringComparison.Ordinal);
        }

        [Fact]
        public void Test_HeliacalObjectNameWithNonAsciiLetter_IsCutThereLikeTheC()
        {
            // strcpy_VBsafe (swehel.c:1819) stops at the first byte that is not isalnum, ' ',
            // '-' or ','. The accent's UTF-8 lead byte is none of these, so "regulusé" is "regulus".
            int rcPlain, rcAccented;
            double[] plain = new double[50], accented = new double[50];
            string serrPlain = null, serrAccented = null;
            using (var swe = new SwissEph())
            {
                swe.FileProvider = Provider();
                double[] geopos = { 8.55, 47.38, 400 };
                double[] datm = { 1013.25, 15, 40, 0 };
                double[] dobs = new double[6];
                double tjd = swe.swe_julday(2020, 1, 1, 0.0, SwissEph.SE_GREG_CAL);
                int flags = SwissEph.SE_HELFLAG_AVKIND_VR | SwissEph.SEFLG_MOSEPH;
                rcPlain = swe.swe_heliacal_ut(tjd, geopos, datm, dobs, "regulus", SwissEph.SE_HELIACAL_RISING, flags, plain, ref serrPlain);
                rcAccented = swe.swe_heliacal_ut(tjd, geopos, datm, dobs, "regulus" + "é", SwissEph.SE_HELIACAL_RISING, flags, accented, ref serrAccented);
            }
            Assert.True(rcPlain >= 0, serrPlain);
            Assert.Equal(rcPlain, rcAccented);
            Assert.Equal(plain[0], accented[0]);
        }
    }
}
