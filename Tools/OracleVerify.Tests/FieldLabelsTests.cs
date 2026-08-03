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

    // MEDIUM 4: the two facts above pinned label TEXT for CALC and HOUSESEX2 only -- 2 of the 22
    // func tokens For_returns_the_documented_column_count already covers by count alone, which
    // proves the right NUMBER of labels but nothing about what they are actually named. A label
    // list that kept the right column count but silently renamed or reordered one of these would
    // pass every count-only assertion above. These four close that gap for the remaining
    // multi-value shapes the count-only theory does not already share a text assertion with:
    // AZALT's own three-element xaz[] labels (not the xx[]/cusp[]/ascmc[] families the two facts
    // above already cover), NODAPSUT's four six-double blocks with DIFFERENT base names in a fixed
    // order, MOONCROSSNODE's three mixed-name fields (not a BuildLabels sequence at all), and
    // HOUSESARMCEX2 sharing HouseSpeedLabels with HOUSESEX2 -- proving the "or" branch in
    // FieldLabels.For's switch actually returns the same array, not a same-shaped but differently
    // spelled one.
    [Fact]
    public void For_AZALT_labels_are_xaz_0_through_2_in_order()
    {
        var labels = FieldLabels.For("AZALT", "AZALT|1");
        Assert.Equal(["xaz[0]", "xaz[1]", "xaz[2]"], labels);
    }

    [Fact]
    public void For_NODAPSUT_labels_are_xnasc_then_xndsc_then_xperi_then_xaphe_six_each_in_order()
    {
        var labels = FieldLabels.For("NODAPSUT", "NODAPSUT|1");
        Assert.Equal("xnasc[0]", labels[0]);
        Assert.Equal("xnasc[5]", labels[5]);
        Assert.Equal("xndsc[0]", labels[6]);
        Assert.Equal("xndsc[5]", labels[11]);
        Assert.Equal("xperi[0]", labels[12]);
        Assert.Equal("xperi[5]", labels[17]);
        Assert.Equal("xaphe[0]", labels[18]);
        Assert.Equal("xaphe[5]", labels[23]);
    }

    [Fact]
    public void For_MOONCROSSNODE_labels_are_jd_cross_xlon_xlat_in_order()
    {
        var labels = FieldLabels.For("MOONCROSSNODE", "MOONCROSSNODE|1");
        Assert.Equal(["jd_cross", "xlon", "xlat"], labels);
    }

    [Fact]
    public void For_HOUSESARMCEX2_shares_HOUSESEX2s_own_label_array_not_just_its_shape()
    {
        Assert.Equal(FieldLabels.For("HOUSESEX2", "HOUSESEX2|1"), FieldLabels.For("HOUSESARMCEX2", "HOUSESARMCEX2|1"));
    }
}
