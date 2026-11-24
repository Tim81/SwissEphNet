using System;
using System.IO;
using System.Linq;
using SwissEphNet.Conformance.Tests.Corpus;
using SwissEphNet.Conformance.Tests.KnownFail;
using Xunit;

namespace SwissEphNet.Conformance.Tests;

/// <summary>
/// The correctness oracle. Runs every iteration in setest/t.exp against the
/// port and compares the outcome to Tests/conformance/known-fail.tsv:
///
///   PASS   -- the set of failures is exactly the known-fail list: no
///             regression (a new failure, or a known one whose category
///             drifted), nothing newly passing left un-pruned, no stale rows.
///   FAIL   -- otherwise. A regression fails the gate; so does a stale or
///             newly-passing row left in place, so the file and the port's
///             actual behavior cannot silently diverge from each other.
///
/// Deliberately NOT part of the fast unit-test run (Tests/SwissEphNet.Tests):
/// 18,919 iterations is not "fast". See .github/workflows/conformance.yml.
/// </summary>
public class ConformanceSuiteTests
{
    [Fact]
    public void CorpusParsesToExpectedTotals()
    {
        // Parse-only: this does not need a full 18,919-iteration dispatch
        // run (which PortMatchesKnownFailList already does), just the reader.
        var expPath = Path.Combine(RepoLocator.SetestDir, "t.exp");
        var doc = ExpReader.Read(expPath);

        // Measured by this reader against the checked-out submodule (external/swisseph at
        // v2.10.04, e308765d, whose t.exp upstream regenerated): 11 TESTSUITE, 68 TESTCASE,
        // 18,919 ITERATION blocks (v2.10.03 had 10, 60 and 12,757; the new suite 11 is 8
        // testcases and 1,620 iterations, and suites 4 and 6 grew to cover every ayanamsa and
        // house system). If this ever comes back lower, the reader is silently dropping rows --
        // investigate, do not loosen this assertion.
        Assert.Equal(11, doc.TestSuites.Count);
        Assert.Equal(68, doc.TotalTestCaseCount);
        Assert.Equal(18_919, doc.TotalIterationCount);

        // 508,929 physical "name: value" lines across every iteration --
        // includes both inputs and asserted values (t.exp does not separate
        // the two), and counts each of the 60 duplicate-key lines in
        // 9.1.1-9.1.20 (suite_09_rise.c reads atpress/attemp/ipl twice per
        // iteration) as its own line, not deduplicated.
        Assert.Equal(508_929, doc.TotalValueLineCount);
    }

    // Generous, not tuned to today's actual runtime (a few seconds): this exists to catch a
    // genuine hang (an accidental infinite loop in a future dispatch case, or a search function
    // that stops converging for some future JD the matrix starts covering), not to flag normal
    // slowness. xunit.v3-only -- see the SHOULD-FIX note this addresses for why
    // Tests/SwissEphNet.Tests (xunit v2) needs a .runsettings instead.
    [Fact(Timeout = 300_000)]
    public void PortMatchesKnownFailList()
    {
        var (_, results) = ConformanceRunner.Run();
        var knownFailPath = Path.Combine(RepoLocator.ConformanceDataDir, "known-fail.tsv");
        var knownFail = KnownFailList.Load(knownFailPath);

        var pins = PinList.Load(Path.Combine(RepoLocator.ConformanceDataDir, "known-fail-pins.tsv"));

        var report = ConformanceReport.Build(results, knownFail, pins, RepoLocator.EpheDir);

        var summary = report.FormatSummary();
        System.Console.WriteLine(summary);

        Assert.True(
            report.Regressions.Count == 0,
            $"{report.Regressions.Count} iteration(s) fail that are not on the known-fail list, or are on it under a " +
            $"different category (a regression either way). See stdout for details, or run the conformance report locally.\n{summary}");

        Assert.True(
            report.PinViolations.Count == 0,
            $"{report.PinViolations.Count} pin violation(s) in {report.PinViolations.Select(v => v.Key).Distinct().Count()} accepted VALUE-MISMATCH row(s): the rows no longer reproduce their pinned fields " +
            $"(known-fail-pins.tsv): a field joined or left the mismatch set, or a value moved beyond setest's tolerance. " +
            "A real change of behaviour: fix the port, or if it is deliberate, regenerate with scripts/regenerate-known-fail.ps1 -Reason." +
            Environment.NewLine +
            string.Join(Environment.NewLine, report.PinViolations.Take(20).Select(v => v.Message)));

        Assert.True(
            report.NewlyPassing.Count == 0,
            $"{report.NewlyPassing.Count} known-fail.tsv row(s) now pass. That's progress -- remove them and regenerate " +
            $"the file (scripts/regenerate-known-fail.ps1), don't leave stale rows in place.\n{summary}");

        Assert.True(
            report.Stale.Count == 0,
            $"{report.Stale.Count} known-fail.tsv row(s) reference an iteration no longer present in the corpus. " +
            $"Regenerate the file.\n{summary}");
    }
}
