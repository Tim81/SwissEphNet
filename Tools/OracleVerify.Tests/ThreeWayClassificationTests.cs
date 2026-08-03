using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// ThreeWayClassifier.LoadAndClassify's own structural refusals (row-count mismatch, case_id-set
/// mismatch, zero rows classified) and ThreeWayClassificationFile's Save/Load round trip were
/// entirely untested before this file existed (MEDIUM 4) -- including Save's own `append: false`
/// writer (see KnownDiffListTests.cs's identical truncate concern for KnownDiffList.Save), which
/// carries the same "the committed file is gone the moment Save opens it" risk this class's own
/// two-dump sibling already has a regression test for.
/// </summary>
public class ThreeWayClassificationTests
{
    private static string WriteDump(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"threeway-dump-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, content);
        return path;
    }

    // FIXSTARMAG: FieldLabels.For requires a recognized func token (the part of case_id before
    // '|') and exactly the value-field count that token's own label array has -- see
    // RowComparer.Compare, which throws on an unrecognized func or a wrong count before this
    // classifier ever sees a value. FIXSTARMAG carries exactly one value field (MagLabels =
    // ["mag"]), the smallest well-formed shape any real func token has, which keeps every fixture
    // row below to a single (decimal, hex) pair.
    private static string Row(string caseId, double value) =>
        $"{caseId}\t0\t\t{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{BitConverter.DoubleToInt64Bits(value):X16}";

    [Fact]
    public void LoadAndClassify_throws_when_the_three_dumps_have_different_row_counts()
    {
        var c210 = WriteDump(Row("FIXSTARMAG|1", 1.0) + "\n" + Row("FIXSTARMAG|2", 2.0) + "\n");
        var c208 = WriteDump(Row("FIXSTARMAG|1", 1.0) + "\n");
        var net = WriteDump(Row("FIXSTARMAG|1", 1.0) + "\n" + Row("FIXSTARMAG|2", 2.0) + "\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => ThreeWayClassifier.LoadAndClassify(c210, c208, net));
            Assert.Contains("different row counts", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(c210); File.Delete(c208); File.Delete(net); }
    }

    [Fact]
    public void LoadAndClassify_throws_when_the_three_dumps_disagree_on_which_case_ids_are_present()
    {
        var c210 = WriteDump(Row("FIXSTARMAG|1", 1.0) + "\n");
        var c208 = WriteDump(Row("FIXSTARMAG|2", 1.0) + "\n");
        var net = WriteDump(Row("FIXSTARMAG|1", 1.0) + "\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => ThreeWayClassifier.LoadAndClassify(c210, c208, net));
            Assert.Contains("disagree on which case ids are present", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(c210); File.Delete(c208); File.Delete(net); }
    }

    // The port's value (netValue) against each C version's own value decides the bucket -- Tracks208
    // means the port MATCHES c208Value and DIFFERS from c210Value, and vice versa for Tracks210 --
    // not "the two C values happen to differ from each other" (c208Value/c210Value themselves are
    // never equal to each other in three of these four cases, but that is incidental to the
    // classification: only each one's relationship to netValue decides it).
    [Theory]
    [InlineData(1.0, 1.0, 1.0, VersionClassificationNames.AgreesBoth)]
    [InlineData(1.0, 2.0, 2.0, VersionClassificationNames.Tracks208)]
    [InlineData(2.0, 1.0, 2.0, VersionClassificationNames.Tracks210)]
    [InlineData(1.0, 2.0, 3.0, VersionClassificationNames.TracksNeither)]
    public void LoadAndClassify_sorts_a_case_id_into_the_documented_bucket(double c210Value, double c208Value, double netValue, string expected)
    {
        var c210 = WriteDump(Row("FIXSTARMAG|1", c210Value) + "\n");
        var c208 = WriteDump(Row("FIXSTARMAG|1", c208Value) + "\n");
        var net = WriteDump(Row("FIXSTARMAG|1", netValue) + "\n");
        try
        {
            var rows = ThreeWayClassifier.LoadAndClassify(c210, c208, net);
            var row = Assert.Single(rows);
            Assert.Equal(expected, VersionClassificationNames.ToName(row.Classification));
        }
        finally { File.Delete(c210); File.Delete(c208); File.Delete(net); }
    }

    [Fact]
    public void ThreeWayClassificationFile_Save_then_Load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"threeway-classification-roundtrip-{Guid.NewGuid():N}.tsv");
        try
        {
            var rows = new[]
            {
                new ThreeWayRow("B|2", VersionClassification.Tracks208, "retc 0!=1", "MATCH", "MATCH"),
                new ThreeWayRow("A|1", VersionClassification.AgreesBoth, "MATCH", "MATCH", "MATCH"),
            };
            ThreeWayClassificationFile.Save(path, rows);

            var lines = File.ReadAllLines(path);
            // Sorted ordinally by case_id (A|1 before B|2), regardless of the order Save was
            // handed -- matches KnownDiffList.Save's own sort contract.
            var dataLines = lines.Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            Assert.Equal("case_id\tclassification\tport_vs_2.08\tport_vs_2.10.03\tc208_vs_c210", dataLines[0]);
            Assert.StartsWith("A|1\t", dataLines[1], StringComparison.Ordinal);
            Assert.StartsWith("B|2\t", dataLines[2], StringComparison.Ordinal);

            var reloaded = ThreeWayClassificationFile.Load(path);
            Assert.Equal(2, reloaded.Count);
            Assert.Contains(reloaded, r => r.CaseId == "A|1" && r.Classification == VersionClassification.AgreesBoth);
            Assert.Contains(reloaded, r => r.CaseId == "B|2" && r.Classification == VersionClassification.Tracks208);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThreeWayClassificationFile_Save_truncates_the_destination_file_append_false()
    {
        // Same concern, and same fix shape, as KnownDiffListTests.cs's identical test for
        // KnownDiffList.Save: append:false means the committed file is destroyed the moment Save
        // opens it, before a single row is written back -- pinned here against the FULL expected
        // content, not merely DoesNotContain a marker, for the same reason that file's own remarks
        // give (DoesNotContain alone cannot distinguish "truncated then written correctly" from "the
        // file was emptied and nothing more").
        var path = Path.Combine(Path.GetTempPath(), $"threeway-classification-truncate-{Guid.NewGuid():N}.tsv");
        try
        {
            File.WriteAllText(path, "this content must not survive a Save call\n\n\n\n\n\n\n\n\n\n");
            ThreeWayClassificationFile.Save(path, [new ThreeWayRow("A|1", VersionClassification.AgreesBoth, "MATCH", "MATCH", "MATCH")]);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("must not survive", text, StringComparison.Ordinal);
            var dataLines = text.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            Assert.Equal("case_id\tclassification\tport_vs_2.08\tport_vs_2.10.03\tc208_vs_c210", dataLines[0]);
            Assert.Equal("A|1\tAGREES-BOTH\tMATCH\tMATCH\tMATCH", dataLines[1]);
        }
        finally { File.Delete(path); }
    }
}
