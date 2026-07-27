using BaselineMatrix;

namespace BaselineVerify;

/// <summary>Pass/fail for one area, with every reason it failed (there can be more than one).</summary>
internal readonly record struct AreaVerdict(bool Passed, IReadOnlyList<string> Reasons)
{
    public static AreaVerdict Pass() => new(true, []);
    public static AreaVerdict Fail(params string[] reasons) => new(false, reasons);
}

/// <summary>Whether a waiver is stale (not earning its keep) and, if so, why.</summary>
internal readonly record struct WaiverVerdict(bool Stale, string? Reason);

internal enum MvidCheckOutcome
{
    /// <summary>No sidecar file content was available to check against.</summary>
    Skipped,

    /// <summary>Sidecar was present but did not contain a parseable SwissEphModuleVersionId= line.</summary>
    Unparseable,

    /// <summary>Current build's ModuleVersionId matches the committed reference's.</summary>
    Matches,

    /// <summary>Current build's ModuleVersionId differs from the committed reference's (the expected, healthy case).</summary>
    Differs,
}

/// <summary>Result of comparing the currently running assembly's identity against whatever the committed reference-mode sidecar recorded.</summary>
internal readonly record struct AssemblyIdentityVerdict(MvidCheckOutcome MvidOutcome, bool Sha256Comparable, bool Sha256Matches)
{
    /// <summary>
    /// True only when there is positive, hard evidence that the currently running
    /// build is the same assembly the reference-mode sidecar recorded -- i.e. local
    /// mode is, for some reason, not actually running local code. This is deliberately
    /// narrow: Skipped and Unparseable are "we could not check", not "we checked and
    /// it's fine", so they must never be folded into "not a match".
    /// </summary>
    public bool IsSuspiciousMatch => MvidOutcome == MvidCheckOutcome.Matches || Sha256Matches;
}

/// <summary>
/// Every PASS/FAIL policy decision BaselineVerify makes, pulled out of Program.cs so
/// it can be unit tested directly instead of only through a full end-to-end run
/// against real matrix data. Program.cs should contain orchestration (read files, call
/// Comparer, call this, print the result) and nothing that itself needs a test.
/// </summary>
internal static class Verdict
{
    /// <summary>
    /// Cap on WaivedFraction (failures actually absorbed) and, separately, on
    /// MatchedFraction (rows touched by a waiver at all, regardless of outcome). Both
    /// matter: WaivedFraction alone would let a broad glob sit quietly over an area as
    /// long as few of the rows it touches happen to be failing today -- MatchedFraction
    /// catches that a glob is touching a lot of rows before any of them actually
    /// regress.
    /// </summary>
    public const double MaxWaivedFraction = 0.05;
    public const double MaxMatchedFraction = 0.05;

    public static AreaVerdict ForArea(CompareResult result)
    {
        var reasons = new List<string>();

        if (result.Fail > 0)
        {
            reasons.Add($"{result.Fail} row(s) beyond tolerance, exact-mismatch, or arity change");
        }
        if (result.OnlyLocal > 0)
        {
            reasons.Add($"{result.OnlyLocal} row(s) present only in the local run");
        }
        if (result.OnlyReference > 0)
        {
            reasons.Add($"{result.OnlyReference} row(s) present only in the committed baseline");
        }
        if (!double.IsNaN(result.WaivedFraction) && result.WaivedFraction > MaxWaivedFraction)
        {
            reasons.Add(
                $"waived fraction {result.WaivedFraction:P1} exceeds the {MaxWaivedFraction:P0} cap " +
                $"({result.Waived} of {result.Total} rows had a failure excused by a waiver)");
        }
        if (!double.IsNaN(result.MatchedFraction) && result.MatchedFraction > MaxMatchedFraction)
        {
            reasons.Add(
                $"waiver match breadth {result.MatchedFraction:P1} exceeds the {MaxMatchedFraction:P0} cap " +
                $"({result.MatchedByAnyWaiver} of {result.Total} rows touched by a waiver, regardless of outcome)");
        }

        return reasons.Count == 0 ? AreaVerdict.Pass() : new AreaVerdict(false, reasons);
    }

    public static AreaVerdict MissingBaselineFile(string path) =>
        AreaVerdict.Fail($"no committed baseline file at {path}");

    public static WaiverVerdict ForWaiver(Waiver waiver, WaiverStats stats)
    {
        if (stats.Matched == 0)
        {
            return new WaiverVerdict(true, $"waiver \"{waiver.Glob}\" matched zero rows. Remove it.");
        }
        if (stats.Waived == 0)
        {
            return new WaiverVerdict(
                true,
                $"waiver \"{waiver.Glob}\" matched {stats.Matched} row(s) but never excused an actual failure " +
                "(every match was exact or within tolerance on its own). Remove it.");
        }
        return new WaiverVerdict(false, null);
    }

    /// <summary>
    /// Pure comparison logic: takes the sidecar file's text content (or null if the
    /// file does not exist) and the currently running build's identity, and decides
    /// what that means. All I/O (does the file exist, read its bytes) is the caller's
    /// job, specifically so this can be tested without touching disk.
    /// </summary>
    public static AssemblyIdentityVerdict CheckAssemblyIdentity(string? sidecarContent, Guid currentMvid, string currentSha256)
    {
        if (sidecarContent is null)
        {
            return new AssemblyIdentityVerdict(MvidCheckOutcome.Skipped, false, false);
        }

        var committedMvid = EnvInfo.ParseModuleVersionId(sidecarContent);
        if (committedMvid is null)
        {
            return new AssemblyIdentityVerdict(MvidCheckOutcome.Unparseable, false, false);
        }

        var committedSha256 = EnvInfo.ParseSha256(sidecarContent);
        var sha256Comparable = committedSha256 is not null;
        var sha256Matches = sha256Comparable && string.Equals(committedSha256, currentSha256, StringComparison.OrdinalIgnoreCase);
        var mvidOutcome = committedMvid.Value == currentMvid ? MvidCheckOutcome.Matches : MvidCheckOutcome.Differs;

        return new AssemblyIdentityVerdict(mvidOutcome, sha256Comparable, sha256Matches);
    }
}
