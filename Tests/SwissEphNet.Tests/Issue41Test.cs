using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Issue #41 : https://github.com/ygrenier/SwissEphNet/issues/41
    /// </summary>
    public class Issue41Test
    {
        public static IEnumerable<object[]> TestDataFixstar()
        {
            yield return new object[] { "1", 4, "Aldebaran,alTau", null };
            yield return new object[] { "5", 4, "Regulus,alLeo", null };
            yield return new object[] { "10", 4, "Gal. Center,SgrA*", null };
            yield return new object[] { "25", 4, "Mirach,beAnd", null };
            yield return new object[] { "1000", 4, "Samakah,bePsc", null };
            yield return new object[] { "10000", -1, "", "star 10000 not found" };
            yield return new object[] { "aldeb", 4, "Aldebaran,alTau", null };
            yield return new object[] { ",alTau", 4, "Aldebaran,alTau", null };
            yield return new object[] { "aldeb%", -1, "", "star aldeb% not found" };
            yield return new object[] { "Spica", 4, "Spica,alVir", null };
            yield return new object[] { "alVir", -1, "", "star alVir not found" };
            yield return new object[] { ",alVir", 4, "Spica,alVir", null };
            // ,alCMi (Procyon) -- see TestBayerSearchFindsStarInvertedUnderCultureSensitiveOrder
            // below for why this specific key matters.
            yield return new object[] { ",alCMi", 4, "Procyon,alCMi", null };
        }

        [Theory]
        [MemberData(nameof(TestDataFixstar))]
        public void TestFixstar(string search, int eres, string estar, string error)
        {
            int day = 16, month = 8, year = 1974;
            double time = 0.05;

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (File.Exists(path))
                    {
                        return new FileStream(path, FileMode.Open, FileAccess.Read);
                    }
                    return ResourceFileHelpers.OpenResourceFile(fn);
                });

                double[] xx = new double[6];

                double tjd = swe.swe_julday(year, month, day, time, SwissEph.SE_GREG_CAL);
                double te = tjd + swe.swe_deltat(tjd);

                string star = search, serr = null;
                int res = swe.swe_fixstar(ref star, te, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.Equal(eres, res);
                if (res == SwissEph.ERR)
                {
                    Assert.Equal(error, serr);
                }
                else
                {
                    Assert.Equal(estar, star);
                }
            }
        }

        public static IEnumerable<object[]> TestDataFixstar2()
        {
            yield return new object[] { "1", 4, ",109Vir", null };
            yield return new object[] { "5", 4, ",13Mon", null };
            yield return new object[] { "10", 4, "Electra,17Tau", null };
            yield return new object[] { "25", 4, ",26UMa", null };
            yield return new object[] { "1000", 4, "Rukbalgethi Genubi,thHer", null };
            yield return new object[] { "10000", -1, "", "error, swe_fixstar(): sequential fixed star number 10000 is not available" };
            yield return new object[] { "aldebaran", 4, "Aldebaran,alTau", null };
            yield return new object[] { "aldeb", -1, "", "error, swe_fixstar(): could not find star name aldeb" };
            yield return new object[] { ",alTau", 4, "Aldebaran,alTau", null };
            yield return new object[] { "aldeb%", 4, "Aldebaran,alTau", null };
            yield return new object[] { "Spica", 4, "Spica,alVir", null };
            yield return new object[] { "alVir", -1, "", "error, swe_fixstar(): could not find star name alvir" };
            yield return new object[] { ",alVir", 4, "Spica,alVir", null };
            // ,alCMi (Procyon) -- see TestBayerSearchFindsStarInvertedUnderCultureSensitiveOrder
            // below for why this specific key matters.
            yield return new object[] { ",alCMi", 4, "Procyon,alCMi", null };
        }

        [Theory]
        [MemberData(nameof(TestDataFixstar2))]
        public void TestFixstar2(string search, int eres, string estar, string error)
        {
            int day = 16, month = 8, year = 1974;
            double time = 0.05;

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (File.Exists(path))
                    {
                        return new FileStream(path, FileMode.Open, FileAccess.Read);
                    }
                    return ResourceFileHelpers.OpenResourceFile(fn);
                });

                double[] xx = new double[6];

                double tjd = swe.swe_julday(year, month, day, time, SwissEph.SE_GREG_CAL);
                double te = tjd + swe.swe_deltat(tjd);

                string star = search, serr = null;
                int res = swe.swe_fixstar2(ref star, te, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.Equal(eres, res);
                if (res == SwissEph.ERR)
                {
                    Assert.Equal(error, serr);
                }
                else
                {
                    Assert.Equal(estar, star);
                }
            }
        }

        [Fact]
        public void TestBayerSearchFindsStarInvertedUnderCultureSensitiveOrder()
        {
            // search_star_in_list (SwissEphNet/CPort/Sweph.cs) looks up a star by
            // Bayer designation with C.bsearch over swed.fixed_stars, sorted by
            // fixedstar_name_compare (ordinal, string.Compare(..., StringComparison.Ordinal)).
            // The search comparator, fstar_node_compare, used to be plain
            // string.Compare(key, value.skey) -- culture-sensitive, with no
            // StringComparison -- so a binary search ran over an array sorted by
            // one order while probing with a different one. That is unsound by
            // construction: a binary search assumes the array is sorted by
            // exactly the comparator it searches with. The original C
            // (commented out immediately above fstar_node_compare in Sweph.cs)
            // uses strcmp for both roles; fstar_node_compare now does too
            // (C.strcmp, which is ordinal).
            //
            // ",alCMi" (Procyon's Bayer designation) is a concrete, measured
            // example of a key this actually broke: under the old
            // culture-sensitive comparator it was unfindable (confirmed by
            // reverting the fix locally and re-running this exact lookup, which
            // returned ERR / "could not find star name ,alCMi"). Measured against
            // the shipped sefstars.txt: 22 adjacent pairs in the ordinal-sorted
            // Bayer array invert under linguistic order, and simulating the
            // search makes 125 of 1,113 Bayer keys unfindable this way. The
            // fixed-star tests elsewhere in this project only ever probed
            // ",alTau" and ",alVir", which happen to land safely regardless of
            // comparator -- neither exercises this defect.
            int day = 16, month = 8, year = 1974;
            double time = 0.05;

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = ResourceFileHelpers.GetPortableFileName(path);
                    if (File.Exists(path))
                    {
                        return new FileStream(path, FileMode.Open, FileAccess.Read);
                    }
                    return ResourceFileHelpers.OpenResourceFile(fn);
                });

                double[] xx = new double[6];
                double tjd = swe.swe_julday(year, month, day, time, SwissEph.SE_GREG_CAL);
                double te = tjd + swe.swe_deltat(tjd);

                string star = ",alCMi";
                string serr = null;
                int res = swe.swe_fixstar2(ref star, te, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.Equal(4, res);
                Assert.Null(serr);
                Assert.Equal("Procyon,alCMi", star);
            }
        }

    }
}
