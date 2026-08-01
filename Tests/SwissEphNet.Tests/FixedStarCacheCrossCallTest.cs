using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Each of swe_fixstar2, swe_fixstar2_mag, swe_fixstar and swe_fixstar_mag keeps its own
    /// one-entry "last star" cache (Sweph.cs, see the field comment next to
    /// fixstar2_slast_starname: "sweph.c:6825-6826: swe_fixstar2's own static TLS
    /// slast_starname/last_stardata") so a second lookup for the SAME name on the SAME instance
    /// skips re-parsing the catalogue. An earlier fix already separated what used to be one
    /// shared cache into four independent ones (Sweph.cs:8039-8040, 8139-8140, 9248-9249,
    /// 9361-9362). Nothing besides the `strcmp(...) == 0` cache-key comparison itself stops a
    /// future edit from weakening that comparison down to something cheaper -- comparing name
    /// *length* instead of content, say -- which "sirius" and "altair" cannot distinguish: both
    /// are six characters. A weakened comparison would still pass every instrument that only
    /// ever constructs one fresh SwissEph instance per lookup (the oracle harness always does --
    /// Tools/OracleDump/Program.cs -- and so does every other fixstar test in this project before
    /// this file). Only two calls for two different equal-length names on ONE instance can catch
    /// it: the second call must return the second star's own data, not the first star's data
    /// wearing the second star's name.
    /// </summary>
    public class FixedStarCacheCrossCallTest
    {
        static void SubscribeStars(SwissEph swe)
        {
            swe.FileProvider = new DelegateFileProvider(path =>
            {
                if (ResourceFileHelpers.GetPortableFileName(path).Equals("sefstars.txt", StringComparison.OrdinalIgnoreCase))
                {
                    return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                }
                return null;
            });
        }

        static double Tjd()
        {
            using (var swe = new SwissEph())
            {
                return swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
            }
        }

        [Fact]
        public void Test_swe_fixstar2_SecondCallWithEqualLengthNameIsNotServedFromFirstCallsCache()
        {
            double tjd = Tjd();

            // Baseline: what "altair" alone, on its own fresh instance, actually returns.
            double[] xxAltairAlone = new double[6];
            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string name = "altair";
                string serr = null;
                int rc = swe.swe_fixstar2(ref name, tjd, SwissEph.SEFLG_MOSEPH, xxAltairAlone, ref serr);
                Assert.NotEqual(SwissEph.ERR, rc);
                Assert.Equal("Altair,alAql", name);
            }

            // "sirius" then "altair" on ONE instance -- both six characters.
            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string sirius = "sirius";
                double[] xxSirius = new double[6];
                string serr = null;
                int rcSirius = swe.swe_fixstar2(ref sirius, tjd, SwissEph.SEFLG_MOSEPH, xxSirius, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcSirius);
                Assert.Equal("Sirius,alCMa", sirius);

                string altair = "altair";
                double[] xxAltair = new double[6];
                int rcAltair = swe.swe_fixstar2(ref altair, tjd, SwissEph.SEFLG_MOSEPH, xxAltair, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcAltair);

                Assert.Equal("Altair,alAql", altair);
                Assert.Equal(xxAltairAlone[0], xxAltair[0], 9);
                Assert.Equal(xxAltairAlone[1], xxAltair[1], 9);
                Assert.Equal(xxAltairAlone[2], xxAltair[2], 9);
            }
        }

        [Fact]
        public void Test_swe_fixstar2_mag_SecondCallWithEqualLengthNameIsNotServedFromFirstCallsCache()
        {
            double magAltairAlone;
            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string name = "altair";
                string serr = null;
                magAltairAlone = 0;
                int rc = swe.swe_fixstar2_mag(ref name, ref magAltairAlone, ref serr);
                Assert.NotEqual(SwissEph.ERR, rc);
                Assert.Equal("Altair,alAql", name);
            }

            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string sirius = "sirius";
                double magSirius = 0;
                string serr = null;
                int rcSirius = swe.swe_fixstar2_mag(ref sirius, ref magSirius, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcSirius);
                Assert.Equal("Sirius,alCMa", sirius);

                string altair = "altair";
                double magAltair = 0;
                int rcAltair = swe.swe_fixstar2_mag(ref altair, ref magAltair, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcAltair);

                Assert.Equal("Altair,alAql", altair);
                Assert.Equal(magAltairAlone, magAltair, 12);
                Assert.NotEqual(magSirius, magAltair);
            }
        }

        [Fact]
        public void Test_swe_fixstar_SecondCallWithEqualLengthNameIsNotServedFromFirstCallsCache()
        {
            double tjd = Tjd();

            double[] xxAltairAlone = new double[6];
            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string name = "altair";
                string serr = null;
                int rc = swe.swe_fixstar(ref name, tjd, SwissEph.SEFLG_MOSEPH, xxAltairAlone, ref serr);
                Assert.NotEqual(SwissEph.ERR, rc);
                Assert.Equal("Altair,alAql", name);
            }

            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string sirius = "sirius";
                double[] xxSirius = new double[6];
                string serr = null;
                int rcSirius = swe.swe_fixstar(ref sirius, tjd, SwissEph.SEFLG_MOSEPH, xxSirius, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcSirius);
                Assert.Equal("Sirius,alCMa", sirius);

                string altair = "altair";
                double[] xxAltair = new double[6];
                int rcAltair = swe.swe_fixstar(ref altair, tjd, SwissEph.SEFLG_MOSEPH, xxAltair, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcAltair);

                Assert.Equal("Altair,alAql", altair);
                Assert.Equal(xxAltairAlone[0], xxAltair[0], 9);
                Assert.Equal(xxAltairAlone[1], xxAltair[1], 9);
                Assert.Equal(xxAltairAlone[2], xxAltair[2], 9);
            }
        }

        [Fact]
        public void Test_swe_fixstar_mag_SecondCallWithEqualLengthNameIsNotServedFromFirstCallsCache()
        {
            double magAltairAlone;
            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string name = "altair";
                string serr = null;
                magAltairAlone = 0;
                int rc = swe.swe_fixstar_mag(ref name, ref magAltairAlone, ref serr);
                Assert.NotEqual(SwissEph.ERR, rc);
                Assert.Equal("Altair,alAql", name);
            }

            using (var swe = new SwissEph())
            {
                SubscribeStars(swe);
                string sirius = "sirius";
                double magSirius = 0;
                string serr = null;
                int rcSirius = swe.swe_fixstar_mag(ref sirius, ref magSirius, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcSirius);
                Assert.Equal("Sirius,alCMa", sirius);

                string altair = "altair";
                double magAltair = 0;
                int rcAltair = swe.swe_fixstar_mag(ref altair, ref magAltair, ref serr);
                Assert.NotEqual(SwissEph.ERR, rcAltair);

                Assert.Equal("Altair,alAql", altair);
                Assert.Equal(magAltairAlone, magAltair, 12);
                Assert.NotEqual(magSirius, magAltair);
            }
        }
    }
}
