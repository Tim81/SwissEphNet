using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class WaiversTests
{
    private static string WriteWaiversFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"waivers-test-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_AcceptsWellFormedWaiver()
    {
        var path = WriteWaiversFile("H|G|A*\t123\tsome reason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.Single(waivers);
            Assert.Equal("H|G|A*", waivers[0].Glob);
            Assert.Equal("123", waivers[0].PrNumber);
            Assert.Equal("some reason", waivers[0].Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_IgnoresBlankAndCommentLines()
    {
        var path = WriteWaiversFile("# a comment\n\n   \nH|G|A*\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.Single(waivers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsBareSingleStarGlob()
    {
        var path = WriteWaiversFile("*\t123\treason\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsBareDoubleStarGlob()
    {
        var path = WriteWaiversFile("**\t123\treason\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsHDoubleStarWithoutSeparator()
    {
        // The exact bypass a review found: "H**" compiles to ^H.*$ without the
        // leading-literal-segment rule, and would silently match every area whose
        // prefix starts with 'H' (H, HP, HN, HS, HX, HSUN).
        var path = WriteWaiversFile("H**\t123\treason\n");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
            Assert.Contains("wildcard before its first '|'", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsAllFieldsWildcardGlob()
    {
        // The other bypass a review found: matches every five-field case id in the
        // whole matrix.
        var path = WriteWaiversFile("*|*|*|*|*\t123\treason\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AcceptsLiteralPrefixWithSeparatorBeforeDoubleStar()
    {
        // The correct, and only, way to waive an entire area.
        var path = WriteWaiversFile("H|**\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.Single(waivers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsGlobThatMatchesSyntheticProbe()
    {
        // Not literally "*" or "**", and its leading segment ("ZZZ_WAIVER_PROBE") is
        // a literal, so it passes the structural check -- but its compiled pattern
        // still matches one of the probe case ids, exercising the probe-match
        // rejection specifically.
        var path = WriteWaiversFile("ZZZ_WAIVER_PROBE|**\t123\treason\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("onlyoneglob\n")]
    [InlineData("glob\treasonwithnopr\n")]
    [InlineData("glob\t\treason\n")]
    [InlineData("glob\t123\t   \n")]
    [InlineData("\t123\treason\n")]
    public void Load_RejectsMalformedLines(string content)
    {
        var path = WriteWaiversFile(content);
        try
        {
            Assert.Throws<InvalidOperationException>(() => Waivers.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompileGlob_IsTheSameCompilerLoadUses()
    {
        // Waivers.CompileGlob is what BaselineVerify's --diff-scope mode calls to
        // validate -ExpectedScope globs (scripts/regenerate-baseline.ps1). It must
        // apply exactly the rules Load() applies to Tests/baseline/waivers.tsv -- this is the
        // "do not write a second glob implementation" guarantee, exercised directly.
        var pattern = Waivers.CompileGlob("H|**", "--expected-scope", "-ExpectedScope glob");
        Assert.Matches(pattern, "H|A|23.4392911|-89|0");
        Assert.DoesNotMatch(pattern, "HP|G|0|-45|0|-5");
    }

    [Fact]
    public void CompileGlob_RejectsCatchAllRegardlessOfCallerLabel()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Waivers.CompileGlob("*", "--expected-scope", "-ExpectedScope glob"));
        Assert.Contains("-ExpectedScope glob", ex.Message, StringComparison.Ordinal);
        Assert.Contains("catch-all", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileGlob_RejectsWildcardBeforeFirstPipeRegardlessOfCallerLabel()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Waivers.CompileGlob("H**", "--expected-scope", "-ExpectedScope glob"));
        Assert.Contains("wildcard before its first '|'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchAll_FieldLocalStarDoesNotCrossPipes()
    {
        var path = WriteWaiversFile("H|*\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.NotEmpty(Waivers.MatchAll(waivers, "H|A"));
            Assert.Empty(Waivers.MatchAll(waivers, "H|A|1|2|3"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MatchAll_DoubleStarCrossesPipes()
    {
        var path = WriteWaiversFile("H|**\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.NotEmpty(Waivers.MatchAll(waivers, "H|A"));
            Assert.NotEmpty(Waivers.MatchAll(waivers, "H|A|1|2|3"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MatchAll_ReturnsEveryMatchingWaiverNotJustTheFirst()
    {
        // A broad area waiver and a narrower one nested inside it must both get
        // credit for the same row -- otherwise the narrower one always shows zero
        // matches and gets flagged stale purely because of line order.
        var path = WriteWaiversFile("H|**\t1\tbroad\nH|G|**\t2\tnarrow\n");
        try
        {
            var waivers = Waivers.Load(path);
            var matches = Waivers.MatchAll(waivers, "H|G|23.4392911|-89|0");
            Assert.Equal(2, matches.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompileGlob_AllWildcardsAfterARealAreaPrefix_MatchesEveryRowInThatAreaAndIsAccepted()
    {
        // "H|**" is documented (Waivers' own class doc, Tools/BaselineGen/README.md) as the
        // correct way to waive an entire area -- so a glob whose leading segment is a real,
        // literal area prefix followed by nothing but wildcards is *intended* to match every
        // row in that area, and CompileGlob must accept it. The synthetic-probe backstop
        // cannot and must not reject this: every probe id begins "ZZZ_WAIVER_PROBE", so only a
        // glob whose literal leading segment is exactly that reserved text could ever match one
        // -- a glob prefixed with a real area name like "GQ" or "H" never touches the probe ids
        // at all, matching or not. See CompileGlob's summary for what the backstop actually
        // guards (the reserved namespace itself), not general over-breadth.
        var pattern = Waivers.CompileGlob("GQ|*|*|*|*|*", "test", "waiver glob");
        Assert.Matches(pattern, "GQ|A|1|2|3|4");
        Assert.Matches(pattern, "GQ|Z|9|9|9|9");
    }

    [Fact]
    public void MatchAll_DoesNotSweepInUnrelatedAreasSharingAPrefixLetter()
    {
        var path = WriteWaiversFile("H|**\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.Empty(Waivers.MatchAll(waivers, "HP|G|0|-45|0|-5"));
            Assert.Empty(Waivers.MatchAll(waivers, "HSUN|I|0|0|0|sentinel99"));
            Assert.NotEmpty(Waivers.MatchAll(waivers, "H|A|23.4392911|-89|0"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
