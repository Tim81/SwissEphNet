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

    public static AreaVerdict OrphanedBaselineFile(string path) =>
        AreaVerdict.Fail(
            $"{path} does not correspond to any area in Tools/BaselineMatrix/Areas.cs's Areas.All. " +
            "Either the area was renamed/removed and this file was left behind (delete it), or it is a " +
            "new area that still needs registering in Areas.All.");

    /// <summary>
    /// Given every file name actually present in the baseline directory (not pre-filtered --
    /// this includes the *.env.txt sidecar and anything else that happens to live there) and
    /// the area names BaselineMatrix currently knows about, returns the baseline-*.tsv file
    /// names with no corresponding area, sorted for stable output.
    ///
    /// Only names matching the baseline-*.tsv shape are ever candidates: this check exists to
    /// catch a *.tsv file abandoned by a removed/renamed Areas.All entry (see
    /// scripts/regenerate-baseline.ps1's own version of this same check, which is where this
    /// gap was first identified and closed on the generator side), not to police every file
    /// that happens to live in Tests/baseline/ -- the sidecar and any other legitimate
    /// non-tsv metadata file are silently ignored, not flagged.
    /// </summary>
    public static IReadOnlyList<string> FindOrphanedBaselineFiles(IEnumerable<string> presentFileNames, IEnumerable<string> knownAreaNames)
    {
        var known = knownAreaNames.Select(static n => $"baseline-{n}.tsv").ToHashSet(StringComparer.Ordinal);
        return presentFileNames
            .Where(static f => f.StartsWith("baseline-", StringComparison.Ordinal) && f.EndsWith(".tsv", StringComparison.Ordinal))
            .Where(f => !known.Contains(f))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static AreaVerdict OrphanedRowCountEntry(string area) =>
        AreaVerdict.Fail(
            $"row-counts.tsv has an entry for '{area}', which does not correspond to any area in " +
            "Tools/BaselineMatrix/Areas.cs's Areas.All. Either the area was renamed/removed and this " +
            "entry was left behind (delete it), or it is a new area that still needs registering in " +
            "Areas.All.");

    /// <summary>
    /// Mirrors <see cref="FindOrphanedBaselineFiles"/> for row-counts.tsv: a stale entry left
    /// behind for an area that no longer exists in Areas.All is silently ignored by every other
    /// check (the dictionary lookup in Program.cs's main loop is keyed by the areas BaselineMatrix
    /// currently knows about, so a row-counts.tsv entry for a retired area is simply never looked
    /// at). Closing this is the same class of gap that <c>FindOrphanedBaselineFiles</c> closed for
    /// baseline-*.tsv files, just on the manifest side rather than the data-file side.
    /// </summary>
    public static IReadOnlyList<string> FindOrphanedRowCountEntries(IEnumerable<string> presentAreaNames, IEnumerable<string> knownAreaNames)
    {
        var known = knownAreaNames.ToHashSet(StringComparer.Ordinal);
        return presentAreaNames
            .Where(a => !known.Contains(a))
            .OrderBy(static a => a, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every area must have a committed expected row count in Tests/baseline/row-counts.tsv
    /// (see RowCounts.cs). Missing entirely is its own failure, distinct from a mismatch,
    /// so a reviewer immediately knows whether the manifest was never written for this area
    /// (e.g. a newly added area that forgot to register it) versus written and then violated.
    /// </summary>
    public static AreaVerdict MissingRowCountEntry(string area) =>
        AreaVerdict.Fail(
            $"no entry for '{area}' in Tests/baseline/row-counts.tsv. Every area needs a committed " +
            "expected row count so a silently truncated or narrowed baseline file cannot pass as long " +
            "as FAIL/ONLY-LOCAL/ONLY-REFERENCE all read zero. scripts/regenerate-baseline.ps1 writes this " +
            "file's entries in the same pass as the TSVs; run it (with a correct -ExpectedScope) rather " +
            "than editing row-counts.tsv by hand.");

    public static AreaVerdict RowCountMismatch(string area, int expected, int actual) =>
        AreaVerdict.Fail(
            $"row count {actual} does not match the {expected} committed in Tests/baseline/row-counts.tsv. " +
            "A deliberate row-count change (a widened sweep, a new house system, a new area) must go " +
            "through scripts/regenerate-baseline.ps1, which rewrites the TSV and this manifest together, " +
            "under -ExpectedScope. If this change was not deliberate, the baseline file was edited, " +
            "truncated, or regenerated outside that path.");

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
