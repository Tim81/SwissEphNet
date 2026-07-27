using System.IO;
using SwissEphNet.Conformance.Tests.KnownFail;
using Xunit;

namespace SwissEphNet.Conformance.Tests;

/// <summary>
/// The correctness oracle. Runs every iteration in setest/t.exp against the
/// port and compares the outcome to Tests/conformance/known-fail.tsv:
///
///   PASS   -- the set of failures is a subset of the known-fail list.
///   FAIL   -- some iteration fails that is not on the list (a regression).
///   REPORT -- (without failing) any iteration on the list that now passes;
///             that is progress, and the entry should be removed.
///
/// Deliberately NOT part of the fast unit-test run (Tests/SwissEphNet.Tests):
/// 12,757 iterations is not "fast". See .github/workflows/conformance.yml.
/// </summary>
public class ConformanceSuiteTests
{
    [Fact]
    public void CorpusParsesToExpectedTotals()
    {
        var (doc, _) = ConformanceRunner.Run();

        // From the task brief / independently verified against the checked-out
        // submodule: 10 TESTSUITE, 60 TESTCASE, 12,757 ITERATION blocks. If
        // this ever comes back lower, the reader is silently dropping rows --
        // investigate, do not loosen this assertion.
        Assert.Equal(10, doc.TestSuites.Count);
        Assert.Equal(60, doc.TotalTestCaseCount);
        Assert.Equal(12_757, doc.TotalIterationCount);
    }

    [Fact]
    public void PortMatchesKnownFailList()
    {
        var (_, results) = ConformanceRunner.Run();
        var knownFailPath = Path.Combine(RepoLocator.ConformanceDataDir, "known-fail.tsv");
        var knownFail = KnownFailList.Load(knownFailPath);

        var report = ConformanceReport.Build(results, knownFail);

        var summary = report.FormatSummary();
        System.Console.WriteLine(summary);

        Assert.True(
            report.Passed,
            $"{report.Regressions.Count} iteration(s) fail that are not on the known-fail list (regressions). " +
            $"See stdout for details, or run the conformance report locally.\n{summary}");
    }
}
