using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Conformance.Tests.Corpus;

public class ExpReaderTests
{
    private const string Sample = """
        localtime: 14.12.2023 16:48:13
        swisseph-version: 2.10.03
        TESTSUITE
          section-id: 1
          section-descr: Suite one
          TESTCASE
            section-id: 1
            section-descr: Case one
            ITERATION
              section-id: 1  #1.1.1
              iflag: 0 #
              jd: 2455334.00000000000000000000 # 17.5.2010 12:00:00
              xx[0]: 56.48558852041013977896
              xx[1]: -0.00002665382988817244
              rc: 2
              serr:
            ITERATION
              section-id: 2  #1.1.2
              iflag: 1
              jd: 111
              xx[0]: 1
              xx[1]: 2
              rc: 0
              serr: some error
          TESTCASE
            section-id: 2
            section-descr: Case two
            ITERATION
              section-id: 1
              jd: 5
        """;

    [Fact]
    public void ParsesHeaderSuitesTestCasesAndIterations()
    {
        using var reader = new StringReader(Sample);
        var doc = ExpReader.Read(reader, "sample");

        Assert.Equal("2.10.03", doc.Header["swisseph-version"]);
        Assert.Single(doc.TestSuites);

        var suite = doc.TestSuites[0];
        Assert.Equal(1, suite.Id);
        Assert.Equal("Suite one", suite.Description);
        Assert.Equal(2, suite.TestCases.Count);

        var case1 = suite.TestCases[0];
        Assert.Equal(1, case1.Id);
        Assert.Equal(2, case1.Iterations.Count);

        var iter1 = case1.Iterations[0];
        Assert.Equal(1, iter1.Id);
        Assert.Equal(0, iter1.Fields.GetInt("iflag"));
        Assert.Equal(2455334.0, iter1.Fields.GetDouble("jd"), 10);
        Assert.Equal(56.48558852041013977896, iter1.Fields.GetDouble("xx[0]"), 10);
        Assert.Equal(2, iter1.Fields.GetInt("rc"));
        Assert.Equal("", iter1.Fields.GetRawString("serr"));

        var iter2 = case1.Iterations[1];
        Assert.Equal("some error", iter2.Fields.GetRawString("serr"));

        var case2 = suite.TestCases[1];
        Assert.Equal(2, case2.Id);
        Assert.Single(case2.Iterations);
    }

    [Fact]
    public void GetDoubleTruncatesTrailingComment()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id: 1
              TESTCASE
                section-id: 1
                ITERATION
                  section-id: 1
                  jd: 2455334.00000000000000000000 # 17.5.2010 12:00:00
            """);
        var doc = ExpReader.Read(reader, "sample");
        var iteration = doc.TestSuites[0].TestCases[0].Iterations[0];
        Assert.Equal(2455334.0, iteration.Fields.GetDouble("jd"), 10);
    }

    [Fact]
    public void GetIntHandlesEmptyStringField()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id: 1
              TESTCASE
                section-id: 1
                ITERATION
                  section-id: 1
                  serr:
            """);
        var doc = ExpReader.Read(reader, "sample");
        var iteration = doc.TestSuites[0].TestCases[0].Iterations[0];
        Assert.Equal("", iteration.Fields.GetRawString("serr"));
    }

    [Fact]
    public void ThrowsOnUnparseableLine()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-id: 1
              this line has no colon and is not blank or a comment
            """);
        Assert.Throws<FormatException>(() => ExpReader.Read(reader, "sample"));
    }

    [Fact]
    public void ThrowsWhenSectionIdMissing()
    {
        using var reader = new StringReader("""
            TESTSUITE
              section-descr: no id here
            """);
        Assert.Throws<FormatException>(() => ExpReader.Read(reader, "sample"));
    }

    [Fact]
    public void TotalsAcrossWholeDocumentAreCorrect()
    {
        using var reader = new StringReader(Sample);
        var doc = ExpReader.Read(reader, "sample");

        Assert.Single(doc.TestSuites);
        Assert.Equal(2, doc.TotalTestCaseCount);
        Assert.Equal(3, doc.TotalIterationCount);
    }
}
