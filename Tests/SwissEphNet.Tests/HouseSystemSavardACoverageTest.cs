using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// House system 'J' (Savard-A, "Savard's supposed Albategnius houses", swehouse.c around
    /// line 1180) had no coverage anywhere in the suite: rewriting SweHouse.cs's `case 'J':`
    /// to an unreachable label falls through to Placidus without any unit test, conformance
    /// dispatch, or baseline generator noticing. The conformance corpus never uses house
    /// system 'J' or 'j' at all (confirmed against Tests/conformance's known-fail/passing
    /// rows), and lowercase 'j' had no coverage even in principle.
    /// </summary>
    public class HouseSystemSavardACoverageTest
    {
        // Reference values from the vendored C itself: a standalone driver linked directly
        // against external/swisseph's swedate.c, swehouse.c, swejpl.c, swemmoon.c, swemplan.c,
        // sweph.c, swephlib.c, swecl.c and swehel.c (the Makefile's SWEOBJ set, unmodified),
        // compiled with the MSVC toolchain already on this machine, calling swe_houses_armc
        // directly with the same armc/geolat/eps used throughout this test suite (see
        // TransliterationFidelityTest's Sunshine-house tests and HouseApiFidelityTest, both of
        // which use the same 123.45/40.0/23.4 triple).
        [Theory]
        [InlineData('J')]
        [InlineData('j')]
        public void TestHousesArmc_SavardA_MatchesReferenceC_NotPlacidus(char hsys)
        {
            const double armc = 123.45;
            const double geolat = 40.0;
            const double eps = 23.4;

            using (var swe = new SwissEph())
            {
                var cusp = new double[40];
                var ascmc = new double[10];

                int rc = swe.swe_houses_armc(armc, geolat, eps, hsys, cusp, ascmc);

                Assert.Equal(0, rc);
                // cusp[2] is where the reviewer's probe found the mutation (Savard-A silently
                // falling through to Placidus) observable: 228.9299 for 'J'/'j' against
                // 234.6672 for 'P'. Pinned to the reference driver's full precision.
                Assert.Equal(228.929870252, cusp[2], 6);
                Assert.Equal(254.253815835, cusp[3], 6);
                Assert.Equal(48.929870252, cusp[8], 6);
                Assert.Equal(74.253815835, cusp[9], 6);

                // Placidus, computed with the same inputs, must differ -- this is what a
                // fallen-through 'J' would silently produce instead.
                var cuspPlacidus = new double[40];
                var ascmcPlacidus = new double[10];
                swe.swe_houses_armc(armc, geolat, eps, 'P', cuspPlacidus, ascmcPlacidus);
                Assert.Equal(234.667162684, cuspPlacidus[2], 6);
                Assert.NotEqual(cuspPlacidus[2], cusp[2]);
            }
        }
    }
}
