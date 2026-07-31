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
        /// <para>
        /// As of the DIR_GLUE fix (SwissEphNet/SwissEph.sweodef.h.cs,
        /// SwissEphNet/CPort/Sweph.cs:2634), CPort itself no longer
        /// hard-codes a backslash anywhere in the paths it builds: DIR_GLUE
        /// is '/' and both swi_fopen's ephepath+filename join and
        /// swi_gen_filename's asteroid-subdirectory join
        /// (SwissEphNet/CPort/SwephLib.cs) use it consistently. That part
        /// used to be a genuine, un-fixable CPort constraint (a hard-coded
        /// '\\' in swi_fopen, independent of DIR_GLUE); it no longer is --
        /// see docs/known-issues.md, "DIR_GLUE fixed: CPort/Sweph.cs:2634 was
        /// a mis-transliteration".
        /// </para>
        /// <para>
        /// This helper is still needed, though, for a different reason: the
        /// *other* half of the path comes from whatever the caller configured
        /// via <c>swe_set_ephe_path</c>, which CPort does not normalize. A
        /// Windows caller passing an OS-native path like
        /// <c>@"C:\ephe"</c> still produces filenames like
        /// <c>C:\ephe/ast4/se04179.se1</c> -- a genuine mix of separators, not
        /// a bug, just two different sources (the caller's own path, and
        /// CPort's internal DIR_GLUE joins) that do not have to agree.
        /// <see cref="System.IO.Path.GetFileName(string)"/> only recognizes
        /// '/' as a separator on non-Windows platforms
        /// (<c>Path.AltDirectorySeparatorChar</c> equals
        /// <c>DirectorySeparatorChar</c> there), so it cannot reliably strip
        /// a prefix that may contain either separator. Splitting on both
        /// explicitly, rather than relying on <c>Path.GetFileName</c>, is
        /// what any portable <c>OnLoadFile</c> consumer needs to do, on every
        /// OS, regardless of what path convention the caller's own configured
        /// ephemeris path happens to use.
        /// </para>
        /// </remarks>
        public static string GetPortableFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int i = path.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 ? path.Substring(i + 1) : path;
        }
    }

    /// <summary>
    /// Adapts a delegate to <see cref="SwissEph.IEphemerisFileProvider"/>, replacing the
    /// per-test <c>OnLoadFile</c> lambda handlers this project used to attach. Tests that read
    /// embedded ephemeris data (rather than a real directory <c>swe_set_ephe_path</c> could
    /// name) still need a provider -- see docs/known-issues.md's OnLoadFile entry for why an
    /// embedded resource is the one case a provider is still the right tool.
    /// </summary>
    public sealed class DelegateFileProvider : SwissEph.IEphemerisFileProvider
    {
        private readonly Func<string, Stream> _open;

        public DelegateFileProvider(Func<string, Stream> open) {
            _open = open;
        }

        public Stream Open(string path) => _open(path);
    }
}
