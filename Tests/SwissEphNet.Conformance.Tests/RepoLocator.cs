using System;
using System.IO;

namespace SwissEphNet.Conformance.Tests;

/// <summary>
/// Locates repo-relative inputs (the external/swisseph submodule and
/// Tests/conformance) from wherever the test binary happens to run from
/// (bin/Debug/net10.0/, a CI runner, etc.), with environment-variable
/// overrides for anyone who wants to point at a different checkout.
/// </summary>
public static class RepoLocator
{
    /// <summary>
    /// Root of the external/swisseph submodule (contains setest/, ephe/, *.c, *.h).
    /// Override with SWISSEPH_CONFORMANCE_SUBMODULE.
    /// </summary>
    public static string SubmoduleRoot { get; } = ResolveSubmoduleRoot();

    /// <summary>Directory containing t.exp / t.fix.</summary>
    public static string SetestDir => Path.Combine(SubmoduleRoot, "setest");

    /// <summary>
    /// Directory containing the core .se1 ephemeris files, sefstars.txt, seorbel.txt.
    /// Override with SWISSEPH_CONFORMANCE_EPHE.
    /// </summary>
    public static string EpheDir { get; } =
        Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_EPHE")
        ?? Path.Combine(ResolveSubmoduleRoot(), "ephe");

    /// <summary>
    /// Directory containing Tests/conformance (known-fail.tsv lives here).
    /// Override with SWISSEPH_CONFORMANCE_DIR.
    /// </summary>
    public static string ConformanceDataDir { get; } = ResolveConformanceDataDir();

    private static string ResolveSubmoduleRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_SUBMODULE");
        if (!string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }

        var found = WalkUpFor(Path.Combine("external", "swisseph", "setest", "t.exp"));
        if (found is null)
        {
            throw new InvalidOperationException(
                "Could not locate the external/swisseph submodule (looked for external/swisseph/setest/t.exp " +
                "in every parent directory of " + AppContext.BaseDirectory + "). " +
                "Run 'git submodule update --init external/swisseph', or set SWISSEPH_CONFORMANCE_SUBMODULE " +
                "to the submodule's root directory.");
        }

        return Path.GetFullPath(Path.Combine(found, "..", ".."));
    }

    private static string ResolveConformanceDataDir()
    {
        var overridePath = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_DIR");
        if (!string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }

        // The build copies known-fail.tsv next to the test binary (see the
        // project's <None Include> with Link), so prefer that -- it works even
        // when the source tree isn't reachable (e.g. a published test binary).
        var nextToBinary = Path.Combine(AppContext.BaseDirectory, "conformance");
        if (File.Exists(Path.Combine(nextToBinary, "known-fail.tsv")))
        {
            return nextToBinary;
        }

        var found = WalkUpFor(Path.Combine("Tests", "conformance", "known-fail.tsv"));
        if (found is null)
        {
            throw new InvalidOperationException(
                "Could not locate Tests/conformance/known-fail.tsv. Set SWISSEPH_CONFORMANCE_DIR to override.");
        }

        return found;
    }

    private static string? WalkUpFor(string relativeMarkerPath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativeMarkerPath);
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }

            dir = dir.Parent;
        }

        return null;
    }
}
