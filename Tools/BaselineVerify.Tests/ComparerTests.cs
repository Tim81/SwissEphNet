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
        var waiver = MakeWaiver("A*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = [];
        List<string> reference = ["A\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.OnlyReference);
        Assert.Equal(0, result.Waived);
        Assert.Equal(0, stats[waiver].Matched);
    }

    [Fact]
    public void Compare_MissingFromReference_IsOnlyLocalAndNotWaivable()
    {
        var waiver = MakeWaiver("A*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A\t1.0"];
        List<string> reference = [];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.OnlyLocal);
        Assert.Equal(0, result.Waived);
        Assert.Equal(0, stats[waiver].Matched);
    }

    [Fact]
    public void Compare_WaiverSuppressesFailureAndTracksAsDiffered()
    {
        var waiver = MakeWaiver("A*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A\t1.0"];
        List<string> reference = ["A\t2.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(0, result.Fail);
        Assert.Equal(1, result.Waived);
        Assert.Equal(1, stats[waiver].Matched);
        Assert.Equal(1, stats[waiver].Differed);
    }

    [Fact]
    public void Compare_WaiverMatchingOnlyIdenticalRows_IsTrackedAsStale()
    {
        // Comparer itself does not fail the run for this -- Program.cs's stale-waiver
        // check does, using exactly this Matched/Differed data. This test documents
        // the data Comparer hands it.
        var waiver = MakeWaiver("A*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A\t1.0"];
        List<string> reference = ["A\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.Exact);
        Assert.Equal(0, result.Waived);
        Assert.Equal(1, stats[waiver].Matched);
        Assert.Equal(0, stats[waiver].Differed);
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
        var waiver = MakeWaiver("A*");
        var stats = Waivers.InitStats([waiver]);
        List<string> local = ["A\t1.0", "B\t1.0"];
        List<string> reference = ["A\t2.0", "B\t1.0"];

        var result = Comparer.Compare(local, reference, [waiver], stats, "test");

        Assert.Equal(1, result.Waived);
        Assert.Equal(2, result.Total);
        Assert.Equal(0.5, result.WaivedFraction, precision: 10);
    }
}
