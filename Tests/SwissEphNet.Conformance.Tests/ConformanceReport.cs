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
    int Error)
{
    /// <summary>Pass rate over iterations that were actually dispatched (excludes NOT-IMPLEMENTED/DATA-MISSING).</summary>
    public int Dispatched => Total - NotImplemented - DataMissing;

    public double PassRate => Dispatched == 0 ? 1.0 : (double)Passed / Dispatched;
}

public sealed class ConformanceReport
{
    public required IReadOnlyList<IterationResult> All { get; init; }

    /// <summary>Failing now, and not on the known-fail list: a regression.</summary>
    public required IReadOnlyList<IterationResult> Regressions { get; init; }

    /// <summary>On the known-fail list, but passing now: progress -- remove the entry.</summary>
    public required IReadOnlyList<KnownFailEntry> NewlyPassing { get; init; }

    /// <summary>On the known-fail list, but no longer present in the corpus at all.</summary>
    public required IReadOnlyList<KnownFailEntry> Stale { get; init; }

    public required IReadOnlyList<SuiteSummary> SuiteSummaries { get; init; }

    public bool Passed => Regressions.Count == 0;

    public static ConformanceReport Build(IReadOnlyList<IterationResult> results, IReadOnlyDictionary<IterationKey, KnownFailEntry> knownFail)
    {
        var seenKeys = new HashSet<IterationKey>();
        var regressions = new List<IterationResult>();

        foreach (var result in results)
        {
            seenKeys.Add(result.Key);
            if (result.Kind != OutcomeKind.Passed && !knownFail.ContainsKey(result.Key))
            {
                regressions.Add(result);
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
                g.Count(r => r.Kind == OutcomeKind.Error)))
            .ToList();

        return new ConformanceReport
        {
            All = results,
            Regressions = regressions,
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
        sb.AppendLine($"{"Suite",-6}{"Passed",8}{"Dispatched",12}{"PassRate",10}{"NotImpl",9}{"DataMiss",10}{"Mismatch",10}{"Error",7}  Description");
        foreach (var s in SuiteSummaries)
        {
            sb.AppendLine($"{s.SuiteId,-6}{s.Passed,8}{s.Dispatched,12}{s.PassRate,10:P1}{s.NotImplemented,9}{s.DataMissing,10}{s.ValueMismatch,10}{s.Error,7}  {s.Description}");
        }

        sb.AppendLine();
        sb.AppendLine($"Regressions (failing now, not on known-fail list): {Regressions.Count}");
        foreach (var r in Regressions.Take(50))
        {
            sb.AppendLine($"  {r.Key} [{r.Kind}] {r.Reason ?? string.Join("; ", r.Mismatches.Select(m => $"{m.Name}: expected {m.Expected}, got {m.Actual}"))}");
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
