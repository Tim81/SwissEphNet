using System.Diagnostics;
using Xunit;

namespace BaselineVerify.Tests;

/// <summary>
/// Program.cs is deliberately untested by design elsewhere in this project (see
/// Tools/BaselineGen/README.md's "BaselineVerify.Tests" bullet): every decision function it
/// calls -- Comparer, Verdict, RowCounts, ScopeDiff, Waivers, Cli -- is unit tested directly,
/// with genuine negative cases, precisely so that logic is reachable from tests without
/// spinning up the process. But nothing before this test ever ran the composed program end to
/// end, which means Program.cs's own wiring of those results into an exit code was never
/// actually exercised: `passed = verdict.Passed &amp;&amp; rowCountVerdict.Passed` could be
/// changed to `||`, or any of the `overallExitCode = 1` assignments this file has (the
/// assembly-identity check, the orphaned-file/orphaned-row-count-entry loops, the row-count
/// mismatch branch) could be deleted outright, and every one of the other ~136 tests in this
/// project would stay green, because none of them ever call into Program.cs itself.
///
/// This test closes that gap, the cheapest way that still proves something real: it copies
/// the actual committed baseline directory, corrupts one area's expected row count in the
/// copy (not a TSV row -- see below for why), runs the real, compiled BaselineVerify.dll
/// against the copy as a subprocess, and asserts the process actually exits nonzero. A
/// passing run here means the composition from "one area's numbers don't add up" all the way
/// to "the process reports failure" is intact, not just that the pieces individually work.
/// </summary>
public class ProgramEndToEndTests
{
    [Fact]
    public void Main_CorruptedRowCount_ExitsNonZero()
    {
        var repoRoot = FindRepoRoot();
        var realBaselineDir = Path.Combine(repoRoot, "Tests", "baseline");
        Assert.True(Directory.Exists(realBaselineDir), $"Expected a real baseline directory at {realBaselineDir}.");

        var fixtureDir = Path.Combine(Path.GetTempPath(), "BaselineVerify.EndToEndTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            foreach (var file in Directory.EnumerateFiles(realBaselineDir))
            {
                File.Copy(file, Path.Combine(fixtureDir, Path.GetFileName(file)));
            }

            // Corrupt row-counts.tsv's "misc" entry, not any baseline-*.tsv row: misc is the
            // smallest area (34 rows) and this way the test asserts against the row-count
            // manifest contract RowCounts.cs/Verdict.cs already define, not against any
            // specific case id or numeric value the matrix happens to produce today -- a
            // hand-picked TSV row would break the moment that row's legitimate content ever
            // changes, for reasons having nothing to do with what this test is checking.
            var rowCountsPath = Path.Combine(fixtureDir, RowCounts.FileName);
            var lines = File.ReadAllLines(rowCountsPath);
            var corruptedLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("misc\t", StringComparison.Ordinal))
                {
                    corruptedLine = i;
                    break;
                }
            }
            Assert.True(corruptedLine >= 0, $"Expected a 'misc' row in {rowCountsPath}.");
            var expectedCount = int.Parse(lines[corruptedLine].Split('\t')[1]);
            lines[corruptedLine] = $"misc\t{expectedCount + 1}";
            File.WriteAllLines(rowCountsPath, lines);

            // BaselineVerify.dll is a ProjectReference of this test project (see
            // BaselineVerify.Tests.csproj), so its build output -- and waivers.tsv, which its
            // own Program.cs looks up relative to its own AppContext.BaseDirectory -- are
            // already copied next to this test assembly.
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
            startInfo.ArgumentList.Add(fixtureDir);

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            var exited = process.WaitForExit(TimeSpan.FromMinutes(5));

            Assert.True(exited, $"BaselineVerify did not exit within 5 minutes. stdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("misc", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    /// <summary>
    /// Walks up from the test assembly's own directory to find SwissEphNet.sln, the same
    /// technique Program.cs's own DiscoverBaselineDir uses to locate Tests/baseline when no
    /// directory is given explicitly -- duplicated here rather than reused because that
    /// method is private to Program.cs's top-level statements and not otherwise reachable.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SwissEphNet.sln")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "Could not find SwissEphNet.sln by walking up from the test assembly's directory.");
        return dir!.FullName;
    }
}
