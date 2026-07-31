using System;
using Xunit;

namespace SwissEphNet.Tests
{
    partial class SwissEphTest
    {
        [Fact]
        public void Test_swe_fixstar()
        {
            using (var swe = new SwissEph())
            {
                string name = "aldebaran";
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] geopos = new double[] { 47.853333, 5.333889, 468 };
                double[] xx = new double[6]; String serr = null;

                int iflag = swe.swe_fixstar(ref name, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.Equal(SwissEph.ERR, iflag);
                Assert.Equal("SwissEph file 'sefstars.txt' not found in PATH '[ephe]'", serr);

                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (string.Equals(path, "[ephe]/sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    }
                    return null;
                });

                iflag = swe.swe_fixstar(ref name, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.NotEqual(SwissEph.ERR, iflag);
                Assert.Null(serr);

                Assert.Equal("Aldebaran,alTau", name);
                Assert.Equal(69.43785467706, xx[0], 11);
                Assert.Equal(-5.46862068665, xx[1], 11);
                Assert.Equal(4214356.43826371, xx[2], 8);
                // This call passes SEFLG_MOSEPH only, without SEFLG_SPEED, so
                // xx[3..5] must come back zeroed. The nonzero values asserted
                // here previously encoded a bug in
                // swi_fixstar_calc_from_record: it set iflgsave = iflag
                // *after* OR-ing SEFLG_SPEED into iflag, so iflgsave always
                // carried the speed bit regardless of what the caller asked
                // for, and the trailing "if (!(iflgsave & SEFLG_SPEED))"
                // zero-fill never ran for a non-speed call. sweph.c:7627-7628
                // assigns iflgsave before OR-ing in SEFLG_SPEED, and
                // sweph.c:7871-7875 zero-fills xx[3..5] under that corrected
                // condition; the port now matches both. Confirmed against
                // Tools/OracleGrid/grid-files.tsv: the file-backed oracle
                // grid's fixstar rows now match the MSVC-built 2.10.03 C bit
                // for bit.
                Assert.Equal(0, xx[3]);
                Assert.Equal(0, xx[4]);
                Assert.Equal(0, xx[5]);

                name = "unknown";
                iflag = swe.swe_fixstar(ref name, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.Equal(SwissEph.ERR, iflag);
                Assert.Equal("star unknown not found", serr);
            }
        }

        [Fact]
        public void Test_swe_fixstar_ut()
        {
            using (var swe = new SwissEph())
            {
                string name = "aldebaran";
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] geopos = new double[] { 47.853333, 5.333889, 468 };
                double[] xx = new double[6]; String serr = null;

                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (string.Equals(path, "[ephe]/sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    }
                    return null;
                });

                int iflag = swe.swe_fixstar_ut(ref name, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);
                Assert.NotEqual(SwissEph.ERR, iflag);
                Assert.Null(serr);

                Assert.Equal("Aldebaran,alTau", name);
                Assert.Equal(69.43785475383, xx[0], 11);
                Assert.Equal(-5.46862068520, xx[1], 11);
                Assert.Equal(4214356.43827158, xx[2], 7);
                // This call passes SEFLG_MOSEPH only, without SEFLG_SPEED, so
                // xx[3..5] must come back zeroed -- see the matching comment
                // in Test_swe_fixstar for the swi_fixstar_calc_from_record bug
                // (sweph.c:7627-7628, sweph.c:7871-7875) that the previous
                // nonzero values here encoded. That also removes the reason
                // for the old xx[5] cross-platform tolerance below: the
                // 0.015543-vs-~0.0155325 divergence came from numerically
                // differentiating a nonzero speed, and a hard-zeroed field has
                // nothing left to differentiate or diverge.
                Assert.Equal(0, xx[3]);
                Assert.Equal(0, xx[4]);
                Assert.Equal(0, xx[5]);
            }
        }

        [Fact]
        public void Test_swe_fixstar_mag()
        {
            using (var swe = new SwissEph())
            {
                string name = "aldebaran";
                double mag = 0; String serr = null;

                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (string.Equals(path, "[ephe]/sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    }
                    return null;
                });

                int iflag = swe.swe_fixstar_mag(ref name, ref mag, ref serr);
                Assert.NotEqual(SwissEph.ERR, iflag);

                Assert.Equal("Aldebaran,alTau", name);
                Assert.Equal(0.86, mag, 12);
            }
        }

        // swe_fixstar2 reads sefstars.txt to the end, which used to leave CFile's EOF flag
        // set for good: Seek did not clear it, so C.rewind became a no-op and every later
        // lookup on the same instance failed with "star ... not found". The real C clears
        // the end-of-file indicator on fseek/rewind (C99 7.19.9.2), so it has no such
        // limitation. This is the user-visible half of the CFile.Seek fix.
        [Fact]
        public void Test_swe_fixstar_AfterFixstar2ReadsWholeFile()
        {
            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    if (string.Equals(path, "[ephe]/sefstars.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResourceFileHelpers.OpenResourceFile("sefstars.txt");
                    }
                    return null;
                });

                // Mirrors the sequence the conformance corpus runs: a path is established
                // first, so swed.fidat is initialized before either lookup.
                swe.swe_set_ephe_path("[ephe]");

                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] xx = new double[6]; String serr = null;

                // Loads the whole catalogue, reaching end of file.
                String probe = "aldebaran";
                swe.swe_fixstar2(ref probe, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                String name = "aldebaran";
                int iflag = swe.swe_fixstar(ref name, tjd, SwissEph.SEFLG_MOSEPH, xx, ref serr);

                Assert.NotEqual(SwissEph.ERR, iflag);
                Assert.Null(serr);
                Assert.Equal("Aldebaran,alTau", name);
            }
        }

    }

}
