using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// RowComparer.Compare's own guards, including the label-count hard-fail
/// (Tools/OracleVerify/RowOutcome.cs:99) verified live during the review that prompted this
/// project: appending a fourth pair to one row on both sides gives "expects 3 value field(s), row
/// has 4", exit 2. Reproduced here directly against the CALC func (6 fields) instead.
/// </summary>
public class RowComparerTests
{
    private static DumpRow Row(string caseId, int retc, string err, params double[] values) =>
        new() { CaseId = caseId, Retc = retc, Err = err, Values = values };

    [Fact]
    public void Compare_throws_when_the_two_dumps_disagree_on_value_field_count()
    {
        var c = Row("CALC|1", 0, "", 1.0, 2.0, 3.0);
        var net = Row("CALC|1", 0, "", 1.0, 2.0);
        var ex = Assert.Throws<FormatException>(() => RowComparer.Compare(c, net));
        Assert.Contains("value-field count differs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_throws_the_label_count_hard_fail_when_the_row_has_more_fields_than_its_func_expects()
    {
        // CALC expects exactly 6 (FieldLabels.For's XxLabels) -- a 7th field on both sides passes
        // the c/net-agree check above but must still fail here, naming the mismatch explicitly
        // rather than silently comparing only the first 6 or throwing an index-out-of-range.
        var c = Row("CALC|1", 0, "", 1, 2, 3, 4, 5, 6, 7);
        var net = Row("CALC|1", 0, "", 1, 2, 3, 4, 5, 6, 7);
        var ex = Assert.Throws<FormatException>(() => RowComparer.Compare(c, net));
        Assert.Contains("expects 6 value field(s), row has 7", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_throws_the_label_count_hard_fail_when_the_row_has_fewer_fields_than_its_func_expects()
    {
        var c = Row("CALC|1", 0, "", 1, 2, 3);
        var net = Row("CALC|1", 0, "", 1, 2, 3);
        var ex = Assert.Throws<FormatException>(() => RowComparer.Compare(c, net));
        Assert.Contains("expects 6 value field(s), row has 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_throws_when_called_with_mismatched_case_ids()
    {
        var c = Row("CALC|1", 0, "", 1, 2, 3, 4, 5, 6);
        var net = Row("CALC|2", 0, "", 1, 2, 3, 4, 5, 6);
        Assert.Throws<InvalidOperationException>(() => RowComparer.Compare(c, net));
    }

    [Fact]
    public void Compare_reports_no_field_diffs_and_Matches_true_for_bit_identical_rows()
    {
        var c = Row("CALC|1", 0, "", 1, 2, 3, 4, 5, 6);
        var net = Row("CALC|1", 0, "", 1, 2, 3, 4, 5, 6);
        var outcome = RowComparer.Compare(c, net);
        Assert.True(outcome.Matches);
        Assert.Empty(outcome.FieldDiffs);
    }

    [Fact]
    public void Compare_detects_a_retc_only_difference_as_RetcDiffers()
    {
        var c = Row("HOUSES|G|1", -1, "geoposition too far from earth center", new double[47]);
        var net = Row("HOUSES|G|1", 0, "", new double[47]);
        var outcome = RowComparer.Compare(c, net);
        Assert.False(outcome.Matches);
        Assert.Equal(FailureShape.RetcDiffers, outcome.Shape);
    }

    [Fact]
    public void Compare_detects_an_err_only_difference_as_ErrOnlyDiffers_when_retc_and_hex_both_match()
    {
        var c = Row("CALC|1", 0, "one message", 1, 2, 3, 4, 5, 6);
        var net = Row("CALC|1", 0, "a different message", 1, 2, 3, 4, 5, 6);
        var outcome = RowComparer.Compare(c, net);
        Assert.False(outcome.Matches);
        Assert.Equal(FailureShape.ErrOnlyDiffers, outcome.Shape);
    }

    [Fact]
    public void Compare_prioritizes_RetcDiffers_over_a_simultaneous_hex_difference()
    {
        var c = Row("CALC|1", -1, "", 1, 2, 3, 4, 5, 6);
        var net = Row("CALC|1", 0, "", 9, 2, 3, 4, 5, 6);
        var outcome = RowComparer.Compare(c, net);
        Assert.Equal(FailureShape.RetcDiffers, outcome.Shape);
    }
}
