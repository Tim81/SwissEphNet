namespace BaselineVerify;

/// <summary>
/// Field-level divergence statistics for one area: how many numeric fields were
/// compared, how many differed at all (any non-exact string match that both sides
/// still parse as numbers), and the relative-difference distribution across just
/// the differing ones. Used only by --report-only mode -- these numbers never
/// affect PASS/FAIL.
/// </summary>
internal sealed class DivergenceStats
{
    public int FieldsCompared;

    /// <summary>
    /// Of FieldsCompared, how many actually parsed as numeric (finite, non-NaN) doubles on
    /// both sides -- unlike FieldsCompared, this excludes serr diagnostic strings, planet
    /// names, and other non-numeric fields. See docs/known-issues.md, "DivergenceReport's
    /// field-compared count includes non-numeric fields": FieldsCompared is kept as-is
    /// because Tools/BaselineGen/README.md and other docs already cite the number it
    /// produces; this is the correctly-labeled count added alongside it.
    /// </summary>
    public int NumericFieldsCompared;

    public int FieldsDiffering;

    /// <summary>Of FieldsDiffering, how many are still beyond tolerance (i.e. would actually fail the gate) after the angle-wraparound allowance.</summary>
    public int FieldsBeyondTolerance;

    /// <summary>Relative difference (EffectiveAbsoluteDiff / max(|a|,|b|)) for every differing field, sorted ascending after <see cref="DivergenceReport.Collect"/> returns.</summary>
    public List<double> SortedRelativeDiffs { get; } = [];

    public double Median => Percentile(SortedRelativeDiffs, 50);
    public double P90 => Percentile(SortedRelativeDiffs, 90);
    public double P99 => Percentile(SortedRelativeDiffs, 99);
    public double Max => SortedRelativeDiffs.Count == 0 ? 0 : SortedRelativeDiffs[^1];

    /// <summary>Linear-interpolated percentile (0-100) of an already-sorted list. 0 for an empty list.</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double p)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var rank = p / 100.0 * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sortedValues[lower];
        }
        var weight = rank - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }
}

/// <summary>
/// Collects field-level divergence for one area's local-vs-reference rows, reusing
/// Comparer's row indexing and number parsing so "what counts as the same value"
/// is defined in exactly one place. Existence mismatches (a case id on only one
/// side) and arity changes (different field counts) are skipped here -- Comparer
/// reports those separately, and neither one is a numeric-field divergence to fold
/// into a relative-difference distribution.
/// </summary>
internal static class DivergenceReport
{
    public static DivergenceStats Collect(IReadOnlyList<string> localRows, IReadOnlyList<string> referenceRows, string areaName)
    {
        var local = Comparer.Index(localRows, $"{areaName} (local run)");
        var reference = Comparer.Index(referenceRows, $"{areaName} (committed baseline)");
        var stats = new DivergenceStats();

        foreach (var (caseId, localFields) in local)
        {
            if (!reference.TryGetValue(caseId, out var referenceFields))
            {
                continue;
            }
            if (localFields.Length != referenceFields.Length)
            {
                continue;
            }

            for (var i = 0; i < localFields.Length; i++)
            {
                var l = localFields[i];
                var r = referenceFields[i];
                stats.FieldsCompared++;

                // Parsed unconditionally (not just for differing fields) so equal-and-numeric
                // fields still count toward NumericFieldsCompared -- an equal field never
                // reaches FieldsDiffering, but it is still a numeric one.
                double lv = 0, rv = 0;
                var isNumeric = Comparer.TryParseDouble(l, out lv) && Comparer.TryParseDouble(r, out rv)
                    && !double.IsNaN(lv) && !double.IsNaN(rv) && !double.IsInfinity(lv) && !double.IsInfinity(rv);
                if (isNumeric)
                {
                    stats.NumericFieldsCompared++;
                }

                if (string.Equals(l, r, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!isNumeric)
                {
                    continue;
                }

                stats.FieldsDiffering++;
                if (!Comparer.WithinTolerance(lv, rv))
                {
                    stats.FieldsBeyondTolerance++;
                }
                var scale = Math.Max(Math.Abs(lv), Math.Abs(rv));
                var absDiff = Comparer.EffectiveAbsoluteDiff(lv, rv);
                stats.SortedRelativeDiffs.Add(scale > 0 ? absDiff / scale : absDiff);
            }
        }

        stats.SortedRelativeDiffs.Sort();
        return stats;
    }
}
