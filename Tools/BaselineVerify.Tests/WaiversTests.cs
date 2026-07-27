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
    public void Load_RejectsGlobThatMatchesSyntheticProbe()
    {
        // Not literally "*" or "**", but its compiled pattern ("^ZZZ_WAIVER_PROBE\|.*$")
        // matches the probe case id -- this exercises the probe-match rejection path
        // specifically, not the literal bare-star check.
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
    public void Match_FieldLocalStarDoesNotCrossPipes()
    {
        var path = WriteWaiversFile("H|*\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.NotNull(Waivers.Match(waivers, "H|A"));
            Assert.Null(Waivers.Match(waivers, "H|A|1|2|3"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Match_DoubleStarCrossesPipes()
    {
        var path = WriteWaiversFile("H|**\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.NotNull(Waivers.Match(waivers, "H|A"));
            Assert.NotNull(Waivers.Match(waivers, "H|A|1|2|3"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Match_DoesNotSweepInUnrelatedAreasSharingAPrefixLetter()
    {
        // "H*" without a field separator is the historical bug this glob syntax
        // exists to prevent: it must not match "HP|...", "HN|...", "HSUN|...", etc.
        var path = WriteWaiversFile("H*\t123\treason\n");
        try
        {
            var waivers = Waivers.Load(path);
            Assert.Null(Waivers.Match(waivers, "HP|G|0|-45|0|-5"));
            Assert.Null(Waivers.Match(waivers, "HSUN|I|0|0|0|sentinel99"));
            Assert.Null(Waivers.Match(waivers, "H|A|23.4392911|-89|0"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
