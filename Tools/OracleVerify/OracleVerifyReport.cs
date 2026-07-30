using System.Globalization;

namespace OracleVerify;

/// <summary>Why a row counts as a regression -- included so a FAIL report names the specific reason, not just the case id.</summary>
internal enum RegressionKind
{
    /// <summary>Differs now and is not in known-diff.tsv at all.</summary>
    NotListed,

    /// <summary>
    /// Listed, but under a category whose expected failure shape (see <see cref="FailureShape"/>)
    /// does not match what is actually failing now -- e.g. a row recorded as RETC whose retc now
    /// matches, or a PORT-VERSION/LIBM-RESIDUAL row whose retc has started differing too.
    /// </summary>
    CategoryMismatch,

    /// <summary>
    /// Listed under the right category, but the current max ULP distance exceeds what
    /// known-diff.tsv recorded -- the row stayed on the list while quietly getting worse. This is
    /// the check ConformanceReport (see its own remarks, lines 99-107) cannot do: category alone
    /// never shrinks or grows, so a 1e-9 mismatch widening to 1e-3 there is invisible. Here it is not.
    /// </summary>
    UlpGrew,

    /// <summary>
    /// Listed under the right category with the right failure shape, but the row's categorical
    /// state (see <see cref="KnownDiffEntry.MaxUlp"/>) has flipped either way since the entry was
    /// recorded: a row recorded as categorical (max_ulp = "categorical") now has only finite field
    /// diffs, or a row recorded with a numeric max_ulp now has at least one categorical field diff.
    /// Neither direction can be judged by comparing magnitudes -- a magnitude comparison has no
    /// meaning once one side is "not a number" -- so a transition either way is reported here
    /// instead of silently passing or silently being treated as growth.
    /// </summary>
    CategoricalStateChanged,
}

internal sealed record Regression(RowOutcome Outcome, RegressionKind Kind, KnownDiffEntry? Entry, string Detail);

/// <summary>
/// The three-way check, applied the same way
/// Tests/SwissEphNet.Conformance.Tests/ConformanceReport.cs applies it to the correctness oracle:
/// a row must either match outright, or be accounted for by exactly one known-diff.tsv entry whose
/// category still fits and whose recorded max ULP has not been exceeded. Rows that have started
/// passing must be pruned; rows whose case_id fell out of the grid must be removed. Any of the
/// three failing means known-diff.tsv and the dumps have diverged in a way a reviewer needs to see.
/// </summary>
internal sealed class OracleVerifyReport
{
    public required IReadOnlyList<RowOutcome> All { get; init; }
    public required IReadOnlyList<Regression> Regressions { get; init; }
    public required IReadOnlyList<KnownDiffEntry> NewlyPassing { get; init; }
    public required IReadOnlyList<KnownDiffEntry> Stale { get; init; }

    public bool Passed => Regressions.Count == 0 && NewlyPassing.Count == 0 && Stale.Count == 0;

    public static OracleVerifyReport Build(IReadOnlyList<RowOutcome> outcomes, IReadOnlyDictionary<string, KnownDiffEntry> knownDiff)
    {
        var byCaseId = new Dictionary<string, RowOutcome>(outcomes.Count, StringComparer.Ordinal);
        foreach (var outcome in outcomes)
        {
            byCaseId[outcome.CaseId] = outcome;
        }

        var regressions = new List<Regression>();
        foreach (var outcome in outcomes)
        {
            if (outcome.Matches)
            {
                continue;
            }

            if (!knownDiff.TryGetValue(outcome.CaseId, out var entry))
            {
                regressions.Add(new Regression(outcome, RegressionKind.NotListed, null,
                    "differs from the C reference but has no entry in known-diff.tsv"));
                continue;
            }

            var expectedShape = entry.Category switch
            {
                DiffCategory.Retc => FailureShape.RetcDiffers,
                DiffCategory.Serr => FailureShape.ErrOnlyDiffers,
                _ => FailureShape.HexOnlyDiffers,
            };
            if (outcome.Shape != expectedShape)
            {
                regressions.Add(new Regression(outcome, RegressionKind.CategoryMismatch, entry,
                    $"listed as {DiffCategoryNames.ToName(entry.Category)} (expects {expectedShape}), current failure shape is {outcome.Shape}"));
                continue;
            }

            // entry.MaxUlp is null exactly when the row was recorded as categorical -- see
            // KnownDiffEntry.MaxUlp's remarks. A magnitude comparison is meaningless once either
            // side is "not a number", so a flip in either direction is its own regression kind,
            // never routed through the numeric UlpGrew check below.
            var wasCategorical = entry.MaxUlp is null;
            if (outcome.HasCategoricalFieldDiff != wasCategorical)
            {
                regressions.Add(new Regression(outcome, RegressionKind.CategoricalStateChanged, entry,
                    wasCategorical
                        ? "recorded as categorical (max_ulp = \"categorical\"), but every field diff is now finite"
                        : "recorded with a numeric max_ulp, but at least one field diff is now categorical (NaN on one side, finite on the other)"));
                continue;
            }

            if (!wasCategorical && outcome.MaxUlp > entry.MaxUlp!.Value)
            {
                regressions.Add(new Regression(outcome, RegressionKind.UlpGrew, entry,
                    $"max ULP distance grew: recorded {entry.MaxUlp}, observed {outcome.MaxUlp}"));
            }
        }

        var newlyPassing = new List<KnownDiffEntry>();
        var stale = new List<KnownDiffEntry>();
        foreach (var entry in knownDiff.Values)
        {
            if (!byCaseId.TryGetValue(entry.CaseId, out var outcome))
            {
                stale.Add(entry);
                continue;
            }

            if (outcome.Matches)
            {
                newlyPassing.Add(entry);
            }
        }

        return new OracleVerifyReport
        {
            All = outcomes,
            Regressions = regressions,
            NewlyPassing = newlyPassing,
            Stale = stale,
        };
    }

    public string FormatSummary()
    {
        var sb = new System.Text.StringBuilder();
        var differing = All.Count(o => !o.Matches);
        sb.AppendLine($"Total rows compared: {All.Count} ({All.Count - differing} bit-identical, {differing} differing)");

        var byCategory = All
            .Where(o => !o.Matches)
            .Select(o => o.Shape)
            .GroupBy(s => s)
            .ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine(
            $"  of which {byCategory.GetValueOrDefault(FailureShape.RetcDiffers)} have a differing return code, " +
            $"{byCategory.GetValueOrDefault(FailureShape.HexOnlyDiffers)} differ only in value(s), " +
            $"{byCategory.GetValueOrDefault(FailureShape.ErrOnlyDiffers)} differ only in the error string");

        sb.AppendLine();
        sb.AppendLine($"Regressions (differing row not accounted for, or accounted for incorrectly): {Regressions.Count}");
        foreach (var r in Regressions.Take(50))
        {
            sb.AppendLine($"  {r.Outcome.CaseId} [{r.Kind}] {r.Detail}");
        }
        if (Regressions.Count > 50)
        {
            sb.AppendLine($"  ... and {Regressions.Count - 50} more");
        }

        sb.AppendLine();
        sb.AppendLine($"Newly passing (remove from known-diff.tsv -- progress!): {NewlyPassing.Count}");
        foreach (var e in NewlyPassing.Take(50))
        {
            sb.AppendLine($"  {e.CaseId} was {DiffCategoryNames.ToName(e.Category)}: {e.Reason}");
        }
        if (NewlyPassing.Count > 50)
        {
            sb.AppendLine($"  ... and {NewlyPassing.Count - 50} more");
        }

        if (Stale.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Stale known-diff entries (case_id no longer present in the grid): {Stale.Count}");
            foreach (var e in Stale)
            {
                sb.AppendLine($"  {e.CaseId}");
            }
        }

        return sb.ToString();
    }

    private static readonly DiffCategory[] AllCategories =
        [DiffCategory.PortVersion, DiffCategory.LibmResidual, DiffCategory.Retc, DiffCategory.Serr];

    /// <summary>
    /// Every category, including one with zero entries -- LIBM-RESIDUAL is expected to read 0 (see
    /// scripts/verify-crt-parity.ps1), and that is only visible as a reported "0", not as an absent
    /// line, if this walks the full <see cref="DiffCategory"/> enum instead of only the categories
    /// actually present in <paramref name="knownDiff"/>.
    /// </summary>
    public string FormatCategoryBreakdown(IReadOnlyDictionary<string, KnownDiffEntry> knownDiff)
    {
        var counts = AllCategories.ToDictionary(c => c, _ => 0);
        foreach (var entry in knownDiff.Values)
        {
            counts[entry.Category]++;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("known-diff.tsv by category:");
        foreach (var category in AllCategories.OrderBy(c => DiffCategoryNames.ToName(c), StringComparer.Ordinal))
        {
            sb.AppendLine($"  {DiffCategoryNames.ToName(category),-14} {counts[category].ToString(CultureInfo.InvariantCulture),6}");
        }
        return sb.ToString();
    }
}
