using SwissEphNet;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Pins the invariant that keeps <c>sweph()</c>'s asteroid-subdirectory retry harmless.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SwissEphNet/CPort/Sweph.cs</c>'s asteroid retry strips a leading "ast0/" from the
    /// generated filename and tries again in the main ephemeris directory. It compares with
    /// <c>subdirlen &gt; 0 &amp;&amp; s.StartsWith(subdirnam, Ordinal)</c>, matching
    /// <c>sweph.c:2219</c>. The first half is load-bearing in C# in a way it is not in C:
    /// <c>strncmp</c> with a length of 0 returns 0 either way, but <c>"x".StartsWith("")</c> is
    /// unconditionally true, so without the guard an empty <c>subdirnam</c> takes the branch,
    /// strips <c>subdirlen + 1 == 1</c> character per pass, and walks the string down to an
    /// <c>ArgumentOutOfRangeException</c> where the C returns <c>NOT_AVAILABLE</c>.
    /// </para>
    /// <para>
    /// That guard cannot be reached through the public API today, and this test does not pretend
    /// to reach it. <c>subdirnam</c> is empty only when the generated filename carries no
    /// directory component, and <c>swi_gen_filename</c> always writes one for exactly the
    /// <c>ipli</c> values that reach the retry: <c>DIR_GLUE</c> is in both the asteroid format
    /// ("ast%d%sse%05d.%s") and the planetary-moon one ("sat%ssepm%d.%s"). So the invariant
    /// asserted here is the reason the guard is currently unreachable, not the guard itself.
    /// </para>
    /// <para>
    /// It is worth pinning because the coupling is invisible from either end. Someone changing
    /// <c>swi_gen_filename</c> to emit a flat filename would make a frozen-path guard in another
    /// file suddenly load-bearing, with nothing to say so. If these assertions ever fail, the
    /// retry in <c>sweph()</c> needs re-examining before the generator change lands.
    /// </para>
    /// </remarks>
    public class GenFilenameSubdirTest
    {
        // Julian day is irrelevant to the subdirectory decision; swi_gen_filename picks the
        // directory from ipli alone and uses tjd only to choose the file's time segment.
        private const double AnyJd = 2451545.0;

        [Theory]
        // Asteroids: ipli > SE_AST_OFFSET (10000). Ceres, Pallas, Juno, Vesta and Chiron are
        // the numbered asteroids the library reaches by default.
        [InlineData(SwissEph.SE_AST_OFFSET + 1)]
        [InlineData(SwissEph.SE_AST_OFFSET + 2)]
        [InlineData(SwissEph.SE_AST_OFFSET + 4)]
        [InlineData(SwissEph.SE_AST_OFFSET + 2060)]
        [InlineData(SwissEph.SE_AST_OFFSET + 99942)]
        // Planetary moons: SE_PLMOON_OFFSET (9000) < ipli < SE_AST_OFFSET.
        [InlineData(SwissEph.SE_PLMOON_OFFSET + 1)]
        [InlineData(SwissEph.SE_PLMOON_OFFSET + 501)]
        public void GeneratedFilenameCarriesADirectoryComponent(int ipli)
        {
            using var swe = new SwissEph();
            swe.SwephLib.swi_gen_filename(AnyJd, ipli, out var fname);

            Assert.False(string.IsNullOrEmpty(fname));

            // The retry in sweph() derives subdirnam by cutting fname at its last DIR_GLUE, so
            // "carries a directory component" means exactly "contains DIR_GLUE", and the cut
            // must leave something on the left for subdirlen to be non-zero.
            var glue = fname.IndexOf(SwissEph.DIR_GLUE, System.StringComparison.Ordinal);
            Assert.True(
                glue > 0,
                $"swi_gen_filename produced \"{fname}\" for ipli {ipli}, with no directory " +
                $"component before a \"{SwissEph.DIR_GLUE}\". That makes subdirnam empty in " +
                "sweph()'s asteroid retry, which puts the subdirlen > 0 guard there (matching " +
                "sweph.c:2219) on the reachable path. Re-examine that retry before accepting " +
                "this filename shape.");
        }
    }
}
