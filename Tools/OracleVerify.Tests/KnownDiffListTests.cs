using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// KnownDiffList.Load hard-fails (throws FormatException), rather than skipping, on a bad header,
/// a wrong column count, or a duplicate case_id -- see KnownDiffList.cs's own remarks: a reader
/// that tolerated any of those could silently compare against a truncated or corrupted list and
/// report a false PASS. Held up by nothing but manual review before this project existed.
/// </summary>
public class KnownDiffListTests
{
    private static string WriteTsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"known-diff-test-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_round_trips_a_well_formed_row()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tPORT-VERSION\t4\tlon differs\n");
        try
        {
            var loaded = KnownDiffList.Load(path);
            var entry = Assert.Single(loaded).Value;
            Assert.Equal("CALC|1", entry.CaseId);
            Assert.Equal(DiffCategory.PortVersion, entry.Category);
            Assert.Equal(4UL, entry.MaxUlp);
            Assert.Equal("lon differs", entry.Reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_reads_the_categorical_marker_as_null_MaxUlp_not_a_number()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tPORT-VERSION\tcategorical\tNaN on one side\n");
        try
        {
            var loaded = KnownDiffList.Load(path);
            Assert.Null(loaded["CALC|1"].MaxUlp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_a_wrong_header()
    {
        var path = WriteTsv("case_id\tcategory\treason\nCALC|1\tPORT-VERSION\tlon differs\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
            Assert.Contains("expected header", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_a_missing_header_entirely()
    {
        var path = WriteTsv(string.Empty);
        try
        {
            Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_wrong_column_count()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tPORT-VERSION\t4\ttoo\tmany\tcolumns\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
            Assert.Contains("expected 4 tab-separated columns", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_a_duplicate_case_id()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tPORT-VERSION\t4\ta\nCALC|1\tRETC\t0\tb\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
            Assert.Contains("duplicate entry", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_an_empty_case_id()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\n\tPORT-VERSION\t4\ta\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
            Assert.Contains("empty case_id", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_an_unparsable_category()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tNOT-A-CATEGORY\t4\ta\n");
        try
        {
            Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_an_unparsable_max_ulp()
    {
        var path = WriteTsv("case_id\tcategory\tmax_ulp\treason\nCALC|1\tPORT-VERSION\tnot-a-number\ta\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => KnownDiffList.Load(path));
            Assert.Contains("cannot parse max_ulp", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_then_Load_round_trips_categorical_and_numeric_rows_and_sorts_by_case_id()
    {
        var path = Path.Combine(Path.GetTempPath(), $"known-diff-roundtrip-{Guid.NewGuid():N}.tsv");
        try
        {
            var entries = new[]
            {
                new KnownDiffEntry("B|2", DiffCategory.Retc, 0, "retc differs"),
                new KnownDiffEntry("A|1", DiffCategory.PortVersion, null, "NaN on one side"),
            };
            KnownDiffList.Save(path, entries);

            var lines = File.ReadAllLines(path);
            Assert.Equal("case_id\tcategory\tmax_ulp\treason", lines[0]);
            // Sorted ordinally by case_id: A|1 before B|2, regardless of the order Save was handed.
            Assert.StartsWith("A|1\t", lines[1], StringComparison.Ordinal);
            Assert.StartsWith("B|2\t", lines[2], StringComparison.Ordinal);
            Assert.Contains("\tcategorical\t", lines[1], StringComparison.Ordinal);

            var reloaded = KnownDiffList.Load(path);
            Assert.Equal(2, reloaded.Count);
            Assert.Null(reloaded["A|1"].MaxUlp);
            Assert.Equal(0UL, reloaded["B|2"].MaxUlp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_truncates_the_destination_file_append_false()
    {
        // KnownDiffList.Save opens with append:false -- see the class's own remarks and
        // scripts/regenerate-oracle-known-diff.ps1's temp-file staging, which exists specifically
        // because calling Save straight against a committed path destroys its prior content the
        // moment the writer opens, before a single row is written back. This test pins that
        // documented behavior so a future change to append:true would be caught here rather than
        // discovered as a silent behavior change in the regeneration script.
        var path = Path.Combine(Path.GetTempPath(), $"known-diff-truncate-{Guid.NewGuid():N}.tsv");
        try
        {
            File.WriteAllText(path, "this content must not survive a Save call\n\n\n\n\n\n\n\n\n\n");
            KnownDiffList.Save(path, [new KnownDiffEntry("A|1", DiffCategory.Retc, 0, "x")]);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("must not survive", text, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }
}
