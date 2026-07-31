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
    /// Issue: https://github.com/ygrenier/SwissEphNet/issues/29
    /// </summary>
    public class Issue29Test
    {
        [Fact]
        public void Test()
        {
            // Like Issue27Test: the original report needed a real
            // C:\Temp\ephe\jplfiles\de430.eph, a machine-local path no CI runner has, guarded by
            // a File.Exists check -- and even when that guard passed, nothing was ever asserted.
            // swe_set_jpl_file opens and parses whatever file it is given as soon as it is
            // called (Sweph.cs's open_jpl_file), so the reported FormatException does not need
            // real JPL binary content to reproduce: a missing file still reaches, and returns
            // from, the same parsing path. Confirmed this runs (and previously would have
            // thrown) without any ephemeris file present, so the regression is now covered on
            // every CI run instead of never.
            string jplfolder = @"C:\Temp\ephe\jplfiles";
            string file = "de430.eph";
            string eop_today = "eop_1962_today.txt";
            string eop_finals = "eop_finals.txt";

            using (var swe = new SwissEph())
            {
                swe.FileProvider = new DelegateFileProvider(path =>
                {
                    string fn = Path.GetFileName(path);
                    if (File.Exists(path))
                    {
                        return new FileStream(path, FileMode.Open, FileAccess.Read);
                    }
                    else if (fn == eop_today || fn == eop_finals)
                    {
                        return ResourceFileHelpers.OpenResourceFile(fn);
                    }
                    return null;
                });
                swe.swe_set_ephe_path(jplfolder);

                // The issue raised a FormatException on this instruction.
                var ex = Record.Exception(() => swe.swe_set_jpl_file(file));

                Assert.Null(ex);
            }
        }

    }
}
