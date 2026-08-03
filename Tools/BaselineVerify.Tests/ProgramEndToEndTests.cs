using System.Diagnostics;
using BaselineMatrix;
using Xunit;

namespace BaselineVerify.Tests;

/// <summary>
/// Program.cs is deliberately untested by design elsewhere in this project (see
/// Tools/BaselineGen/README.md's "BaselineVerify.Tests" bullet): every decision function it
/// calls -- Comparer, Verdict, RowCounts, ScopeDiff, Waivers, Cli -- is unit tested directly,
/// with genuine negative cases, precisely so that logic is reachable from tests without
/// spinning up the process. But nothing before this file existed ever ran the composed program
/// end to end, which means Program.cs's own wiring of those results into an exit code was never
/// actually exercised.
///
/// Each test here copies the real committed baseline directory, corrupts the copy one specific
/// way, runs the real, compiled BaselineVerify.dll against the copy as a subprocess, and asserts
/// on the specific failure text that mutation should produce -- not just "the process exited
/// nonzero" (that alone would still pass if a completely unrelated FAIL fired instead of the one
/// the test means to exercise).
///
/// Coverage, against every `overallExitCode = 1` assignment / exit-code-affecting branch in
/// Program.cs's main verify path:
///
///   - Program.cs:116 (assembly-identity check)         -- COVERED: Main_MatchingAssemblyIdentitySidecar_ExitsNonZero
///   - Program.cs:127 (orphaned baseline file loop)      -- COVERED: Main_StrayBaselineFile_ExitsNonZero
///   - Program.cs:135 (orphaned row-count entry loop)    -- COVERED: Main_StrayRowCountEntry_ExitsNonZero
///   - Program.cs:152 (missing baseline file)            -- COVERED: Main_MissingBaselineFile_ExitsNonZero
///   - Program.cs:169 (`verdict.Passed &amp;&amp; rowCountVerdict.Passed`) -- COVERED: Main_CorruptedRowCount_ExitsNonZero
///   - Program.cs:176 (`overallExitCode = 1` for a failed area) -- COVERED: Main_CorruptedRowCount_ExitsNonZero
///     (both :169 and :176 are reached by the same fixture: a corrupted row-count entry is the
///     cheapest failure that forces `passed` false and then requires the assignment at :176 to
///     actually propagate it, so mutating either one independently makes this one test fail)
///   - Program.cs:209 (per-area exception catch)         -- COVERED: Main_DuplicateCaseId_ReportsErrorAndExitsNonZero
///   - Program.cs:228 (stale-waiver check)                -- NOT COVERED, deliberately, not by oversight.
///     Waivers.tsv is loaded from `Path.Combine(AppContext.BaseDirectory, "waivers.tsv")`
///     (Program.cs, near the top of the verify path) -- a path fixed to wherever
///     BaselineVerify.dll itself was built, never to the baseline-directory argument these
///     fixtures manipulate. A fixture copy under a temp directory has no way to change which
///     waivers.tsv the subprocess loads, and the committed Tests/baseline/waivers.tsv is
///     currently empty besides. Reaching :228 from an end-to-end test would require either
///     editing the committed waivers file (out of scope for a test fixture, and this project is
///     told not to touch Tests/baseline/ contents) or overwriting BaselineVerify's own build
///     output copy of waivers.tsv in place, which is fragile (silently reset by the next
///     rebuild) and not worth the coupling. `Verdict.ForWaiver`'s own unit tests in
///     VerdictTests.cs already exercise the staleness decision directly; what stays unverified
///     here is only Program.cs's wiring of that decision into `overallExitCode`, same class of
///     gap :169/:176 close for the row-count branch.
///
/// Every one of the above was confirmed by mutation, not by inspection: break the line, run the
/// specific test that claims to cover it, watch it fail with the corruption-fixture's own
/// assertion (not just a changed exit code), then restore the line and watch the test pass
/// again.
///
/// Every [Fact] below carries an explicit Timeout (xunit.v3-only), set to 330 seconds -- just
/// above RunBaselineVerify's own 5-minute Process.WaitForExit bound, so a genuinely wedged
/// subprocess still surfaces through that method's own "did not exit" assertion (with stdout/
/// stderr attached) before xunit's own timeout would otherwise cut it off with a bare "test
/// exceeded timeout" and no diagnostic content.
/// </summary>
public class ProgramEndToEndTests
{
    [Fact(Timeout = 330_000)]
    public void Main_CorruptedRowCount_ExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // Corrupt row-counts.tsv's "misc" entry, not any baseline-*.tsv row: misc is the
            // smallest area (34 rows) and this way the test asserts against the row-count
            // manifest contract RowCounts.cs/Verdict.cs already define, not against any
            // specific case id or numeric value the matrix happens to produce today -- a
            // hand-picked TSV row would break the moment that row's legitimate content ever
            // changes, for reasons having nothing to do with what this test is checking.
            var rowCountsPath = Path.Combine(fixtureDir, RowCounts.FileName);
            var lines = File.ReadAllLines(rowCountsPath);
            var corruptedLine = Array.FindIndex(lines, l => l.StartsWith("misc\t", StringComparison.Ordinal));
            Assert.True(corruptedLine >= 0, $"Expected a 'misc' row in {rowCountsPath}.");
            var expectedCount = int.Parse(lines[corruptedLine].Split('\t')[1]);
            lines[corruptedLine] = $"misc\t{expectedCount + 1}";
            File.WriteAllLines(rowCountsPath, lines);

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            // Not "misc" alone (that substring appears in every run's status table whether misc
            // passes or fails) -- the row-count-mismatch reason line Verdict.RowCountMismatch
            // produces, which only appears when the mismatch this fixture created actually
            // reached the report.
            Assert.Contains("FAIL misc: row count", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact(Timeout = 330_000)]
    public void Main_StrayBaselineFile_ExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // A baseline-*.tsv with no matching Areas.All entry: the shape FindOrphanedBaselineFiles
            // (Program.cs:121, checked at :127) exists to catch -- an area renamed or removed from
            // Areas.All left a TSV behind, or a new area's file landed before Areas.All registered it.
            File.WriteAllText(Path.Combine(fixtureDir, "baseline-bogus.tsv"), "BOGUS|1\tvalue\n");

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("baseline-bogus.tsv", stdout, StringComparison.Ordinal);
            Assert.Contains("does not correspond to any area in Tools/BaselineMatrix/Areas.cs's Areas.All", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact(Timeout = 330_000)]
    public void Main_StrayRowCountEntry_ExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // Mirrors Main_StrayBaselineFile_ExitsNonZero on the manifest side: an entry in
            // row-counts.tsv for an area name Areas.All does not know, the shape
            // FindOrphanedRowCountEntries (Program.cs:130, checked at :135) exists to catch --
            // exactly what a bare "ROWCOUNT <name> 0" line for a deleted area (finding 1's bug)
            // would leave behind if it made it into a real regeneration.
            var rowCountsPath = Path.Combine(fixtureDir, RowCounts.FileName);
            File.AppendAllText(rowCountsPath, "totallybogus\t5\n");

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("totallybogus", stdout, StringComparison.Ordinal);
            Assert.Contains("does not correspond to any area in Tools/BaselineMatrix/Areas.cs's Areas.All", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact(Timeout = 330_000)]
    public void Main_MissingBaselineFile_ExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // "misc" still has a row-counts.tsv entry and is still in Areas.All -- only its
            // baseline-misc.tsv is gone, isolating Verdict.MissingBaselineFile (Program.cs:150,
            // checked at :152) from the orphaned-entry checks the two tests above exercise.
            var baselinePath = Path.Combine(fixtureDir, "baseline-misc.tsv");
            Assert.True(File.Exists(baselinePath), $"Expected {baselinePath} to exist before deleting it.");
            File.Delete(baselinePath);

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("no committed baseline file at", stdout, StringComparison.Ordinal);
            Assert.Contains("baseline-misc.tsv", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact(Timeout = 330_000)]
    public void Main_DuplicateCaseId_ReportsErrorAndExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // Comparer.Index (Comparer.cs) throws InvalidOperationException on a duplicate case
            // id within one side's rows; Program.cs's per-area try/catch (the catch itself at
            // :204, the exit-code assignment at :209) is what stands between that throw and the
            // rest of the areas never being checked at all. Duplicating the file's own first
            // line is enough -- the exact case id or value is irrelevant to what this fixture
            // needs, only that it repeats.
            var baselinePath = Path.Combine(fixtureDir, "baseline-misc.tsv");
            var lines = File.ReadAllLines(baselinePath);
            Assert.True(lines.Length > 0, $"Expected at least one row in {baselinePath}.");
            File.AppendAllText(baselinePath, lines[0] + Environment.NewLine);

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("ERROR  misc", stdout, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", stdout, StringComparison.Ordinal);
            Assert.Contains("Duplicate case id", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact(Timeout = 330_000)]
    public void Main_MatchingAssemblyIdentitySidecar_ExitsNonZero()
    {
        var fixtureDir = CopyRealBaselineFixture();
        try
        {
            // CheckAssemblyIdentity (Program.cs:114, failure path at :116) is meant to catch
            // BaselineVerify somehow running against the reference-mode assembly instead of the
            // in-repo one it is supposed to verify. To exercise the FAIL branch honestly, the
            // sidecar has to describe the CURRENT build, not a hand-written fake GUID --
            // EnvInfo.Describe() here and the BaselineVerify.dll subprocess both resolve
            // SwissEphNet.dll from the same solution build (this test project's own
            // ProjectReference chain runs through BaselineVerify -> BaselineMatrix ->
            // SwissEphNet, exactly like the subprocess's), so the ModuleVersionId this test
            // captures is the same one the subprocess will compute for itself.
            var sidecarPath = Path.Combine(fixtureDir, EnvInfo.SidecarFileName);
            File.WriteAllText(sidecarPath, EnvInfo.Describe());

            var (exitCode, stdout, _) = RunBaselineVerify(fixtureDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("FAIL assembly-identity check: current ModuleVersionId matches", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    /// <summary>
    /// Copies every file from the real committed Tests/baseline/ into a fresh temp directory so
    /// a test can corrupt the copy without touching the source of truth. Caller owns cleanup
    /// (delete the returned directory when done).
    /// </summary>
    private static string CopyRealBaselineFixture()
    {
        var repoRoot = FindRepoRoot();
        var realBaselineDir = Path.Combine(repoRoot, "Tests", "baseline");
        Assert.True(Directory.Exists(realBaselineDir), $"Expected a real baseline directory at {realBaselineDir}.");

        var fixtureDir = Path.Combine(Path.GetTempPath(), "BaselineVerify.EndToEndTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        foreach (var file in Directory.EnumerateFiles(realBaselineDir))
        {
            File.Copy(file, Path.Combine(fixtureDir, Path.GetFileName(file)));
        }
        return fixtureDir;
    }

    /// <summary>
    /// Runs the real, compiled BaselineVerify.dll (a ProjectReference build output sitting next
    /// to this test assembly, see BaselineVerify.Tests.csproj) as a subprocess against
    /// <paramref name="baselineDir"/> and returns its exit code and captured output.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunBaselineVerify(string baselineDir)
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
        startInfo.ArgumentList.Add(baselineDir);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var exited = process.WaitForExit(TimeSpan.FromMinutes(5));

        Assert.True(exited, $"BaselineVerify did not exit within 5 minutes. stdout:\n{stdout}\nstderr:\n{stderr}");
        return (process.ExitCode, stdout, stderr);
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
