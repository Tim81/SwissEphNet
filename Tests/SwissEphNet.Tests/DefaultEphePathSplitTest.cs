using System.Runtime.InteropServices;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swi_fopen splits swed.ephepath on <see cref="SwissEph.PATH_SEPARATOR"/> (CPort/Sweph.cs,
    /// transliterating sweph.c:2377), and this port deliberately keeps PATH_SEPARATOR at
    /// { ';' } on every platform where the C's own non-Windows cut-list (sweodef.h:305) is
    /// ";:" -- a bare ':' cannot be added to a cross-platform cut-list without splitting a
    /// Windows drive letter. See SwissEph.sweodef.h.cs and docs/known-issues.md's "Three
    /// file-layer divergences" for that decision.
    ///
    /// The consequence, which is what this test pins: SwissEph.DefaultEphePath has to be
    /// joined with ';' off Windows too. Joined with upstream's ':' (swephexp.h:403) it
    /// reaches swi_fopen as ONE unsplit component, is used verbatim as a directory prefix,
    /// and matches nothing -- losing in particular the "." component that sweph.c:2381 maps
    /// to the current directory. A caller who never calls swe_set_ephe_path then computes
    /// from Moshier on Linux and macOS where the C reads the ephemeris file. Measured with
    /// Programs/SweTest against this repository's own ephe/ directory, same path, separator
    /// the only difference: ';' printed Sun 279.8584613 for 1.1.2000 and ':' printed
    /// "using Moshier eph." and 279.8584626.
    ///
    /// Neither verification gate can see that, which is why this test exists rather than a
    /// baseline row. The characterization baseline is Moshier-only and never subscribes to
    /// file loading, so a silent Moshier fallback is invisible to it by construction; the
    /// conformance and oracle runs are Windows, whose literal carries no separator at all.
    ///
    /// The cases below go through DefaultEphePathFor(bool) rather than the DefaultEphePath
    /// property on purpose. A test that reads the property exercises only the branch matching
    /// the runner it happens to be on, so on a Windows runner it passes whatever the
    /// non-Windows literal says -- confirmed by putting the ':' form back and watching the
    /// property-based version of this test stay green on both TFMs. Passing the platform in
    /// makes both literals reachable everywhere, so this fails on Windows too.
    /// </summary>
    public class DefaultEphePathSplitTest
    {
        // A ':' left inside a component means the string was joined with a character
        // swi_fopen does not split on. The one legitimate ':' is a Windows drive letter
        // ("C:\ephe"), which is part of a path rather than a separator between paths.
        static void AssertNoUnsplitSeparator(string[] parts)
        {
            foreach (var part in parts)
            {
                // Ordinal explicitly: a path separator is a byte, not a letter, and this
                // repository treats every unqualified comparison as a defect to be closed.
                var colon = part.IndexOf(':', System.StringComparison.Ordinal);
                Assert.True(
                    colon < 0 || colon == 1,
                    "component '" + part + "' of the default ephemeris path contains a ':' that is not "
                        + "a drive letter, so the default was joined with a separator swi_fopen does not "
                        + "split on; it will arrive there as one unusable path and every calculation "
                        + "will fall back to Moshier.");
            }
        }

        [Fact]
        public void NonWindowsDefaultSplitsIntoUpstreamsThreeComponents()
        {
            var parts = SwissEph.DefaultEphePathFor(isWindows: false).Split(SwissEph.PATH_SEPARATOR);

            AssertNoUnsplitSeparator(parts);

            // swephexp.h:403's three components, in upstream's order and spelling. The first
            // is the current directory, and is the only one of the three that exists on a
            // machine that is not Astrodienst's.
            Assert.Equal(new[] { ".", "/users/ephe2/", "/users/ephe/" }, parts);
        }

        [Fact]
        public void WindowsDefaultIsUpstreamsSingleComponent()
        {
            var parts = SwissEph.DefaultEphePathFor(isWindows: true).Split(SwissEph.PATH_SEPARATOR);

            AssertNoUnsplitSeparator(parts);

            // swephexp.h:401's single component; nothing to split.
            Assert.Equal(new[] { "\\sweph\\ephe\\" }, parts);
        }

        [Fact]
        public void ThePropertyPicksTheBranchForTheRunningPlatform()
        {
            // The two cases above prove the literals are right; this one proves the property
            // still routes to them, so neither of them is testing a value the library has
            // stopped using.
            Assert.Equal(
                SwissEph.DefaultEphePathFor(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)),
                SwissEph.DefaultEphePath);
        }
    }
}
