using BaselineMatrix;
using SwissEphNet;
using Xunit;

namespace BaselineVerify.Tests;

/// <summary>
/// Ayanamsa.SidModeSweepCount (Tools/BaselineMatrix/Ayanamsa.cs) is a literal, not
/// SwissEph.SE_NSIDM_PREDEF, and that literal is the only thing bounding the sidereal-mode
/// sweep AddRows performs. Nothing re-derives it from the library, so a local-mode SwissEphNet
/// that grows a 48th predefined sidereal mode would leave the sweep, and row-counts.tsv's
/// committed 2,464, both silently unchanged -- every gate would stay green while the new mode
/// went ungenerated. This test is that guard.
///
/// It is deliberately NOT "SidModeSweepCount == SE_NSIDM_PREDEF": that equality is exactly what
/// Ayanamsa.cs's own doc comment says was tried and reverted, because reference mode resolves
/// SwissEphNet 2.8.0.2, whose SE_NSIDM_PREDEF is 43 -- four short of the sweep's 47 -- and an
/// equality guard evaluated there would fail on every reference-mode run, for a reason that has
/// nothing to do with a real regression. The directional form (&lt;=) only ever fires the way
/// this test wants it to: sweeping a mode id the library does not define is unambiguously a
/// bug (an IndexOutOfRangeException or worse waiting to happen at some other call site), so
/// SidModeSweepCount &gt; SE_NSIDM_PREDEF must fail loudly; SidModeSweepCount &lt; SE_NSIDM_PREDEF
/// (the case this test exists for) fails loudly too, but with a distinct message, since it
/// means new coverage exists and the matrix is not sweeping it.
///
/// Local-mode only, and deliberately without an #if in Ayanamsa.cs to enforce that: this test
/// project (BaselineVerify.Tests.csproj) already refuses to build with UseReferencePackage=true
/// (see its RejectReferenceMode target and BaselineVerify.csproj's matching one, which
/// ProjectReference propagates down through BaselineMatrix as well), so a test that only exists
/// in this project is local-mode-only by construction -- no separate conditional-compilation
/// guard needed in Ayanamsa.cs itself. Reading SidModeSweepCount from here (rather than
/// hardcoding 47 a second time) is exactly why the constant was changed from private to
/// internal, with BaselineMatrix.csproj's InternalsVisibleTo naming this assembly.
/// </summary>
public class AyanamsaSweepCoverageTests
{
    [Fact]
    public void SidModeSweepCount_never_exceeds_what_the_library_defines()
    {
        Assert.True(
            Ayanamsa.SidModeSweepCount <= SwissEph.SE_NSIDM_PREDEF,
            $"Ayanamsa.SidModeSweepCount ({Ayanamsa.SidModeSweepCount}) sweeps sidereal mode ids " +
            $"the library does not define (SwissEph.SE_NSIDM_PREDEF = {SwissEph.SE_NSIDM_PREDEF}). " +
            "swe_set_sid_mode is being called with an id past the end of the library's own " +
            "ayanamsa[] table -- fix Ayanamsa.SidModeSweepCount, do not raise this bound.");
    }

    [Fact]
    public void SidModeSweepCount_covers_every_predefined_sidereal_mode()
    {
        Assert.True(
            Ayanamsa.SidModeSweepCount >= SwissEph.SE_NSIDM_PREDEF,
            $"SwissEph.SE_NSIDM_PREDEF ({SwissEph.SE_NSIDM_PREDEF}) now exceeds " +
            $"Ayanamsa.SidModeSweepCount ({Ayanamsa.SidModeSweepCount}): local-mode SwissEphNet has " +
            "grown a new predefined sidereal mode the matrix does not sweep, and every AY/AYUT/AYEX/" +
            "AYEXUT case id for it is missing from the baseline. This needs a scoped regeneration, not " +
            "a hand edit: widen Ayanamsa.SidModeSweepCount to match SE_NSIDM_PREDEF, then run " +
            "scripts/regenerate-baseline.ps1 with an -ExpectedScope covering the new AY*/id rows so the " +
            "new coverage lands as a reviewed, provably-scoped change instead of silently appearing.");
    }
}
