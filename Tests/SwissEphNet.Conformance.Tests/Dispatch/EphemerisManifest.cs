using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Asserts that <see cref="RepoLocator.EpheDir"/> (or its
/// <c>SWISSEPH_CONFORMANCE_EPHE</c> override) contains exactly the files
/// <c>Tests/conformance/required-ephemeris-files.tsv</c> declares -- no fewer, no more.
/// </summary>
/// <remarks>
/// Exists because "no fewer" alone is not enough: a plain, non-sparse
/// <c>git submodule update --init external/swisseph</c> pulls every era file this
/// upstream ships (378 MB, not the declared ~4.2 MB core set), and several iterations
/// (suite 5 testcase 3 among them -- see <see cref="EphemerisFileResolver.NeedsEraFileWeDoNotShip"/>)
/// only pass because a file the manifest does not declare happens to be present. Running
/// the oracle, or worse, regenerating <c>known-fail.tsv</c>, against that tree produces a
/// list that looks right locally and is wrong for CI and every other contributor -- exactly
/// what happened once already. Checking only for missing files would not have caught it: every
/// declared file was present, plus 150 more nobody asked for.
/// </remarks>
public static class EphemerisManifest
{
    public static IReadOnlyList<string> Load(string manifestPath)
    {
        var result = new List<string>();
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            result.Add(trimmed);
        }

        // Mirrors scripts/run-oracle-dump.ps1:361-363 and scripts/verify-swetest-diff.ps1's own
        // Assert-EphemerisManifest, both of which Fail on $required.Count -eq 0. Without this, a
        // manifest that parsed to zero required files (a bad rewrite, an accidentally emptied
        // file, comment lines with no data rows left) makes Check's Missing/Extra both trivially
        // satisfiable and AssertMatches() a silent no-op.
        if (result.Count == 0)
        {
            throw new InvalidOperationException($"'{manifestPath}' parsed to zero required files.");
        }

        return result;
    }

    /// <summary>
    /// Compares the manifest against what is actually on disk in <paramref name="epheDir"/>.
    /// Only top-level files are considered (matching the declared set, which is flat) --
    /// a subdirectory like ephe/sat/ is reported as a single extra entry ("sat/") rather than
    /// walked recursively, since its mere presence is already the finding.
    /// </summary>
    public static EphemerisManifestResult Check(IReadOnlyList<string> required, string epheDir)
    {
        // scripts/run-oracle-dump.ps1:363-365 and scripts/verify-swetest-diff.ps1's own
        // Assert-EphemerisManifest both Fail outright on a missing directory, as a check
        // independent of what $required contains. Folding a missing directory into
        // "Missing = required" instead is not equivalent: paired with an empty (unguarded)
        // required list it makes Missing.Count == 0 and Extra.Count == 0 both true, so
        // AssertMatches() returns silently -- Load's non-emptiness check above closes that
        // specific combination, but a missing directory is wrong regardless of what required
        // contains, and deserves its own loud failure rather than being folded into that
        // comparison's result at all.
        if (!Directory.Exists(epheDir))
        {
            throw new InvalidOperationException(
                $"'{epheDir}' does not exist. Run the sparse-checkout recipe in CONTRIBUTING.md's " +
                "\"The upstream C is vendored at external/swisseph\" section.");
        }

        // Ordinal, not OrdinalIgnoreCase: the file system this runs on can be case-sensitive
        // (Linux CI) even though the manifest and this code are usually exercised on Windows
        // first. "SEPL_18.SE1" on disk matching a manifest entry "sepl_18.se1" via
        // OrdinalIgnoreCase would pass locally on Windows and then report both a false "missing"
        // and a false "extra" on Linux, which is exactly the platform-only failure this repo's
        // dual-OS CI matrix exists to catch, not to reproduce here.
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(epheDir))
        {
            var name = Path.GetFileName(entry);
            present.Add(Directory.Exists(entry) ? name + "/" : name);
        }

        var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
        var missing = required.Where(r => !present.Contains(r)).ToList();
        var extra = present.Where(p => !requiredSet.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        return new EphemerisManifestResult(epheDir, missing, extra);
    }

    /// <summary>
    /// Loads the manifest from <see cref="RepoLocator.ConformanceDataDir"/> and checks it
    /// against <see cref="RepoLocator.EpheDir"/>, throwing with a message naming the exact
    /// difference if they do not match exactly.
    /// </summary>
    public static void AssertMatches()
    {
        var manifestPath = Path.Combine(RepoLocator.ConformanceDataDir, "required-ephemeris-files.tsv");
        var required = Load(manifestPath);
        var result = Check(required, RepoLocator.EpheDir);

        if (result.Missing.Count == 0 && result.Extra.Count == 0)
        {
            return;
        }

        var message = new System.Text.StringBuilder();
        message.AppendLine($"external/swisseph/ephe ({result.EpheDir}) does not match the declared ephemeris file set " +
                            $"({manifestPath}).");
        if (result.Missing.Count > 0)
        {
            message.AppendLine($"Missing ({result.Missing.Count}): {string.Join(", ", result.Missing)}");
            message.AppendLine("Fetch the declared sparse core set -- see CONTRIBUTING.md's \"The upstream C is vendored at external/swisseph\".");
        }

        if (result.Extra.Count > 0)
        {
            message.AppendLine($"Extra ({result.Extra.Count}): {string.Join(", ", result.Extra.Take(20))}" +
                                (result.Extra.Count > 20 ? $", ... and {result.Extra.Count - 20} more" : ""));
            message.AppendLine("This usually means the submodule was checked out with a plain " +
                                "'git submodule update --init' instead of the sparse recipe in CONTRIBUTING.md -- " +
                                "that pulls every era file (378 MB) instead of the declared ~4.2 MB core set, and " +
                                "some iterations only pass because of a file the manifest does not declare. Reset " +
                                "the sparse-checkout patterns (git -C external/swisseph sparse-checkout reapply) " +
                                "rather than adding the extra files to the manifest, unless you are deliberately " +
                                "changing what this repo declares as its data set.");
        }

        throw new InvalidOperationException(message.ToString());
    }
}

public sealed record EphemerisManifestResult(string EpheDir, IReadOnlyList<string> Missing, IReadOnlyList<string> Extra);
