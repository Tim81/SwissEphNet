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
    /// <summary>Pass rate over iterations that were actually dispatched and comparable (excludes NOT-IMPLEMENTED/DATA-MISSING/UNREPRODUCIBLE).</summary>
    public int Dispatched => Total - NotImplemented - DataMissing - Unreproducible;

    public double PassRate => Dispatched == 0 ? 1.0 : (double)Passed / Dispatched;
}

/// <summary>A known-fail entry whose recorded category no longer matches what the port actually does now.</summary>
public sealed record CategoryDrift(IterationResult Result, FailureCategory RecordedCategory);

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
    /// list, just recorded under a category the current run no longer
    /// matches -- e.g. a VALUE-MISMATCH that degraded into an ERROR crash, or
    /// a mismatch that got orders of magnitude worse in a way that changed
    /// its classification. Key-membership alone would let these through as
    /// "still failing, still on the list"; they are not the same failure.
    /// </summary>
    public required IReadOnlyList<CategoryDrift> Drifted { get; init; }

    /// <summary>On the known-fail list, but passing now: progress -- remove the entry.</summary>
    public required IReadOnlyList<KnownFailEntry> NewlyPassing { get; init; }

    /// <summary>On the known-fail list, but no longer present in the corpus at all.</summary>
    public required IReadOnlyList<KnownFailEntry> Stale { get; init; }

    public required IReadOnlyList<SuiteSummary> SuiteSummaries { get; init; }

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

        return new ConformanceReport
        {
            All = results,
            Regressions = regressions,
            Drifted = drifted,
            NewlyPassing = newlyPassing,
            Stale = stale,
            SuiteSummaries = suiteSummaries,
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
}
