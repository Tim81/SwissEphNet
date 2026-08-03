using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>DumpRow.Parse's own guards against a malformed dump line.</summary>
public class DumpRowTests
{
    [Fact]
    public void Parse_reads_case_id_retc_err_and_hex_decoded_values()
    {
        // 3ff0000000000000 = 1.0 in IEEE 754 hex.
        var row = DumpRow.Parse("CALC|1\t0\t\t1.000000\t3ff0000000000000", "dump.tsv", 2);
        Assert.Equal("CALC|1", row.CaseId);
        Assert.Equal(0, row.Retc);
        Assert.Equal("", row.Err);
        Assert.Equal([1.0], row.Values);
    }

    [Fact]
    public void Parse_throws_on_too_few_fields()
    {
        var ex = Assert.Throws<FormatException>(() => DumpRow.Parse("CALC|1\t0", "dump.tsv", 2));
        Assert.Contains("malformed row", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_on_an_odd_number_of_decimal_hex_columns()
    {
        // 3 base fields + 1 trailing column (an incomplete decimal/hex pair) is not divisible by 2.
        var ex = Assert.Throws<FormatException>(() => DumpRow.Parse("CALC|1\t0\t\t1.0", "dump.tsv", 2));
        Assert.Contains("malformed row", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_on_an_empty_case_id()
    {
        var ex = Assert.Throws<FormatException>(() => DumpRow.Parse("\t0\t\t1.0\t3ff0000000000000", "dump.tsv", 2));
        Assert.Contains("empty case_id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_on_an_unparsable_retc()
    {
        var ex = Assert.Throws<FormatException>(() => DumpRow.Parse("CALC|1\tnot-an-int\t\t1.0\t3ff0000000000000", "dump.tsv", 2));
        Assert.Contains("cannot parse retc", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3ff000000000000")]  // 15 hex digits, one short
    [InlineData("3ff00000000000000")] // 17 hex digits, one long
    [InlineData("not-hex-at-all-x")]  // 16 characters, not valid hex
    public void Parse_throws_on_a_malformed_hex_column(string badHex)
    {
        var ex = Assert.Throws<FormatException>(() => DumpRow.Parse($"CALC|1\t0\t\t1.0\t{badHex}", "dump.tsv", 2));
        Assert.Contains("cannot parse hex column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reads_multiple_decimal_hex_pairs_in_order()
    {
        // 0.0 and 1.0.
        var row = DumpRow.Parse("CALC|1\t0\t\t0.0\t0000000000000000\t1.0\t3ff0000000000000", "dump.tsv", 2);
        Assert.Equal([0.0, 1.0], row.Values);
    }
}
