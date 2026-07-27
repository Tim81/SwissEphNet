using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class DivergenceReportTests
{
    [Fact]
    public void Collect_AllExact_ZeroDiffering()
    {
        List<string> local = ["A\t1.0\t2.0"];
        List<string> reference = ["A\t1.0\t2.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(2, stats.FieldsCompared);
        Assert.Equal(0, stats.FieldsDiffering);
        Assert.Empty(stats.SortedRelativeDiffs);
    }

    [Fact]
    public void Collect_OneFieldDiffers_CountsOnlyThatOne()
    {
        List<string> local = ["A\t1.0\t2.0"];
        List<string> reference = ["A\t1.0\t3.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(2, stats.FieldsCompared);
        Assert.Equal(1, stats.FieldsDiffering);
        Assert.Single(stats.SortedRelativeDiffs);
    }

    [Fact]
    public void Collect_NonNumericMismatch_IsNotCountedAsDiverging()
    {
        // An EXCEPTION marker vs a real value is Comparer's job to fail on; it is
        // not part of the numeric-divergence distribution.
        List<string> local = ["A\tEXCEPTION\tSomeType\t"];
        List<string> reference = ["A\t1.0\t2.0\t3.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(3, stats.FieldsCompared);
        Assert.Equal(0, stats.FieldsDiffering);
    }

    [Fact]
    public void Collect_ArityMismatch_IsSkippedEntirely()
    {
        List<string> local = ["A\t1.0\t2.0"];
        List<string> reference = ["A\t1.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(0, stats.FieldsCompared);
        Assert.Equal(0, stats.FieldsDiffering);
    }

    [Fact]
    public void Collect_ExistenceMismatch_IsSkippedEntirely()
    {
        List<string> local = ["A\t1.0"];
        List<string> reference = ["B\t1.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(0, stats.FieldsCompared);
        Assert.Equal(0, stats.FieldsDiffering);
    }

    [Fact]
    public void Collect_RelativeDiffMatchesExpectedMagnitude()
    {
        // local=2.0, reference=1.0 -> raw diff 1.0, scale 2.0 -> relative diff 0.5.
        List<string> local = ["A\t2.0"];
        List<string> reference = ["A\t1.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(1, stats.FieldsDiffering);
        Assert.Equal(0.5, stats.SortedRelativeDiffs[0], precision: 10);
    }

    [Fact]
    public void Collect_UsesAngleWraparoundSoRelativeDiffIsTiny()
    {
        // Same measured case as ComparerTests: raw diff is ~360, but the report
        // must reflect the true (tiny) angular difference, not the raw one -- this
        // is what makes the median/p90/p99 numbers meaningful instead of being
        // dominated by wraparound artifacts.
        List<string> local = ["A\t0"];
        List<string> reference = ["A\t359.99999999999994"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(1, stats.FieldsDiffering);
        Assert.True(stats.SortedRelativeDiffs[0] < 1e-12, $"expected a near-zero relative diff, got {stats.SortedRelativeDiffs[0]:E}");
    }

    [Fact]
    public void Collect_TracksFieldsBeyondToleranceSeparatelyFromDiffering()
    {
        // Three differing fields: one wraparound (now within tolerance), one
        // within the plain numeric tolerance, one genuinely beyond it.
        List<string> local = ["A\t0\t1.0000000000005\t270"];
        List<string> reference = ["A\t359.99999999999994\t1.0\t243.43494882292202"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(3, stats.FieldsDiffering);
        Assert.Equal(1, stats.FieldsBeyondTolerance);
    }

    [Fact]
    public void Percentile_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, DivergenceStats.Percentile([], 50));
    }

    [Fact]
    public void Percentile_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(7.0, DivergenceStats.Percentile([7.0], 50));
        Assert.Equal(7.0, DivergenceStats.Percentile([7.0], 99));
    }

    [Fact]
    public void Percentile_MedianOfKnownSet()
    {
        List<double> values = [1, 2, 3, 4, 5];
        Assert.Equal(3.0, DivergenceStats.Percentile(values, 50), precision: 10);
        Assert.Equal(1.0, DivergenceStats.Percentile(values, 0), precision: 10);
        Assert.Equal(5.0, DivergenceStats.Percentile(values, 100), precision: 10);
    }

    [Fact]
    public void Stats_MaxIsLastSortedValue()
    {
        List<string> local = ["A\t1.0", "B\t1.0"];
        List<string> reference = ["A\t2.0", "B\t100.0"];

        var stats = DivergenceReport.Collect(local, reference, "test");

        Assert.Equal(2, stats.FieldsDiffering);
        Assert.Equal(stats.SortedRelativeDiffs[^1], stats.Max, precision: 10);
    }
}
