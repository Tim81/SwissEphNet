using System;
using System.IO;
using Xunit;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Covers the two floors <see cref="EphemerisManifest"/> was missing relative to its own
/// PowerShell clones (scripts/run-oracle-dump.ps1's and scripts/verify-swetest-diff.ps1's
/// Assert-EphemerisManifest, both of which Fail on a zero-length required list and on a missing
/// directory), and the switch from OrdinalIgnoreCase to Ordinal name comparison.
/// </summary>
public class EphemerisManifestTests
{
    [Fact]
    public void Load_EmptyManifest_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var manifestPath = Path.Combine(dir, "required-ephemeris-files.tsv");
            // Only comments and blank lines -- parses to zero required files, the shape a
            // bad rewrite or an accidentally emptied file would produce.
            File.WriteAllText(manifestPath, "# comment only\n\n   \n");

            var ex = Assert.Throws<InvalidOperationException>(() => EphemerisManifest.Load(manifestPath));
            Assert.Contains("zero required files", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_NonEmptyManifest_ReturnsEntries()
    {
        var dir = NewTempDir();
        try
        {
            var manifestPath = Path.Combine(dir, "required-ephemeris-files.tsv");
            File.WriteAllText(manifestPath, "# comment\nsepl_18.se1\nsefstars.txt\n\n");

            var required = EphemerisManifest.Load(manifestPath);

            Assert.Equal(["sepl_18.se1", "sefstars.txt"], required);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_MissingDirectory_Throws()
    {
        var dir = NewTempDir();
        Directory.Delete(dir); // exists on disk for NewTempDir's bookkeeping, then removed here
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => EphemerisManifest.Check(["sepl_18.se1"], dir));
            Assert.Contains(dir, ex.Message, StringComparison.Ordinal);
            Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            // Already removed; nothing to clean up, but guard against the test itself creating it.
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Check_DirectoryMatchesRequiredSet_NoMissingOrExtra()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "sepl_18.se1"), "x");
            File.WriteAllText(Path.Combine(dir, "sefstars.txt"), "x");

            var result = EphemerisManifest.Check(["sepl_18.se1", "sefstars.txt"], dir);

            Assert.Empty(result.Missing);
            Assert.Empty(result.Extra);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // scripts/run-oracle-dump.ps1's and scripts/verify-swetest-diff.ps1's Assert-EphemerisManifest
    // both fold case with ToLowerInvariant before comparing, matching OrdinalIgnoreCase -- but this
    // repo's dual-OS CI matrix means the actual file system underneath can be case-sensitive
    // (Linux) even though this is normally exercised on a case-insensitive one (Windows) first.
    // "SEPL_18.SE1" on disk must show up as both a genuine miss (the required lower-case name is
    // absent) and a genuine extra (an unrecognised upper-case name is present) -- exactly what a
    // Linux checkout with the wrong case would report, and exactly what OrdinalIgnoreCase would
    // silently absorb.
    [Fact]
    public void Check_IsCaseSensitive_UppercaseFileIsBothMissingAndExtra()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "SEPL_18.SE1"), "x");

            var result = EphemerisManifest.Check(["sepl_18.se1"], dir);

            Assert.Equal(["sepl_18.se1"], result.Missing);
            Assert.Equal(["SEPL_18.SE1"], result.Extra);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Check's own remarks say a subdirectory is reported as a single extra entry ("sat/",
    // trailing slash) rather than walked recursively -- that trailing slash is what makes a
    // directory sharing a *name* with a required file (e.g. a stray "sepl_18.se1/" left behind
    // by some other tool) show up as its own, distinct Extra entry rather than silently
    // satisfying the Missing check. Removing the "/" in Check's `Directory.Exists(entry) ?
    // name + "/" : name` line would collapse that directory's `present` key onto the plain file
    // name, and the required file would incorrectly read as present.
    [Fact]
    public void Check_DirectoryNamedLikeARequiredFile_IsReportedAsBothMissingAndExtra()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "sepl_18.se1"));

            var result = EphemerisManifest.Check(["sepl_18.se1"], dir);

            // The required *file* is still missing -- a directory of the same name is not the
            // file the manifest declared.
            Assert.Equal(["sepl_18.se1"], result.Missing);
            // ...and the directory itself is reported as an extra entry, with the trailing "/"
            // that distinguishes it from the plain file name a "flattened" (no trailing slash)
            // comparison would have silently matched against.
            Assert.Equal(["sepl_18.se1/"], result.Extra);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ephemeris-manifest-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
