using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// DumpFile.Load's own refusals -- a duplicate case_id and a zero-row file -- were untested before
/// this file existed (MEDIUM 4): "a run that processed nothing is not a pass" and the duplicate
/// hard-fail are both load-bearing (see DumpFile's own remarks and Program.cs's LoadAndCompare,
/// which relies on DumpFile.Load already having refused an empty dump before its own "zero rows
/// were compared" floor could ever see one), but held up by nothing but manual review before now.
/// </summary>
public class DumpFileTests
{
    private static string WriteDump(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dump-file-test-{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_throws_on_a_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dump-file-missing-{Guid.NewGuid():N}.tsv");
        Assert.Throws<FileNotFoundException>(() => DumpFile.Load(path));
    }

    [Fact]
    public void Load_throws_on_zero_rows()
    {
        // A file that exists but contains nothing but blank lines: every line is skipped (Length ==
        // 0), so the row dictionary stays empty and the zero-row floor must fire -- "a run that
        // processed nothing is not a pass" (DumpFile's own remarks), the same posture
        // Tools/OracleDump/Program.cs and Tools/CReference/sedump.c take on their own output.
        var path = WriteDump("\n\n\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => DumpFile.Load(path));
            Assert.Contains("produced zero rows", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_throws_on_a_duplicate_case_id()
    {
        // case_id, retc, err, then one (decimal, hex) pair -- DumpRow.Parse's own minimal shape
        // (see DumpRow.cs's remarks); DumpFile.Load does not care how many pairs a real func
        // needs, only that Parse itself succeeds, so one pair is enough here.
        var row = "CALC|1\t0\t\t1.5\t3FF8000000000000";
        var path = WriteDump(row + "\n" + row + "\n");
        try
        {
            var ex = Assert.Throws<FormatException>(() => DumpFile.Load(path));
            Assert.Contains("duplicate case_id", ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_keys_rows_by_case_id_and_skips_blank_lines()
    {
        var row1 = "CALC|1\t0\t\t1.5\t3FF8000000000000";
        var row2 = "CALC|2\t0\t\t2.0\t4000000000000000";
        var path = WriteDump(row1 + "\n\n" + row2 + "\n");
        try
        {
            var rows = DumpFile.Load(path);
            Assert.Equal(2, rows.Count);
            Assert.Equal("CALC|1", rows["CALC|1"].CaseId);
            Assert.Equal("CALC|2", rows["CALC|2"].CaseId);
            Assert.Equal(1.5, rows["CALC|1"].Values[0]);
            Assert.Equal(2.0, rows["CALC|2"].Values[0]);
        }
        finally { File.Delete(path); }
    }
}
