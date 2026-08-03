using System.Diagnostics;
using Xunit;

namespace BaselineVerify.Tests;

/// <summary>
/// End-to-end coverage for <c>--diff-scope</c> mode (Program.cs's RunDiffScopeMode,
/// Program.cs:259-378), the one entry point in this project with no end-to-end coverage before
/// this file: <c>ProgramEndToEndTests</c> exercises only the default verify path, never the
/// separate --diff-scope path scripts/regenerate-baseline.ps1's -ExpectedScope gate actually
/// runs, and ScopeDiffTests only unit-tests the pure ScopeDiff.ComputeArea function these lines
/// call, not Program.cs's own argv handling, exit codes, or console output -- the composition
/// these tests exist to prove is correct.
///
/// Unlike ProgramEndToEndTests, these use small synthetic two-row baseline directories rather
/// than a copy of the real, ~127,000-row Tests/baseline/: --diff-scope's own logic (ScopeDiff,
/// glob matching, offender/summary formatting) does not depend on the matrix's actual content,
/// and a tiny fixture keeps each test fast and makes the expected offenders/summaries exact and
/// legible, not "some subset of a huge real diff".
///
/// Coverage, against RunDiffScopeMode's own branches:
///   - all changes covered by -ExpectedScope   -- COVERED: DiffScope_AllChangesInScope_PrintsScopeOkAndSucceeds
///   - a changed id outside -ExpectedScope     -- COVERED: DiffScope_ChangeOutsideScope_PrintsOffenderAndFails
///   - an added id outside -ExpectedScope      -- COVERED: DiffScope_AddedRowOutsideScope_PrintsOffenderAndFails
///   - an area present only in oldDir (deleted)-- COVERED: DiffScope_AreaDeletedEntirely_TreatsEveryRowAsRemoved
///   - an invalid -ExpectedScope glob           -- COVERED: DiffScope_InvalidGlob_ExitsWithParseError
///   - the ROWCOUNT/SHA256/PREFIX lines a SCOPE-OK run emits (regenerate-baseline.ps1 parses
///     ROWCOUNT directly out of this output) -- COVERED: DiffScope_AllChangesInScope_PrintsScopeOkAndSucceeds
///
/// Every one of the above was confirmed by mutation: this file's own tests were written by first
/// checking each failed with the assertion this class claims (e.g. no OFFENDER line for an
/// in-scope-only run), same discipline ProgramEndToEndTests documents for the verify path.
/// </summary>
public class DiffScopeEndToEndTests
{
    [Fact(Timeout = 150_000)]
    public void DiffScope_AllChangesInScope_PrintsScopeOkAndSucceeds()
    {
        var (oldDir, newDir) = CreateFixturePair(
            oldRows: ["misc|1\tone", "misc|2\ttwo"],
            newRows: ["misc|1\tone", "misc|2\tCHANGED"]);
        try
        {
            var (exitCode, stdout, _) = RunDiffScope(oldDir, newDir, "misc|**");

            Assert.Equal(0, exitCode);
            Assert.Contains("SCOPE-OK", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("OFFENDER", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("SCOPE-VIOLATION", stdout, StringComparison.Ordinal);
            Assert.Contains("CHANGED-AREA misc: 1 changed / 0 added / 0 removed", stdout, StringComparison.Ordinal);
            Assert.Contains("ROWCOUNT misc\t2", stdout, StringComparison.Ordinal);
            Assert.Contains("PREFIX misc: misc", stdout, StringComparison.Ordinal);
            // SHA256 of newDir's baseline-misc.tsv -- exact hash not asserted (that belongs to a
            // Comparer/hashing unit test), just that the line shape regenerate-baseline.ps1's
            // $areaHashLines parsing depends on is actually emitted end to end.
            Assert.Matches(@"SHA256 misc\t[0-9A-F]{64}", stdout);
        }
        finally
        {
            Directory.Delete(oldDir, recursive: true);
            Directory.Delete(newDir, recursive: true);
        }
    }

    [Fact(Timeout = 150_000)]
    public void DiffScope_ChangeOutsideScope_PrintsOffenderAndFails()
    {
        var (oldDir, newDir) = CreateFixturePair(
            oldRows: ["misc|1\tone", "misc|2\ttwo"],
            newRows: ["misc|1\tone", "misc|2\tCHANGED"]);
        try
        {
            // Scope only covers "misc|1|**", not "misc|2" -- the exact row that changed.
            var (exitCode, stdout, _) = RunDiffScope(oldDir, newDir, "misc|1|**");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("SCOPE-VIOLATION", stdout, StringComparison.Ordinal);
            Assert.Contains("OFFENDER area=misc caseid=misc|2 (changed)", stdout, StringComparison.Ordinal);
            Assert.Contains("SCOPE-FAIL", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("SCOPE-OK", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(oldDir, recursive: true);
            Directory.Delete(newDir, recursive: true);
        }
    }

    [Fact(Timeout = 150_000)]
    public void DiffScope_AddedRowOutsideScope_PrintsOffenderAndFails()
    {
        var (oldDir, newDir) = CreateFixturePair(
            oldRows: ["misc|1\tone"],
            newRows: ["misc|1\tone", "misc|2\ttwo"]);
        try
        {
            var (exitCode, stdout, _) = RunDiffScope(oldDir, newDir, "nomatch|**");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("OFFENDER area=misc caseid=misc|2 (added)", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(oldDir, recursive: true);
            Directory.Delete(newDir, recursive: true);
        }
    }

    [Fact(Timeout = 150_000)]
    public void DiffScope_AreaDeletedEntirely_TreatsEveryRowAsRemoved()
    {
        // "misc" exists only in oldDir (as if BaselineMatrix's Areas.All dropped it) -- see the
        // AreaNamesFromBaselineFiles doc comment in Program.cs: every one of its case ids must
        // still be classified "removed" and covered by -ExpectedScope, not silently skipped
        // because there is no baseline-misc.tsv in newDir to diff against.
        var oldDir = Path.Combine(Path.GetTempPath(), "DiffScopeEndToEndTests-old-" + Guid.NewGuid().ToString("N"));
        var newDir = Path.Combine(Path.GetTempPath(), "DiffScopeEndToEndTests-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        File.WriteAllLines(Path.Combine(oldDir, "baseline-misc.tsv"), new[] { "misc|1\tone" });
        try
        {
            var (exitCode, stdout, _) = RunDiffScope(oldDir, newDir, "nomatch|**");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("OFFENDER area=misc caseid=misc|1 (removed)", stdout, StringComparison.Ordinal);
            // No newPath means no ROWCOUNT/SHA256/PREFIX line for it (Program.cs's own File.Exists(newPath) guard).
            Assert.DoesNotContain("ROWCOUNT misc", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(oldDir, recursive: true);
            Directory.Delete(newDir, recursive: true);
        }
    }

    [Fact(Timeout = 150_000)]
    public void DiffScope_InvalidGlob_ExitsWithParseError()
    {
        var (oldDir, newDir) = CreateFixturePair(
            oldRows: ["misc|1\tone"],
            newRows: ["misc|1\tone"]);
        try
        {
            // A catch-all glob ("*" alone, no '|') is one of Waivers.CompileGlob's own rejected
            // shapes (see its doc comment, reused here per RunDiffScopeMode's own remarks) --
            // this exercises the try/catch around compiled = globs.Select(...) at the top of
            // RunDiffScopeMode, distinct from every other test here (which all reach the main
            // diff body).
            var (exitCode, _, stderr) = RunDiffScope(oldDir, newDir, "*");

            Assert.Equal(2, exitCode);
            Assert.NotEmpty(stderr);
        }
        finally
        {
            Directory.Delete(oldDir, recursive: true);
            Directory.Delete(newDir, recursive: true);
        }
    }

    private static (string OldDir, string NewDir) CreateFixturePair(string[] oldRows, string[] newRows)
    {
        var oldDir = Path.Combine(Path.GetTempPath(), "DiffScopeEndToEndTests-old-" + Guid.NewGuid().ToString("N"));
        var newDir = Path.Combine(Path.GetTempPath(), "DiffScopeEndToEndTests-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        File.WriteAllLines(Path.Combine(oldDir, "baseline-misc.tsv"), oldRows);
        File.WriteAllLines(Path.Combine(newDir, "baseline-misc.tsv"), newRows);
        return (oldDir, newDir);
    }

    /// <summary>
    /// Runs the real, compiled BaselineVerify.dll (a ProjectReference build output sitting next
    /// to this test assembly) as a subprocess in --diff-scope mode -- same technique as
    /// ProgramEndToEndTests.RunBaselineVerify, duplicated rather than shared because that method
    /// is private to a different test class and always passes a single positional directory
    /// argument, not this mode's five-plus-argument shape.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunDiffScope(string oldDir, string newDir, params string[] globs)
    {
        var baselineVerifyDll = Path.Combine(AppContext.BaseDirectory, "BaselineVerify.dll");
        Assert.True(
            File.Exists(baselineVerifyDll),
            $"Expected BaselineVerify.dll next to the test assembly at {baselineVerifyDll} (ProjectReference build output).");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(baselineVerifyDll);
        startInfo.ArgumentList.Add("--diff-scope");
        startInfo.ArgumentList.Add(oldDir);
        startInfo.ArgumentList.Add(newDir);
        startInfo.ArgumentList.Add("--expected-scope");
        foreach (var glob in globs)
        {
            startInfo.ArgumentList.Add(glob);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var exited = process.WaitForExit(TimeSpan.FromMinutes(2));

        Assert.True(exited, $"BaselineVerify did not exit within 2 minutes. stdout:\n{stdout}\nstderr:\n{stderr}");
        return (process.ExitCode, stdout, stderr);
    }
}
