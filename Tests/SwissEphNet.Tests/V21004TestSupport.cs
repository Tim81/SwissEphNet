using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Locates the ephemeris files of the external/swisseph submodule (sepl_*, semo_*, seas_*,
    /// seorbel.txt), the way Tests/SwissEphNet.Conformance.Tests/RepoLocator.cs does: walk up from
    /// the test binary to external/swisseph/ephe, or take SWISSEPH_CONFORMANCE_EPHE.
    /// </summary>
    internal static class RepoEphe
    {
        private static readonly string[] Needed =
        {
            "sepl_12.se1", "sepl_18.se1", "semo_12.se1", "semo_18.se1", "seas_18.se1", "seorbel.txt"
        };

        /// <summary>The ephe directory, or null if the submodule is not checked out.</summary>
        public static string Dir { get; } = Locate();

        public static string SkipReason =>
            Dir == null
                ? "external/swisseph/ephe (sepl_12/18, semo_12/18, seas_18, seorbel.txt) not found; " +
                  "run 'git submodule update --init external/swisseph' or set SWISSEPH_CONFORMANCE_EPHE"
                : null;

        private static string Locate()
        {
            string over = Environment.GetEnvironmentVariable("SWISSEPH_CONFORMANCE_EPHE");
            if (!string.IsNullOrEmpty(over))
                return Complete(over) ? over : null;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string cand = Path.Combine(dir.FullName, "external", "swisseph", "ephe");
                if (Complete(cand))
                    return cand;
                dir = dir.Parent;
            }
            return null;
        }

        private static bool Complete(string dir)
        {
            foreach (var f in Needed)
                if (!File.Exists(Path.Combine(dir, f)))
                    return false;
            return true;
        }

        /// <summary>A SwissEph reading the ephemeris files straight from <see cref="Dir"/>.</summary>
        public static SwissEph Open()
        {
            var swe = new SwissEph();
            swe.FileProvider = new DelegateFileProvider(path =>
                File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
            swe.swe_set_ephe_path(Dir);
            return swe;
        }
    }

    /// <summary>[Fact] that is skipped when the submodule's ephemeris files are not available.</summary>
    internal sealed class EpheFactAttribute : FactAttribute
    {
        public EpheFactAttribute() { Skip = RepoEphe.SkipReason; }
    }

    /// <summary>[Theory] that is skipped when the submodule's ephemeris files are not available.</summary>
    internal sealed class EpheTheoryAttribute : TheoryAttribute
    {
        public EpheTheoryAttribute() { Skip = RepoEphe.SkipReason; }
    }

    /// <summary>
    /// Compares against values printed (%.17g) by Astrodienst's C built with MSVC (/O2 /fp:precise /MD) on
    /// Windows x64: exact there, a small tolerance elsewhere where libm may differ in the last ulp.
    /// </summary>
    internal static class CReference
    {
        public static bool ExactPlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64;

        public static void Equal(double expected, double actual, double tolerance, string what = null)
        {
            if (ExactPlatform)
                Assert.True(expected.Equals(actual), $"{what}: expected {expected:R}, actual {actual:R}");
            else
                Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected:R}, actual {actual:R}");
        }
    }
}
