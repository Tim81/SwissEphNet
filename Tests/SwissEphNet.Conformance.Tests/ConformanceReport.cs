using System.Collections.Generic;
using System.Linq;
using SwissEphNet.Conformance.Tests.Dispatch;
using SwissEphNet.Conformance.Tests.KnownFail;

namespace SwissEphNet.Conformance.Tests;

public sealed record SuiteSummary(
    int SuiteId,
    string Description,
    int Total,
    int Passed,
    int NotImplemented,
    int DataMissing,
    int ValueMismatch,
    int Error,
    int Unreproducible)
{
    /// <summary>
    /// Iterations this suite actually ran a comparison for -- <c>Total</c> minus every
    /// category that means "never dispatched to the port at all": NOT-IMPLEMENTED (the
    /// port doesn't have the API yet), DATA-MISSING (a required data file isn't shipped),
    /// and UNREPRODUCIBLE (a structural C-vs-C# representational gap makes the reference
    /// call impossible to construct, distinct from the other two -- see
    /// <c>Suite06Houses.Dispatch</c>'s remarks for the shape this took historically).
    /// Passed and ValueMismatch/Error both count as dispatched: the port ran and produced
    /// an answer, right or wrong.
    /// </summary>
    public int Dispatched => Total - NotImplemented - DataMissing - Unreproducible;

    /// <summary>
    /// <c>Passed / Dispatched</c> -- the fraction of iterations this suite actually ran
    /// that matched the reference exactly (within <c>t.fix</c> tolerance). Excludes
    /// NOT-IMPLEMENTED, DATA-MISSING and UNREPRODUCIBLE from both sides of the ratio, so
    /// it does not reward a suite for having large swaths of it never attempted. As of the
    /// suite-6-testcase-6 fix (all five house entry points now have a faithful `int hsys`
    /// overload), UNREPRODUCIBLE is 0 across the whole corpus; this definition does not
    /// change now that the exclusion it names is usually vacuous, but keep it here rather
    /// than dropping the term, since a future structural gap in a different function would
    /// use the same category and the same exclusion again.
    /// </summary>
    public double PassRate => Dispatched == 0 ? 1.0 : (double)Passed / Dispatched;
}

/// <summary>A known-fail entry whose recorded category no longer matches what the port actually does now.</summary>
public sealed record CategoryDrift(IterationResult Result, FailureCategory RecordedCategory);

/// <summary>
/// One (suite, testcase) group's outcome breakdown -- see "Reporting by testcase" in
/// CONTRIBUTING.md. Exists because reporting 12,757 iteration rows is not something a
/// contributor can read; 60 testcase-level rows is. Two gates in this repo have opposite
/// expectations (the characterization baseline expects zero diffs, this oracle expects most
/// iterations to fail because the port is at 2.08 and the corpus is 2.10.03) and grouping by
/// testcase, split into <see cref="IsActionable"/> and parked, is what makes a red oracle run
/// legible instead of alarming: most testcases are expected to be parked on
/// NOT-IMPLEMENTED/DATA-MISSING, a handful carry the actual porting work queue.
/// </summary>
public sealed record TestCaseSummary(
    int Suite,
    int TestCase,
    string? Description,
    int Total,
    int Passed,
    int NotImplemented,
    int DataMissing,
    int ValueMismatch,
    int Error,
    int Unreproducible)
{
    /// <summary>
    /// True when at least one iteration in this testcase is VALUE-MISMATCH or ERROR --
    /// something a porting PR against the current 2.08 code could plausibly fix. False means
    /// every non-passing iteration here is NOT-IMPLEMENTED (2.10-only API), DATA-MISSING (a
    /// data file this repo does not ship), or UNREPRODUCIBLE (a structural gap) -- parked on
    /// something outside the scope of "fix the port's logic", not evidence of a bug.
    /// </summary>
    public bool IsActionable => ValueMismatch > 0 || Error > 0;
}

public sealed class ConformanceReport
{
    public required IReadOnlyList<IterationResult> All { get; init; }

    /// <summary>
    /// Failing now and either not on the known-fail list at all, or on it
    /// under a different category (see <see cref="Drifted"/>): a regression
    /// either way.
    /// </summary>
    public required IReadOnlyList<IterationResult> Regressions { get; init; }

    /// <summary>
    /// The subset of <see cref="Regressions"/> that *is* on the known-fail
    /// list, just recorded under a <see cref="FailureCategory"/> the current
    /// run no longer matches -- e.g. a VALUE-MISMATCH that degraded into an
    /// ERROR crash, or an ERROR that started reproducing as a VALUE-MISMATCH.
    /// Key-membership alone would let these through as "still failing, still
    /// on the list"; they are not the same failure.
    ///
    /// This is drift between <see cref="OutcomeKind"/>s only. There is no
    /// magnitude-based classification: a VALUE-MISMATCH stays a
    /// VALUE-MISMATCH (and so is invisible to <see cref="Drifted"/>) no
    /// matter how much the numeric diff grows from what known-fail.tsv's
    /// "reason" column happened to record when the row was generated --
    /// e.g. a 1e-9 mismatch widening to 1e-3 is not detected here. That
    /// column is a snapshot for a human reading the file, not a value this
    /// gate compares against on a later run.
    /// </summary>
    public required IReadOnlyList<CategoryDrift> Drifted { get; init; }

    /// <summary>On the known-fail list, but passing now: progress -- remove the entry.</summary>
    public required IReadOnlyList<KnownFailEntry> NewlyPassing { get; init; }

    /// <summary>On the known-fail list, but no longer present in the corpus at all.</summary>
    public required IReadOnlyList<KnownFailEntry> Stale { get; init; }

    public required IReadOnlyList<SuiteSummary> SuiteSummaries { get; init; }

    public required IReadOnlyList<TestCaseSummary> TestCaseSummaries { get; init; }

    /// <summary>
    /// The full contract: no regressions (new failures or category drift),
    /// nothing newly passing left un-pruned, and no stale rows left behind.
    /// Any of the three failing means known-fail.tsv and the port have
    /// diverged from each other in a way a reviewer needs to see as a diff.
    /// </summary>
    public bool Passed => Regressions.Count == 0 && NewlyPassing.Count == 0 && Stale.Count == 0;

    public static ConformanceReport Build(IReadOnlyList<IterationResult> results, IReadOnlyDictionary<IterationKey, KnownFailEntry> knownFail)
    {
        var seenKeys = new HashSet<IterationKey>();
        var regressions = new List<IterationResult>();
        var drifted = new List<CategoryDrift>();

        foreach (var result in results)
        {
            seenKeys.Add(result.Key);

            if (result.Kind == OutcomeKind.Passed)
            {
                continue;
            }

            if (!knownFail.TryGetValue(result.Key, out var entry))
            {
                regressions.Add(result);
                continue;
            }

            var currentCategory = FailureCategoryNames.FromOutcomeKind(result.Kind);
            if (currentCategory != entry.Category)
            {
                regressions.Add(result);
                drifted.Add(new CategoryDrift(result, entry.Category));
            }
        }

        var newlyPassing = new List<KnownFailEntry>();
        var stale = new List<KnownFailEntry>();
        foreach (var entry in knownFail.Values)
        {
            if (!seenKeys.Contains(entry.Key))
            {
                stale.Add(entry);
                continue;
            }

            var actual = results.First(r => r.Key == entry.Key);
            if (actual.Kind == OutcomeKind.Passed)
            {
                newlyPassing.Add(entry);
            }
        }

        var suiteSummaries = results
            .GroupBy(r => (r.Key.Suite, r.SuiteDescription))
            .OrderBy(g => g.Key.Suite)
            .Select(g => new SuiteSummary(
                g.Key.Suite,
                g.Key.SuiteDescription,
                g.Count(),
                g.Count(r => r.Kind == OutcomeKind.Passed),
                g.Count(r => r.Kind == OutcomeKind.NotImplemented),
                g.Count(r => r.Kind == OutcomeKind.DataMissing),
                g.Count(r => r.Kind == OutcomeKind.ValueMismatch),
                g.Count(r => r.Kind == OutcomeKind.Error),
                g.Count(r => r.Kind == OutcomeKind.Unreproducible)))
            .ToList();

        var testCaseSummaries = results
            .GroupBy(r => (r.Key.Suite, r.Key.TestCase, r.TestCaseDescription))
            .OrderBy(g => g.Key.Suite)
            .ThenBy(g => g.Key.TestCase)
            .Select(g => new TestCaseSummary(
                g.Key.Suite,
                g.Key.TestCase,
                g.Key.TestCaseDescription,
                g.Count(),
                g.Count(r => r.Kind == OutcomeKind.Passed),
                g.Count(r => r.Kind == OutcomeKind.NotImplemented),
                g.Count(r => r.Kind == OutcomeKind.DataMissing),
                g.Count(r => r.Kind == OutcomeKind.ValueMismatch),
                g.Count(r => r.Kind == OutcomeKind.Error),
                g.Count(r => r.Kind == OutcomeKind.Unreproducible)))
            .ToList();

        return new ConformanceReport
        {
            All = results,
            Regressions = regressions,
            Drifted = drifted,
            NewlyPassing = newlyPassing,
            Stale = stale,
            SuiteSummaries = suiteSummaries,
            TestCaseSummaries = testCaseSummaries,
        };
    }

    public string FormatSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Total iterations: {All.Count}");
        sb.AppendLine();
        sb.AppendLine("Per-suite:");
        sb.AppendLine($"{"Suite",-6}{"Passed",8}{"Dispatched",12}{"PassRate",10}{"NotImpl",9}{"DataMiss",10}{"Mismatch",10}{"Error",7}{"Unrepro",9}  Description");
        foreach (var s in SuiteSummaries)
        {
            sb.AppendLine($"{s.SuiteId,-6}{s.Passed,8}{s.Dispatched,12}{s.PassRate,10:P1}{s.NotImplemented,9}{s.DataMissing,10}{s.ValueMismatch,10}{s.Error,7}{s.Unreproducible,9}  {s.Description}");
        }

        sb.AppendLine();
        sb.AppendLine($"Regressions (failing now and either off the known-fail list or drifted to a different category): {Regressions.Count}");
        foreach (var r in Regressions.Take(50))
        {
            sb.AppendLine($"  {r.Key} [{r.Kind}] {r.Reason ?? string.Join("; ", r.Mismatches.Select(m => $"{m.Name}: expected {m.Expected}, got {m.Actual}"))}");
        }

        if (Drifted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  Of which, category drift (still on the list, but recorded under a different category): {Drifted.Count}");
            foreach (var d in Drifted.Take(50))
            {
                sb.AppendLine($"    {d.Result.Key} recorded as {FailureCategoryNames.ToName(d.RecordedCategory)}, now {d.Result.Kind}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Newly passing (remove from known-fail.tsv -- progress!): {NewlyPassing.Count}");
        foreach (var e in NewlyPassing)
        {
            sb.AppendLine($"  {e.Key} was {FailureCategoryNames.ToName(e.Category)}: {e.Reason}");
        }

        if (Stale.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Stale known-fail entries (iteration no longer present in corpus): {Stale.Count}");
            foreach (var e in Stale)
            {
                sb.AppendLine($"  {e.Key}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Groups the run by (suite, testcase) -- see "Reporting by testcase" in CONTRIBUTING.md
    /// and <see cref="TestCaseSummary"/> -- instead of by the 12,757 individual iteration
    /// rows <see cref="FormatSummary"/> lists (up to the first 50 per section). Splits
    /// actionable testcases (at least one VALUE-MISMATCH/ERROR: real porting work) from
    /// parked ones (every non-passing iteration is NOT-IMPLEMENTED/DATA-MISSING/UNREPRODUCIBLE:
    /// blocked on something other than the port's logic) so a red oracle run is legible
    /// rather than alarming.
    /// </summary>
    public string FormatByTestCase()
    {
        var sb = new System.Text.StringBuilder();
        var actionable = TestCaseSummaries.Where(t => t.IsActionable).ToList();
        var parked = TestCaseSummaries.Where(t => !t.IsActionable).ToList();

        sb.AppendLine($"{TestCaseSummaries.Count} testcases total: {actionable.Count} actionable (have a VALUE-MISMATCH or ERROR), " +
                      $"{parked.Count} parked (every non-passing iteration is NOT-IMPLEMENTED/DATA-MISSING/UNREPRODUCIBLE).");
        sb.AppendLine();
        sb.AppendLine("Actionable (the porting work queue):");
        sb.AppendLine($"{"Suite.TC",-9}{"Passed",8}{"Total",8}{"Mismatch",10}{"Error",7}  Description");
        foreach (var t in actionable)
        {
            sb.AppendLine($"{$"{t.Suite}.{t.TestCase}",-9}{t.Passed,8}{t.Total,8}{t.ValueMismatch,10}{t.Error,7}  {t.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("Parked (blocked on something other than the port's logic -- not evidence of a bug):");
        sb.AppendLine($"{"Suite.TC",-9}{"Total",8}{"NotImpl",9}{"DataMiss",10}{"Unrepro",9}  Description");
        foreach (var t in parked)
        {
            sb.AppendLine($"{$"{t.Suite}.{t.TestCase}",-9}{t.Total,8}{t.NotImplemented,9}{t.DataMissing,10}{t.Unreproducible,9}  {t.Description}");
        }

        return sb.ToString();
    }
}
