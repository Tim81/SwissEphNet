using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwissEphNet.Tests
{
    public static class ResourceFileHelpers
    {
        public static Stream OpenResourceFile(string name)
        {
            var asm = typeof(ResourceFileHelpers).GetAssembly();
            String sr = $"SwissEphNet.Tests.files.{name}".Replace("/", ".").Replace("\\", ".");
            return asm.GetManifestResourceStream(sr);
        }

        /// <summary>
        /// Strips a directory prefix off a file path the same way on every OS.
        /// </summary>
        /// <remarks>
        /// CPort's swi_fopen (SwissEphNet/CPort/Sweph.cs) joins the configured
        /// ephemeris path to a file name with a hard-coded backslash,
        /// regardless of platform -- that join is part of the transliterated C
        /// and is not something this port can change (see CONTRIBUTING.md on
        /// CPort). The OnLoadFile filenames tests receive therefore always
        /// contain a backslash before the base file name, even on Linux/macOS.
        /// System.IO.Path.GetFileName only recognizes '/' as a separator on
        /// those platforms (Path.AltDirectorySeparatorChar equals
        /// DirectorySeparatorChar there), so it leaves a literal backslash --
        /// and everything before it -- as part of what it thinks is the file
        /// name. Splitting on both separators explicitly, rather than relying
        /// on Path.GetFileName, is what any OnLoadFile consumer needs to do to
        /// be portable against this library, on every OS.
        /// </remarks>
        public static string GetPortableFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int i = path.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 ? path.Substring(i + 1) : path;
        }
    }
}
