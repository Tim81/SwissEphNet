using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SwissEphNet.Conformance.Tests.Corpus;

public class FixPrecisionReaderTests
{
    [Fact]
    public void DefaultsTo1eMinus9WhenNothingOverridesAnything()
    {
        using var reader = new StringReader("""
            # a leading comment, and a disabled directive that must not count
            #precision:1e-7
            TESTSUITE
              section-id:1
              TESTCASE
                section-id:1
            """);
        var table = FixPrecisionReader.Resolve(reader, [(1, 1)]);

        var p = table[(1, 1)];
        Assert.Equal(FixPrecisionReader.DefaultPrecision, p.All);
        Assert.All(p.Xx, x => Assert.Equal(FixPrecisionReader.DefaultPrecision, x));
    }

    [Fact]
    public void TestCaseLevelPrecisionXxOverridesAndFillsAll()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id:1
              TESTCASE
                section-id:1
                precision-xx:1e-8,1e-8,1e-8,1e-8,1e-8,1e-8
            """);
        var table = FixPrecisionReader.Resolve(reader, [(1, 1)]);

        var p = table[(1, 1)];
        Assert.All(p.Xx, x => Assert.Equal(1e-8, x));
    }

    [Fact]
    public void SuiteLevelPrecisionAppliesToEveryTestCaseThatDoesNotOverride()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id:6
              precision:1e-3
              TESTCASE
                section-id:1
              TESTCASE
                section-id:2
            """);
        var table = FixPrecisionReader.Resolve(reader, [(6, 1), (6, 2)]);

        Assert.Equal(1e-3, table[(6, 1)].All);
        Assert.Equal(1e-3, table[(6, 2)].All);
    }

    [Fact]
    public void TestCaseThatOverridesNothingCarriesForwardThePreviousResolvedValue()
    {
        // Reproduces the real reference tool's behavior: prepare_precisions
        // only overwrites the running (all, xx[]) state when its hierarchy
        // search finds something; a testcase that finds nothing inherits
        // whatever the previous testcase (in declaration order) left behind.
        using var reader = new StringReader("""
            TESTSUITE
              section-id:1
              TESTCASE
                section-id:1
                precision:1e-8
              TESTCASE
                section-id:2
            """);
        var table = FixPrecisionReader.Resolve(reader, [(1, 1), (1, 2)]);

        Assert.Equal(1e-8, table[(1, 1)].All);
        Assert.Equal(1e-8, table[(1, 2)].All); // inherited, not reset to default
    }

    [Fact]
    public void CarryForwardCrossesSuiteBoundaries()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id:4
              TESTCASE
                section-id:1
                precision:1e-8
            TESTSUITE
              section-id:5
              TESTCASE
                section-id:1
            """);
        var table = FixPrecisionReader.Resolve(reader, [(4, 1), (5, 1)]);

        Assert.Equal(1e-8, table[(5, 1)].All); // inherited from suite 4's last testcase
    }

    [Fact]
    public void CommentedPrecisionLinesAreInert()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id:1
              TESTCASE
                section-id:1
                #precision:1e-5
                precision-xx:1e-8,1e-8,1e-8,1e-6,1e-6,1e-6
              TESTCASE
                section-id:2
                #precision:1e-8
                #precision-xx:1e-5,1e-9,1e-9,1e-7,1e-7,1e-7
            """);
        var table = FixPrecisionReader.Resolve(reader, [(1, 1), (1, 2)]);

        Assert.Equal(new[] { 1e-8, 1e-8, 1e-8, 1e-6, 1e-6, 1e-6 }, table[(1, 1)].Xx);
        // testcase 2's own directives are all commented out, so it inherits testcase 1's.
        Assert.Equal(new[] { 1e-8, 1e-8, 1e-8, 1e-6, 1e-6, 1e-6 }, table[(1, 2)].Xx);
    }

    [Fact]
    public void OrphanedSuiteNotInValidPairsIsIgnoredEntirely()
    {
        // Reproduces t.fix's disabled/orphaned "suite 66": it must not affect
        // the carry-forward state used by real suites that follow it.
        using var reader = new StringReader("""
            TESTSUITE
              section-id:66
              precision:9.9
              TESTCASE
                section-id:1
            TESTSUITE
              section-id:6
              precision:1e-3
              TESTCASE
                section-id:1
            """);
        var table = FixPrecisionReader.Resolve(reader, [(6, 1)]);

        Assert.False(table.ContainsKey((66, 1)));
        Assert.Equal(1e-3, table[(6, 1)].All);
    }
}
