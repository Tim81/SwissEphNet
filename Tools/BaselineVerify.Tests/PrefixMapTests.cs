using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class PrefixMapTests
{
    [Fact]
    public void Discover_SinglePrefix_ReturnsOneEntry()
    {
        var rows = new[]
        {
            "GQ|0|1000000|-1|0,51.5,0|0,0\t-1\t0\tinvalid method: -1",
            "GQ|0|1000000|0|-118.24,34.05,100|0,0\t0\t33.89058359653324\t",
        };

        var prefixes = PrefixMap.Discover(rows);

        Assert.Equal(["GQ"], prefixes);
    }

    [Fact]
    public void Discover_MultiplePrefixes_ReturnsAllSortedAndDeduplicated()
    {
        // Mirrors house-pos, which mixes HouseName ("HN|...") and HousePos ("HP|...") rows.
        var rows = new[]
        {
            "HP|0|0|-45|0|-5\t10\tswe_house_pos(): using simplified algorithm for system 0\\n",
            "HN|0\tPlacidus",
            "HN|A\tequal",
            "HP|0|0|-45|0|0\t10\t",
        };

        var prefixes = PrefixMap.Discover(rows);

        Assert.Equal(["HN", "HP"], prefixes);
    }

    [Fact]
    public void Discover_NoRows_ReturnsEmpty()
    {
        var prefixes = PrefixMap.Discover([]);
        Assert.Empty(prefixes);
    }

    [Fact]
    public void Discover_CaseIdWithNoPipe_ContributesWholeCaseIdAsItsOwnPrefix()
    {
        var rows = new[] { "SOLO\tvalue" };

        var prefixes = PrefixMap.Discover(rows);

        Assert.Equal(["SOLO"], prefixes);
    }

    [Fact]
    public void Discover_CaseIdWithNoTab_UsesWholeRowAsCaseId()
    {
        // Defensive: real rows always have at least one tab-separated column after the case
        // id, but Discover should not throw if one somehow doesn't.
        var rows = new[] { "GQ|0" };

        var prefixes = PrefixMap.Discover(rows);

        Assert.Equal(["GQ"], prefixes);
    }
}
