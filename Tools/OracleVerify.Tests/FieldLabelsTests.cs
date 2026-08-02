using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// FieldLabels.For's unknown-func throw, plus the column count each known func returns -- this is
/// what RowComparer's own label-count hard-fail (see RowComparerTests) actually compares against,
/// so a label list that silently drifted from sedump.c/Tools/OracleDump's own column count would
/// make that hard-fail compare against the wrong number instead of catching anything.
/// </summary>
public class FieldLabelsTests
{
    [Fact]
    public void For_throws_FormatException_on_an_unrecognized_func_token()
    {
        var ex = Assert.Throws<FormatException>(() => FieldLabels.For("NOT_A_REAL_FUNC", "NOT_A_REAL_FUNC|1"));
        Assert.Contains("unrecognized func token", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NOT_A_REAL_FUNC|1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CALC", 6)]
    [InlineData("CALCUT", 6)]
    [InlineData("FIXSTAR", 6)]
    [InlineData("FIXSTAR2UT", 6)]
    [InlineData("PCTR", 6)]
    [InlineData("HOUSES", 47)]
    [InlineData("HOUSESARMC", 47)]
    [InlineData("HOUSESEX", 47)]
    [InlineData("HOUSESEX2", 94)]
    [InlineData("HOUSESARMCEX2", 94)]
    [InlineData("FIXSTARMAG", 1)]
    [InlineData("FIXSTAR2MAG", 1)]
    [InlineData("NAME", 0)]
    [InlineData("HOUSENAME", 0)]
    [InlineData("SOLCROSS", 1)]
    [InlineData("MOONCROSSNODE", 3)]
    [InlineData("AYANAMSA", 1)]
    [InlineData("AYANAMSAEX", 1)]
    [InlineData("SIDTIME", 1)]
    [InlineData("AZALT", 3)]
    [InlineData("NODAPSUT", 24)]
    [InlineData("GETCURRENTFILEDATA", 3)]
    public void For_returns_the_documented_column_count(string func, int expectedCount)
    {
        var labels = FieldLabels.For(func, $"{func}|1");
        Assert.Equal(expectedCount, labels.Count);
    }

    [Fact]
    public void For_CALC_labels_are_xx_0_through_5_in_order()
    {
        var labels = FieldLabels.For("CALC", "CALC|1");
        Assert.Equal(["xx[0]", "xx[1]", "xx[2]", "xx[3]", "xx[4]", "xx[5]"], labels);
    }

    [Fact]
    public void For_HOUSESEX2_labels_are_cusp_then_ascmc_then_cusp_speed_then_ascmc_speed_in_order()
    {
        var labels = FieldLabels.For("HOUSESEX2", "HOUSESEX2|1");
        Assert.Equal("cusp[0]", labels[0]);
        Assert.Equal("cusp[36]", labels[36]);
        Assert.Equal("ascmc[0]", labels[37]);
        Assert.Equal("ascmc[9]", labels[46]);
        Assert.Equal("cusp_speed[0]", labels[47]);
        Assert.Equal("cusp_speed[36]", labels[83]);
        Assert.Equal("ascmc_speed[0]", labels[84]);
        Assert.Equal("ascmc_speed[9]", labels[93]);
    }
}
