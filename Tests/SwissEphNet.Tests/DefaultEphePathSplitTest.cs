using SwissEphNet.CPort;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// The <c>SE_EPHE_PATH</c> default and <c>PATH_SEPARATOR</c> have to agree, on both
    /// platforms, because <c>swi_fopen</c> splits the one with the other (CPort/Sweph.cs,
    /// transliterating sweph.c:2377).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>swephexp.h:399-408</c> gives the default as <c>"\\sweph\\ephe\\"</c> under
    /// <c>#if MSDOS</c> and <c>".:/users/ephe2/:/users/ephe/"</c> otherwise;
    /// <c>sweodef.h:305/:311</c> gives the cut-list as <c>";:"</c> under <c>#if UNIX_FS</c>
    /// and <c>";"</c> otherwise. Each pair is self-consistent in the C, because the C is
    /// compiled per platform. This port is not, so both are chosen at run time, and the pairing
    /// is a property that has to be asserted rather than one the compiler enforces.
    /// </para>
    /// <para>
    /// It went wrong once in each direction. First the port carried upstream's colon-joined
    /// default while splitting on <c>';'</c> only, so the default arrived as one unsplit
    /// component, matched nothing, and lost the <c>"."</c> that <c>sweph.c:2381</c> maps to the
    /// current directory: a caller who never calls <c>swe_set_ephe_path</c> computed from
    /// Moshier on Linux and macOS where the C read the file. Then the default was rewritten
    /// with semicolons to suit the separator, which fixed that and moved the port away from the
    /// C, and the Linux and macOS exactness gates went red. The separator is what was wrong;
    /// the literal is upstream's and is carried verbatim.
    /// </para>
    /// <para>
    /// Both helpers take the platform as an argument because reading only the host's values
    /// cannot check this. On a Windows runner the non-Windows literal is never exercised, so a
    /// mismatch there passes unnoticed, which is how the first version survived until a Linux
    /// gate caught it. The characterization baseline cannot see it either: it is Moshier-only
    /// and never subscribes to file loading, so a silent Moshier fallback is invisible to it by
    /// construction.
    /// </para>
    /// </remarks>
    public class DefaultEphePathSplitTest
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DefaultPathSplitsIntoItsComponentsOnItsOwnPlatform(bool isWindows)
        {
            var path = SwissEph.DefaultEphePathFor(isWindows);
            var sep = SwissEph.PathSeparatorFor(isWindows);

            var n = SwephLib.swi_cutstr(path, sep, out var parts, 20);

            var expected = isWindows
                ? new[] { "\\sweph\\ephe\\" }
                : new[] { ".", "/users/ephe2/", "/users/ephe/" };

            Assert.Equal(expected, parts);
            Assert.Equal(expected.Length, n);
        }

        [Fact]
        public void NonWindowsDefaultKeepsTheCurrentDirectoryComponent()
        {
            // "." is the only one of upstream's three components that exists on a machine that
            // is not Astrodienst's. Losing it is what made the silent Moshier fallback, so it
            // is asserted on its own and not only as part of the collection above.
            Assert.Contains(".", Split(isWindows: false));
        }

        [Fact]
        public void NonWindowsSeparatorAcceptsBothFormsTheCAccepts()
        {
            // sweodef.h:305 is ";:" -- "semicolon or colon may be used". A caller on Unix may
            // reasonably pass either, and the colon form is what every other Swiss Ephemeris
            // binding on that platform takes.
            var sep = SwissEph.PathSeparatorFor(isWindows: false);
            Assert.Contains(';', sep);
            Assert.Contains(':', sep);

            Assert.Equal(new[] { "/a", "/b" }, Cut("/a:/b", sep));
            Assert.Equal(new[] { "/a", "/b" }, Cut("/a;/b", sep));
        }

        [Fact]
        public void WindowsSeparatorLeavesADriveLetterIntact()
        {
            // Why the colon is not simply added everywhere: sweodef.h:311 is ";" alone on
            // Windows, and a bare colon would cut "C:\ephe" at the drive letter. This is the
            // case that makes the cut-list platform-dependent rather than the union of both.
            var sep = SwissEph.PathSeparatorFor(isWindows: true);
            Assert.DoesNotContain(':', sep);

            Assert.Equal(new[] { "C:\\ephe", "D:\\ephe2" }, Cut("C:\\ephe;D:\\ephe2", sep));
        }

        [Fact]
        public void HostValuesComeFromTheHelpersForThisPlatform()
        {
            // The helpers are only worth testing if the shipped statics actually come from them.
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);

            Assert.Equal(SwissEph.PathSeparatorFor(isWindows), SwissEph.PATH_SEPARATOR);
            Assert.Equal(SwissEph.DefaultEphePathFor(isWindows), SwissEph.DefaultEphePath);
        }

        private static string[] Split(bool isWindows) =>
            Cut(SwissEph.DefaultEphePathFor(isWindows), SwissEph.PathSeparatorFor(isWindows));

        private static string[] Cut(string s, char[] sep)
        {
            SwephLib.swi_cutstr(s, sep, out var parts, 20);
            return parts;
        }
    }
}
