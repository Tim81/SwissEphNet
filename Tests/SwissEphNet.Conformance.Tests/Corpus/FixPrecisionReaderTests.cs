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
    public void TestCaseThatOverridesNothingStartsFromTheDefault()
    {
        // setest.c:253-258 (prepare_precisions, v2.10.04): "Start from the default in every test
        // case: a precision of an earlier test case does not carry over".
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
        Assert.Equal(FixPrecisionReader.DefaultPrecision, table[(1, 2)].All);
        Assert.All(table[(1, 2)].Xx, x => Assert.Equal(FixPrecisionReader.DefaultPrecision, x));
    }

    [Fact]
    public void NothingCarriesAcrossSuiteBoundariesEither()
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

        Assert.Equal(FixPrecisionReader.DefaultPrecision, table[(5, 1)].All);
    }

    [Fact]
    public void PrecisionXxFromAnEarlierTestCaseDoesNotCarryOver()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id:1
              TESTCASE
                section-id:1
                precision-xx:1e-8,1e-8,1e-8,1e-6,1e-6,1e-6
              TESTCASE
                section-id:2
                precision:1e-5
            """);
        var table = FixPrecisionReader.Resolve(reader, [(1, 1), (1, 2)]);

        Assert.Equal(new[] { 1e-8, 1e-8, 1e-8, 1e-6, 1e-6, 1e-6 }, table[(1, 1)].Xx);
        // "precision" sets every xx[i] too, and nothing of testcase 1's xx survives.
        Assert.All(table[(1, 2)].Xx, x => Assert.Equal(1e-5, x));
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
        // testcase 2's own directives are all commented out, so it gets the default.
        Assert.All(table[(1, 2)].Xx, x => Assert.Equal(FixPrecisionReader.DefaultPrecision, x));
    }

    [Fact]
    public void OrphanedSuiteNotInValidPairsIsIgnoredEntirely()
    {
        // Reproduces t.fix's disabled/orphaned "suite 66": it must not leak into real suites.
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
