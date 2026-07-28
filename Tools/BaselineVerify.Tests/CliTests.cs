using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class CliTests
{
    [Fact]
    public void Parse_PositionalBaselineDirectory_IsNotDroppedWhenDumpFailuresIsAbsent()
    {
        // The regression: Program.cs used to compute `i != dumpFailuresFlagIndex + 1` with no
        // guard for --dump-failures being absent (index -1, +1 == 0), silently discarding
        // args[0] -- the positional baseline directory -- on every invocation that did not
        // also pass --dump-failures. `BaselineVerify C:\some\dir` used to report PASS against
        // the repo's own baseline instead of failing on the missing directory.
        var result = Cli.Parse(["C:\\definitely\\not\\a\\real\\dir"]);

        Assert.False(result.IsError);
        Assert.False(result.IsDiffScope);
        Assert.Equal("C:\\definitely\\not\\a\\real\\dir", result.Verify!.BaselineDir);
        Assert.False(result.Verify!.ReportOnly);
        Assert.Null(result.Verify!.DumpFailuresPath);
    }

    [Fact]
    public void Parse_PositionalBaselineDirectory_SurvivesAlongsideDumpFailures()
    {
        var result = Cli.Parse(["C:\\some\\dir", "--dump-failures", "C:\\out.txt"]);

        Assert.False(result.IsError);
        Assert.Equal("C:\\some\\dir", result.Verify!.BaselineDir);
        Assert.Equal("C:\\out.txt", result.Verify!.DumpFailuresPath);
    }

    [Fact]
    public void Parse_PositionalBaselineDirectory_SurvivesWhenDumpFailuresComesFirst()
    {
        // Flag-ordering: --dump-failures and its path argument appear before the positional
        // directory. dumpFailuresFlagIndex is 0 here, so the "skip index dumpFailuresFlagIndex
        // + 1" rule must skip index 1 (the path), not index 0 (the flag itself, already
        // excluded by name) and not silently eat the real positional argument at index 2.
        var result = Cli.Parse(["--dump-failures", "C:\\out.txt", "C:\\some\\dir"]);

        Assert.False(result.IsError);
        Assert.Equal("C:\\out.txt", result.Verify!.DumpFailuresPath);
        Assert.Equal("C:\\some\\dir", result.Verify!.BaselineDir);
    }

    [Fact]
    public void Parse_NoPositionalArgument_LeavesBaselineDirNull()
    {
        var result = Cli.Parse(["--report-only"]);

        Assert.False(result.IsError);
        Assert.True(result.Verify!.ReportOnly);
        Assert.Null(result.Verify!.BaselineDir);
    }

    [Fact]
    public void Parse_ReportOnlyBothSpellings_AreRecognized()
    {
        Assert.True(Cli.Parse(["--report-only"]).Verify!.ReportOnly);
        Assert.True(Cli.Parse(["-ReportOnly"]).Verify!.ReportOnly);
        Assert.False(Cli.Parse([]).Verify!.ReportOnly);
    }

    [Fact]
    public void Parse_DumpFailuresWithoutPathArgument_IsAnError()
    {
        var result = Cli.Parse(["--dump-failures"]);
        Assert.True(result.IsError);
        Assert.Contains("--dump-failures requires a file path argument", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DiffScope_MissingBothDirectories_IsAnError()
    {
        var result = Cli.Parse(["--diff-scope"]);
        Assert.True(result.IsError);
        Assert.Contains("--diff-scope requires two directory arguments", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DiffScope_MissingExpectedScope_IsAnError()
    {
        var result = Cli.Parse(["--diff-scope", "old", "new"]);
        Assert.True(result.IsError);
        Assert.Contains("requires --expected-scope", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DiffScope_EmptyGlobList_IsAnError()
    {
        var result = Cli.Parse(["--diff-scope", "old", "new", "--expected-scope"]);
        Assert.True(result.IsError);
        Assert.Contains("requires at least one glob", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DiffScope_WellFormed_ParsesDirectoriesAndGlobs()
    {
        var result = Cli.Parse(["--diff-scope", "old-dir", "new-dir", "--expected-scope", "H|**", "C|**"]);

        Assert.False(result.IsError);
        Assert.True(result.IsDiffScope);
        Assert.Equal("old-dir", result.DiffScope!.OldDir);
        Assert.Equal("new-dir", result.DiffScope!.NewDir);
        Assert.Equal(["H|**", "C|**"], result.DiffScope!.Globs);
    }

    [Fact]
    public void Parse_DiffScope_GlobThatContainsALiteralComma_IsPassedThroughUnsplit()
    {
        // 2,782 of 106,095 real case ids contain a literal comma (gauquelin, pheno, calc,
        // risetrans, eclipse, coord, pheno-ast). Cli.Parse must never split on ',' -- that is
        // exactly the bug in scripts/regenerate-baseline.ps1's own normalization, not
        // something this C# layer should also do. Waivers.CompileGlob's escaping already
        // handles a literal comma correctly; this only proves Cli.Parse hands it the glob
        // untouched.
        var result = Cli.Parse(["--diff-scope", "old", "new", "--expected-scope", "calc|defaulteph|1,2|**"]);

        Assert.False(result.IsError);
        Assert.Equal(["calc|defaulteph|1,2|**"], result.DiffScope!.Globs);
    }

    [Fact]
    public void Parse_DiffScope_FlagAfterExpectedScope_IsRejectedRatherThanTreatedAsAGlob()
    {
        // args[(scopeFlagIndex + 1)..] takes everything to the end of argv. Putting a
        // flag-shaped token after --expected-scope must be rejected outright, not silently
        // compiled as (and then likely rejected as an unrelated "too broad") glob.
        var result = Cli.Parse(["--diff-scope", "old", "new", "--expected-scope", "H|**", "--some-other-flag"]);

        Assert.True(result.IsError);
        Assert.Contains("--some-other-flag", result.Error, StringComparison.Ordinal);
        Assert.Contains("must be the last argument", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DiffScope_TakesPrecedenceOverVerifyFlags()
    {
        // --diff-scope must be recognized regardless of what other verify-mode-looking flags
        // are also present -- Cli.Parse checks for --diff-scope first, unconditionally.
        var result = Cli.Parse(["--report-only", "--diff-scope", "old", "new", "--expected-scope", "H|**"]);
        Assert.True(result.IsDiffScope);
    }
}
