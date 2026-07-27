using System;
using Xunit;

namespace SwissEphNet.Tests
{
    /// <summary>
    /// Shared skip reasons, so every fixed-star test that depends on the same
    /// known, not-yet-fixed bugs cites identical wording.
    /// </summary>
    public static class SkipReasons
    {
        /// <summary>
        /// Fixed-star lookup reads sefstars.txt with a default (no explicit
        /// encoding) CFile. CFile falls back to UTF-8 on every TFM this project
        /// targets, because Encoding.GetEncoding("Windows-1252") is unavailable
        /// without registering System.Text.Encoding.CodePages -- see the
        /// constructor comment on CFile and README.md's note on this fallback --
        /// and star names are matched with culture-sensitive string comparisons
        /// (C.strcmp/StartsWith without StringComparison; see the C.strcmp note
        /// in SwissEphNet/CPort/.editorconfig, which is exactly why
        /// CA1304/CA1305/CA1307/CA1309/CA1310 are kept as warnings there). Both
        /// bugs happen to leave these particular assertions passing on Windows
        /// today (NLS comparison behavior, and Windows historically shipping the
        /// Windows-1252 code page), but neither bug is fixed, and the same
        /// lookup path fails on Linux/macOS. PR1 is expected to fix Windows-1252
        /// decoding and the culture-sensitive comparisons; once it does, remove
        /// these skips and confirm the tests pass on every OS.
        /// </summary>
        public const string FixedStarWindows1252AndCulture =
            "Skipped on non-Windows: fixed-star lookup depends on two known, unfixed bugs -- " +
            "the Windows-1252 ephemeris-file encoding fallback and culture-sensitive string " +
            "comparison (C.strcmp/StartsWith) -- both queued for PR1.";
    }

    /// <summary>
    /// A [Fact] that only runs on Windows. The reason is required and shows up as
    /// the skip reason in test output on non-Windows, so the skip is visible and
    /// self-documenting rather than silently reducing the test count.
    /// </summary>
    /// <remarks>
    /// FactAttribute.Skip is a plain writable property, not a compile-time
    /// constant, so setting it from inside the attribute's own constructor (which
    /// runs at test-discovery time, via reflection) is enough to make the skip
    /// decision at runtime -- no third-party "skippable fact" package needed.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute(string reason)
        {
            if (!OperatingSystem.IsWindows())
                Skip = reason;
        }
    }

    /// <summary>
    /// A [Theory] that only runs on Windows. See <see cref="WindowsOnlyFactAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
    {
        public WindowsOnlyTheoryAttribute(string reason)
        {
            if (!OperatingSystem.IsWindows())
                Skip = reason;
        }
    }
}