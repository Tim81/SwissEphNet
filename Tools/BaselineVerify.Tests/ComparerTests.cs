using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class ComparerTests
{
    private static Waiver MakeWaiver(string glob)
    {
        var path = Path.Combine(Path.GetTempPath(), $"waiver-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, $"{glob}\t1\ttest waiver\n");
        try
        {
            return Waivers.Load(path)[0];
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Compare_ExactMatch_CountsAsExact()
    {
        List<string> local = ["A\t1.5"];
        List<string> reference = ["A\t1.5"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Exact);
        Assert.Equal(0, result.ToleranceOk);
        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_WithinTolerance_CountsAsToleranceOk()
    {
        // Differs by 5e-13, under the 1e-12 absolute floor.
        List<string> local = ["A\t1.0000000000005"];
        List<string> reference = ["A\t1.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Exact);
        Assert.Equal(1, result.ToleranceOk);
        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_BeyondTolerance_CountsAsFail()
    {
        List<string> local = ["A\t1.01"];
        List<string> reference = ["A\t1.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
        Assert.Contains(result.FailureDetails, d => d.Contains("beyond tolerance", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_ZeroVersusNegligibleValue_IsWithinTolerance()
    {
        // The regression case: mutating an exact-zero field to 1e-18 must PASS, since
        // 1e-18 degrees is not a real behavior change. Before the absolute-epsilon
        // fix, the pure-relative formula treated this as an infinite relative jump.
        List<string> local = ["A\t1E-18"];
        List<string> reference = ["A\t0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
        Assert.Equal(1, result.ToleranceOk);
    }

    [Fact]
    public void Compare_ZeroVersusMeaningfulValue_StillFails()
    {
        // The absolute floor must not swallow real differences.
        List<string> local = ["A\t0.5"];
        List<string> reference = ["A\t0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    // --- Angle-wraparound tests: a measured cross-platform run found 2,637 fields
    // where Windows wrote 0 and Linux wrote 359.99999999999994 for the same house
    // cusp -- a raw difference of ~360, but a true angular difference of 5.68e-14
    // degrees. ---

    [Fact]
    public void Compare_AngleWraparoundAtZero_Passes()
    {
        // The exact measured case: one side lands just under 360 instead of
        // wrapping to 0. Raw difference is ~360; true angular difference is tiny.
        List<string> local = ["A\t0"];
        List<string> reference = ["A\t359.99999999999994"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_AngleWraparoundAt360_Passes()
    {
        // The mirror case: one side is exactly 360 instead of wrapping to 0.
        List<string> local = ["A\t360"];
        List<string> reference = ["A\t0.00000000000006"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_LargeDifferenceNotNearABoundary_StillFails()
    {
        // Neither value is within 1e-9 of 0 or 360, so this must not be treated as
        // wraparound -- it is a genuine, large difference (this is literally the
        // hsys 'Y' finding: cusp 2 = 270 on Windows, 243.43494882292202 on Linux).
        List<string> local = ["A\t270"];
        List<string> reference = ["A\t243.43494882292202"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    [Fact]
    public void Compare_JulianDayDifferenceOf360_StillFails()
    {
        // "Do not apply it blindly to every numeric field": a difference of ~360 in
        // a Julian Day (or any large, non-angular field) is meaningful and must not
        // be forgiven just because the raw gap happens to match 360.
        List<string> local = ["A\t2451545.0"];
        List<string> reference = ["A\t2451185.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    [Fact]
    public void Compare_SpeedValuesNearZeroOnBothSides_DoesNotFalselyWrap()
    {
        // Two SPEED values that both happen to sit near a station (close to zero,
        // not straddling the 0/360 wrap point) must be judged on their raw
        // difference, same as always -- wraparound must be a no-op here, not an
        // accidental loosening. This difference (2e-10) exceeds the absolute floor
        // (1e-12) and must still fail.
        List<string> local = ["A\t0.0000000003"];
        List<string> reference = ["A\t0.0000000001"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    [Fact]
    public void Compare_ValueNearBoundaryButOtherSideNotInDegreeRange_DoesNotWrap()
    {
        // One side is near 0, but the other is nowhere near a plausible degree
        // value -- this must not be treated as a wraparound candidate.
        List<string> local = ["A\t0.0000000001"];
        List<string> reference = ["A\t1000.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    // --- Exact-boundary tests (item 8): "just inside" vs "just outside" the
    // threshold itself, not "close" vs "ten billion times over". ---

    [Fact]
    public void Compare_JustInsideAbsoluteThreshold_Passes()
    {
        // scale ~= 1, so the threshold is exactly the 1e-12 absolute floor. 0.9x
        // stays safely on the "inside" side even after floating-point round-trip
        // noise from the addition and the string round-trip (a bit-exact 1.0x test
        // is not reliable: rounding in "a + threshold" itself can land a hair
        // above or below the true boundary).
        const double a = 1.0;
        const double threshold = 1e-12;
        var b = a + threshold * 0.9;
        List<string> local = [$"A\t{a:R}"];
        List<string> reference = [$"A\t{b:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_JustPastAbsoluteThreshold_Fails()
    {
        const double a = 1.0;
        const double threshold = 1e-12;
        var b = a + threshold * 1.1;
        List<string> local = [$"A\t{a:R}"];
        List<string> reference = [$"A\t{b:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    // --- Relative-tolerance branch (item 7): every other test above sits at
    // magnitude ~1, where the absolute floor always wins and RelativeEpsilon is
    // never actually the deciding term. These exercise the crossover (scale=10,
    // where 1e-13*scale == 1e-12) and the large-magnitude case that matters for
    // real data (datetime JD values around 5.37e6). ---

    [Fact]
    public void Compare_AtCrossoverScale_BothTermsAreEqual()
    {
        // scale = 10 is exactly where the relative term (1e-13 * 10 = 1e-12) equals
        // the absolute floor -- neither dominates, and the shared threshold is 1e-12.
        // 0.9x keeps this on the "inside" of the boundary despite floating-point
        // round-trip noise (see the absolute-threshold test above for why a bit-exact
        // 1.0x multiplier is not reliable here).
        const double a = 10.0;
        const double threshold = 1e-12;
        var b = a + threshold * 0.9;
        List<string> local = [$"A\t{a:R}"];
        List<string> reference = [$"A\t{b:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_LargeScale_RelativeTermGovernsAndAllowsProportionallyLargerDiff()
    {
        // scale ~ 5,370,000 (Julian-day sized). Relative threshold here is
        // ~5.37e-7, a hundred thousand times wider than the 1e-12 absolute floor --
        // if the absolute floor were still governing, this diff would fail.
        const double a = 5_370_000.0;
        const double relativeThreshold = 1e-13 * a; // ~5.37e-7
        var within = a + relativeThreshold * 0.5;
        List<string> local = [$"A\t{within:R}"];
        List<string> reference = [$"A\t{a:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_LargeScale_BeyondRelativeThreshold_Fails()
    {
        const double a = 5_370_000.0;
        const double relativeThreshold = 1e-13 * a;
        var beyond = a + relativeThreshold * 2.0;
        List<string> local = [$"A\t{beyond:R}"];
        List<string> reference = [$"A\t{a:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
    }

    [Fact]
    public void Compare_LargeScale_AbsoluteFloorAloneWouldHaveBeenFarTooTight()
    {
        // 100x the absolute floor (1e-10), but still << the ~5.37e-7 relative
        // threshold at this magnitude. Passing here proves the relative term, not
        // the absolute floor, is what is actually governing this comparison.
        const double a = 5_370_000.0;
        const double diff = 1e-10;
        List<string> local = [$"A\t{(a + diff):R}"];
        List<string> reference = [$"A\t{a:R}"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(0, result.Fail);
    }

    [Fact]
    public void Compare_NumericToException_IsFail()
    {
        // Same arity both sides (3 fields), but one side is not parseable as a
        // number, so it must fall to the exact-string-match path and fail.
        List<string> local = ["A\t1.0\t2.0\t3.0"];
        List<string> reference = ["A\tEXCEPTION\tSomeExceptionType\t"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
        Assert.Contains(result.FailureDetails, d => d.Contains("exact-match mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_ArityChange_IsFailWithFieldCountMessage()
    {
        List<string> local = ["A\t1.0\t2.0"];
        List<string> reference = ["A\t1.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(1, result.Fail);
        Assert.Contains(result.FailureDetails, d => d.Contains("field count differs", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_MissingFromLocal_IsOnlyReferenceAndNotWaivable()
    {
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = [];
        List<string> reference = ["A|1\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.OnlyReference);
        Assert.Equal(0, result.Waived);
        Assert.Equal(0, stats[waiver].Matched);
    }

    [Fact]
    public void Compare_MissingFromReference_IsOnlyLocalAndNotWaivable()
    {
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0"];
        List<string> reference = [];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.OnlyLocal);
        Assert.Equal(0, result.Waived);
        Assert.Equal(0, stats[waiver].Matched);
    }

    [Fact]
    public void Compare_WaiverSuppressesFailureAndTracksAsWaived()
    {
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0"];
        List<string> reference = ["A|1\t2.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(0, result.Fail);
        Assert.Equal(1, result.Waived);
        Assert.Equal(1, result.MatchedByAnyWaiver);
        Assert.Equal(1, stats[waiver].Matched);
        Assert.Equal(1, stats[waiver].Waived);
        Assert.Single(result.WaivedDetails);
    }

    [Fact]
    public void Compare_WaiverMatchingOnlyIdenticalRows_IsTrackedAsStale()
    {
        // Comparer itself does not fail the run for this -- Verdict.ForWaiver does,
        // using exactly this Matched/Waived data. This test documents the data
        // Comparer hands it.
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0"];
        List<string> reference = ["A|1\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.Exact);
        Assert.Equal(0, result.Waived);
        Assert.Equal(1, stats[waiver].Matched);
        Assert.Equal(0, stats[waiver].Waived);
    }

    [Fact]
    public void Compare_WaiverMatchingOnlyToleranceOkRows_DoesNotCountAsWaived()
    {
        // Regression test: a waiver whose matches are all merely within-tolerance
        // (never an outright failure) must NOT look "used" -- Waived must stay 0 so
        // Verdict.ForWaiver still flags it stale. Before this fix, "differed"
        // included ToleranceOk, so a waiver like this could dodge the stale check
        // forever without ever having excused an actual failure.
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0000000000005"]; // within tolerance of "A|1\t1.0"
        List<string> reference = ["A|1\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.ToleranceOk);
        Assert.Equal(0, result.Waived);
        Assert.Equal(1, stats[waiver].Matched);
        Assert.Equal(0, stats[waiver].Waived);
    }

    [Fact]
    public void Compare_OverlappingWaivers_BothGetCreditForTheSameRow()
    {
        // A broad area waiver and a narrower one nested inside it must not starve
        // each other of credit based on which one happens to load first.
        var broad = MakeWaiver("H|**");
        var narrow = MakeWaiver("H|G|**");
        var waivers = new List<Waiver> { broad, narrow };
        var stats = Waivers.InitStats(waivers);
        List<string> local = ["H|G|1\t1.0"];
        List<string> reference = ["H|G|1\t2.0"]; // would fail without a waiver

        var result = Comparer.Compare(local, reference, waivers, stats, "test");

        Assert.Equal(1, result.Waived);
        Assert.Equal(1, stats[broad].Matched);
        Assert.Equal(1, stats[broad].Waived);
        Assert.Equal(1, stats[narrow].Matched);
        Assert.Equal(1, stats[narrow].Waived);
    }

    [Fact]
    public void Compare_DuplicateCaseIdInLocal_Throws()
    {
        List<string> local = ["A\t1.0", "A\t2.0"];
        List<string> reference = ["A\t1.0"];

        Assert.Throws<InvalidOperationException>(() => Comparer.Compare(local, reference, [], [], "test"));
    }

    [Fact]
    public void Compare_DuplicateCaseIdInReference_Throws()
    {
        List<string> local = ["A\t1.0"];
        List<string> reference = ["A\t1.0", "A\t2.0"];

        Assert.Throws<InvalidOperationException>(() => Comparer.Compare(local, reference, [], [], "test"));
    }

    [Fact]
    public void Compare_ReportsRawLineCounts()
    {
        List<string> local = ["A\t1.0", "B\t2.0", ""];
        List<string> reference = ["A\t1.0", "B\t2.0"];

        var result = Comparer.Compare(local, reference, [], [], "test");

        Assert.Equal(2, result.LocalLineCount);
        Assert.Equal(2, result.ReferenceLineCount);
    }

    [Fact]
    public void WaivedFraction_ComputesCorrectly()
    {
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0", "B|1\t1.0"];
        List<string> reference = ["A|1\t2.0", "B|1\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.Waived);
        Assert.Equal(2, result.Total);
        Assert.Equal(0.5, result.WaivedFraction, precision: 10);
    }

    [Fact]
    public void MatchedFraction_CountsRegardlessOfOutcome()
    {
        // Two rows matched by the same waiver: one fails (waived), one is exact.
        // MatchedFraction must count both; WaivedFraction only the first.
        var waiver = MakeWaiver("A|*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A|1\t1.0", "A|2\t1.0"];
        List<string> reference = ["A|1\t2.0", "A|2\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.Waived);
        Assert.Equal(2, result.MatchedByAnyWaiver);
        Assert.Equal(0.5, result.WaivedFraction, precision: 10);
        Assert.Equal(1.0, result.MatchedFraction, precision: 10);
    }
}
