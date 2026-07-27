using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class VerdictTests
{
    private static CompareResult MakeResult(int total, int fail = 0, int onlyLocal = 0, int onlyReference = 0, int waived = 0, int matchedByAnyWaiver = 0) =>
        new()
        {
            Total = total,
            Fail = fail,
            OnlyLocal = onlyLocal,
            OnlyReference = onlyReference,
            Waived = waived,
            MatchedByAnyWaiver = matchedByAnyWaiver,
        };

    [Fact]
    public void ForArea_AllZero_Passes()
    {
        var verdict = Verdict.ForArea(MakeResult(total: 100));
        Assert.True(verdict.Passed);
        Assert.Empty(verdict.Reasons);
    }

    [Fact]
    public void ForArea_ZeroTotal_Passes()
    {
        Assert.True(Verdict.ForArea(MakeResult(total: 0)).Passed);
    }

    [Fact]
    public void ForArea_AnyFail_Fails()
    {
        Assert.False(Verdict.ForArea(MakeResult(total: 100, fail: 1)).Passed);
    }

    [Fact]
    public void ForArea_OnlyLocal_Fails()
    {
        Assert.False(Verdict.ForArea(MakeResult(total: 100, onlyLocal: 1)).Passed);
    }

    [Fact]
    public void ForArea_OnlyReference_Fails()
    {
        Assert.False(Verdict.ForArea(MakeResult(total: 100, onlyReference: 1)).Passed);
    }

    [Fact]
    public void ForArea_WaivedFractionExactlyAtCap_Passes()
    {
        // 5 of 100 is exactly 5%; the rule is "> cap", so the boundary itself passes.
        Assert.True(Verdict.ForArea(MakeResult(total: 100, waived: 5)).Passed);
    }

    [Fact]
    public void ForArea_WaivedFractionJustUnderCap_Passes()
    {
        Assert.True(Verdict.ForArea(MakeResult(total: 1000, waived: 49)).Passed); // 4.9%
    }

    [Fact]
    public void ForArea_WaivedFractionJustOverCap_Fails()
    {
        Assert.False(Verdict.ForArea(MakeResult(total: 1000, waived: 51)).Passed); // 5.1%
    }

    [Fact]
    public void ForArea_MatchedBreadthExactlyAtCap_Passes()
    {
        Assert.True(Verdict.ForArea(MakeResult(total: 100, matchedByAnyWaiver: 5)).Passed);
    }

    [Fact]
    public void ForArea_MatchedBreadthJustOverCap_FailsEvenWithNoWaivedFailures()
    {
        // The reason breadth is tracked separately from failures-absorbed: a glob
        // touching a lot of rows is a risk before any of them ever regress.
        var verdict = Verdict.ForArea(MakeResult(total: 1000, waived: 0, matchedByAnyWaiver: 51));
        Assert.False(verdict.Passed);
        Assert.Contains(verdict.Reasons, r => r.Contains("breadth", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingBaselineFile_Fails()
    {
        var verdict = Verdict.MissingBaselineFile(@"some\path.tsv");
        Assert.False(verdict.Passed);
        Assert.Contains(@"some\path.tsv", verdict.Reasons[0]);
    }

    private static Waiver MakeWaiver(string glob = "A|*")
    {
        var path = Path.Combine(Path.GetTempPath(), $"waiver-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, $"{glob}\t1\ttest\n");
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
    public void ForWaiver_ZeroMatched_IsStale()
    {
        var verdict = Verdict.ForWaiver(MakeWaiver(), new WaiverStats { Matched = 0, Waived = 0 });
        Assert.True(verdict.Stale);
        Assert.Contains("matched zero rows", verdict.Reason);
    }

    [Fact]
    public void ForWaiver_MatchedButNeverWaivedAFailure_IsStale()
    {
        var verdict = Verdict.ForWaiver(MakeWaiver(), new WaiverStats { Matched = 5, Waived = 0 });
        Assert.True(verdict.Stale);
        Assert.Contains("never excused an actual failure", verdict.Reason);
    }

    [Fact]
    public void ForWaiver_MatchedAndActuallyWaivedAFailure_IsNotStale()
    {
        var verdict = Verdict.ForWaiver(MakeWaiver(), new WaiverStats { Matched = 5, Waived = 1 });
        Assert.False(verdict.Stale);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void CheckAssemblyIdentity_NullContent_IsSkippedAndNotSuspicious()
    {
        var verdict = Verdict.CheckAssemblyIdentity(null, Guid.NewGuid(), "abc");
        Assert.Equal(MvidCheckOutcome.Skipped, verdict.MvidOutcome);
        Assert.False(verdict.IsSuspiciousMatch);
    }

    [Fact]
    public void CheckAssemblyIdentity_ContentWithoutMvidLine_IsUnparseableAndNotSuspicious()
    {
        var verdict = Verdict.CheckAssemblyIdentity("FrameworkDescription=.NET 10.0.10\n", Guid.NewGuid(), "abc");
        Assert.Equal(MvidCheckOutcome.Unparseable, verdict.MvidOutcome);
        Assert.False(verdict.IsSuspiciousMatch);
    }

    [Fact]
    public void CheckAssemblyIdentity_MatchingMvid_IsSuspiciousMatch()
    {
        var mvid = Guid.NewGuid();
        var content = $"SwissEphModuleVersionId={mvid:D}\nSwissEphAssemblySha256=AABBCC\n";

        var verdict = Verdict.CheckAssemblyIdentity(content, mvid, "DDEEFF");

        Assert.Equal(MvidCheckOutcome.Matches, verdict.MvidOutcome);
        Assert.True(verdict.IsSuspiciousMatch);
    }

    [Fact]
    public void CheckAssemblyIdentity_DifferingMvidAndSha256_IsHealthyLocalMode()
    {
        var content = $"SwissEphModuleVersionId={Guid.NewGuid():D}\nSwissEphAssemblySha256=AABBCC\n";

        var verdict = Verdict.CheckAssemblyIdentity(content, Guid.NewGuid(), "DDEEFF");

        Assert.Equal(MvidCheckOutcome.Differs, verdict.MvidOutcome);
        Assert.True(verdict.Sha256Comparable);
        Assert.False(verdict.Sha256Matches);
        Assert.False(verdict.IsSuspiciousMatch);
    }

    [Fact]
    public void CheckAssemblyIdentity_DifferingMvidButMatchingSha256_IsStillSuspicious()
    {
        // Should not happen in practice (a different compile implies a different
        // MVID), but if the hashes ever do match, that alone must be treated as
        // suspicious regardless of the MVID outcome.
        var content = $"SwissEphModuleVersionId={Guid.NewGuid():D}\nSwissEphAssemblySha256=AABBCC\n";

        var verdict = Verdict.CheckAssemblyIdentity(content, Guid.NewGuid(), "AABBCC");

        Assert.True(verdict.Sha256Matches);
        Assert.True(verdict.IsSuspiciousMatch);
    }

    [Fact]
    public void CheckAssemblyIdentity_NoSha256Line_IsNotComparable()
    {
        var content = $"SwissEphModuleVersionId={Guid.NewGuid():D}\n";

        var verdict = Verdict.CheckAssemblyIdentity(content, Guid.NewGuid(), "DDEEFF");

        Assert.False(verdict.Sha256Comparable);
        Assert.False(verdict.Sha256Matches);
    }
}
