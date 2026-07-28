using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class RowCountsTests
{
    private static string WriteRowCountsFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"row-counts-test-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"row-counts-missing-{Guid.NewGuid():N}.tsv");
        var counts = RowCounts.Load(path);
        Assert.Empty(counts);
    }

    [Fact]
    public void Load_ParsesEntries()
    {
        var path = WriteRowCountsFile("calc\t12608\nhouses-armc\t55512\n");
        try
        {
            var counts = RowCounts.Load(path);
            Assert.Equal(2, counts.Count);
            Assert.Equal(12608, counts["calc"]);
            Assert.Equal(55512, counts["houses-armc"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_IgnoresBlankAndCommentLines()
    {
        var path = WriteRowCountsFile("# a comment\n\n   \ncalc\t12608\n");
        try
        {
            var counts = RowCounts.Load(path);
            Assert.Single(counts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsNonIntegerCount()
    {
        var path = WriteRowCountsFile("calc\tmany\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => RowCounts.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsNegativeCount()
    {
        var path = WriteRowCountsFile("calc\t-1\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => RowCounts.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsMissingCount()
    {
        var path = WriteRowCountsFile("calc\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => RowCounts.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsDuplicateArea()
    {
        var path = WriteRowCountsFile("calc\t12608\ncalc\t99\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => RowCounts.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
