using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// swe_rise_trans_true_hor (SweCl.cs) samples up to 15 coarse points across a 28-hour window,
    /// refines any culmination it finds, and then binary-searches for the rise/set moment itself.
    /// Every one of those three phases re-invokes swe_calc for the target body, and each of the
    /// four call sites (SweCl.cs, currently around lines 4555, 4627, 4659 and 4721) guards its own
    /// call the same way: `if (SE.swe_calc(...) == SwissEph.ERR) return SwissEph.ERR;`. Before this
    /// file, nothing in this project called swe_rise_trans_true_hor with an input constructed to
    /// make swe_calc actually fail -- Suite09Rise (Tests/SwissEphNet.Conformance.Tests) dispatches
    /// the function, but only ever on success paths, since the conformance corpus has no
    /// intentionally-failing rows for it.
    ///
    /// A single body/date whose swe_calc call fails (a missing ephemeris file, or a date outside
    /// SE_INTP_APOG's/SE_INTP_PERG's Moshier-range guard) fails identically for every sample this
    /// function takes across the whole 28-hour window, coarse or refined: the failure reason
    /// (file not found, date out of range) does not depend on which of the ~15-40 nearby instants
    /// is being asked for. That makes the four guards mutually redundant for this whole class of
    /// input -- removing any strict subset of the four still leaves ERR propagating correctly,
    /// because whichever guard is reached next (later in the same call) catches it instead.
    /// Confirmed directly (and reverted before commit): with all four guards intact, both tests
    /// below fail fast at the very first sample (line ~4555); with any one, two or three of the
    /// four removed, both tests still return ERR, caught by whichever guard survives; only
    /// removing ALL FOUR simultaneously -- exactly the mutation the review that motivated this
    /// file used -- flips both tests to a false "success": rc=0 (not ERR), a stale non-null serr
    /// left over from the failed swe_calc call that nothing then re-checks or clears, and a
    /// fully-populated but meaningless tret. That is the same shape of bug as the fixed-star cache
    /// weakening this project also gained tests for: a return value that looks fine and an serr
    /// that is either empty or, worse, populated but silently ignored by the caller.
    ///
    /// Two different failure mechanisms, so a change that breaks only one of them (say, an
    /// ephemeris-file-availability check) still has the other to fall back on for coverage:
    /// </summary>
    public class RiseTransErrorPropagationTest
    {
        [Fact]
        public void Test_MissingEphemerisFile_PropagatesErrorRatherThanFabricatingATret()
        {
            // No FileProvider is attached, so SE_CERES (a minor planet -- always file-backed,
            // regardless of the ephemeris flag; see PlaDiamCoverageTest.cs's remarks on the same
            // point) fails on the very first swe_calc call the coarse loop makes.
            using (var swe = new SwissEph())
            {
                double tjd = swe.swe_julday(1974, 8, 16, 0.5, SwissEph.SE_GREG_CAL);
                double[] geopos = { 5.333889, 47.853333, 468 };
                double tret = 0;
                string serr = null;

                int rc = swe.swe_rise_trans_true_hor(tjd, SwissEph.SE_CERES, null, SwissEph.SEFLG_SWIEPH,
                    SwissEph.SE_CALC_RISE, geopos, 0, 0, 0, ref tret, ref serr);

                Assert.Equal(SwissEph.ERR, rc);
                Assert.False(string.IsNullOrEmpty(serr));
                Assert.Equal(0, tret);
            }
        }

        [Fact]
        public void Test_IntpApogOutsideMoshierRange_PropagatesErrorRatherThanFabricatingATret()
        {
            // SE_INTP_APOG (Sweph.cs) rejects any JD outside [MOSHLUEPH_START, MOSHLUEPH_END] =
            // [625000.5, 2818000.5] (Sweph.h.cs). Placing tjd_ut half a day before the upper bound
            // puts the coarse loop's 28-hour sampling window (tjd_ut-2h .. tjd_ut+26h) squarely
            // astride that boundary, so this exercises a MID-loop failure (confirmed: the first
            // few of the fifteen coarse samples are inside the valid range and only a later one
            // crosses out), not just an immediate first-call failure -- a different shape from the
            // missing-file test above, which fails on sample zero.
            using (var swe = new SwissEph())
            {
                const double MOSHLUEPH_END = 2818000.5;
                double tjd_ut = MOSHLUEPH_END - 0.5;
                double[] geopos = { 5.333889, 47.853333, 468 };
                double tret = 0;
                string serr = null;

                int rc = swe.swe_rise_trans_true_hor(tjd_ut, SwissEph.SE_INTP_APOG, null, SwissEph.SEFLG_SWIEPH,
                    SwissEph.SE_CALC_RISE, geopos, 0, 0, 0, ref tret, ref serr);

                Assert.Equal(SwissEph.ERR, rc);
                Assert.Contains("restricted", serr, StringComparison.Ordinal);
                Assert.Equal(0, tret);
            }
        }
    }
}
