using System.Globalization;

namespace BaselineVerify;

internal sealed class CompareResult
{
    /// <summary>Number of raw (non-blank) lines read from the local run.</summary>
    public int LocalLineCount;

    /// <summary>Number of raw (non-blank) lines read from the committed reference file.</summary>
    public int ReferenceLineCount;

    /// <summary>Union of case ids across both sides.</summary>
    public int Total;

    public int Exact;
    public int ToleranceOk;
    public int Fail;

    /// <summary>Rows that would have failed comparison but were excused by at least one waiver.</summary>
    public int Waived;

    /// <summary>
    /// Rows touched by at least one waiver's glob, regardless of comparison outcome.
    /// Always &gt;= Waived: a glob can match a row that turns out to be Exact or
    /// ToleranceOk on its own, in which case it counts here but not in Waived. This is
    /// "breadth of match" as distinct from "failures actually absorbed" -- a glob that
    /// touches a lot of rows is a risk even if today most of those rows happen to pass
    /// anyway, since tomorrow's regression in one of them would be silently swallowed.
    /// </summary>
    public int MatchedByAnyWaiver;

    public int OnlyLocal;
    public int OnlyReference;

    /// <summary>One line per FAIL row (not waived), in report order.</summary>
    public List<string> FailureDetails { get; } = [];

    /// <summary>One line per row that failed comparison but was excused by a waiver, so a reviewer can see how far the value moved without re-running by hand.</summary>
    public List<string> WaivedDetails { get; } = [];

    /// <summary>Fraction of Total whose failure was excused by a waiver. NaN if Total is 0.</summary>
    public double WaivedFraction => Total == 0 ? double.NaN : Waived / (double)Total;

    /// <summary>Fraction of Total touched by a waiver at all, regardless of outcome. NaN if Total is 0.</summary>
    public double MatchedFraction => Total == 0 ? double.NaN : MatchedByAnyWaiver / (double)Total;
}

/// <summary>
/// Row-by-row, field-by-field comparison keyed by case id (not by line position),
/// so a grid resize that changes row order or count does not itself register as a
/// wall of failures. Numeric fields are compared with an epsilon that combines a
/// relative and an absolute tolerance; every other field must match exactly.
///
/// Existence (a case id present on only one side) is checked and reported BEFORE any
/// waiver is consulted, and unconditionally counts as a failure: a waiver can only
/// ever excuse a value difference on a row both sides agree exists, never the
/// disappearance or appearance of a row. Waiving row deletion would let a matrix
/// change silently drop coverage while still reporting green.
///
/// A case id can match more than one waiver (e.g. a broad area waiver and a narrower
/// one nested inside it); every matching waiver gets credit, not just the first one
/// found, so waiver semantics do not silently depend on line order in waivers.tsv.
/// </summary>
internal static class Comparer
{
    // Combines a relative and an absolute component. The absolute floor matters
    // because a large share of the numeric fields in the matrix are exactly zero
    // (unused ascmc slots, zero-padded Gauquelin cusps for non-'G' systems, etc.);
    // for those, "relative to the larger magnitude" is meaningless, a value moving
    // from 0 to 1e-18 is not a real behavior change, and 1e-12 degrees (about
    // 3.6e-9 arcsec) is still far below anything the library or any caller of it
    // could act on.
    private const double RelativeEpsilon = 1e-13;
    private const double AbsoluteEpsilon = 1e-12;

    public static CompareResult Compare(
        IReadOnlyList<string> localRows,
        IReadOnlyList<string> referenceRows,
        IReadOnlyList<Waiver> waivers,
        Dictionary<Waiver, WaiverStats> waiverStats,
        string areaName)
    {
        var local = Index(localRows, $"{areaName} (local run)");
        var reference = Index(referenceRows, $"{areaName} (committed baseline)");
        var result = new CompareResult
        {
            LocalLineCount = localRows.Count(static r => r.Length > 0),
            ReferenceLineCount = referenceRows.Count(static r => r.Length > 0),
        };

        var allCaseIds = new SortedSet<string>(StringComparer.Ordinal);
        allCaseIds.UnionWith(local.Keys);
        allCaseIds.UnionWith(reference.Keys);
        result.Total = allCaseIds.Count;

        foreach (var caseId in allCaseIds)
        {
            var hasLocal = local.TryGetValue(caseId, out var localFields);
            var hasReference = reference.TryGetValue(caseId, out var referenceFields);

            // Existence is checked first and is never waivable: a waiver can only
            // excuse a value difference on a row both sides agree exists.
            if (!hasLocal)
            {
                result.OnlyReference++;
                result.FailureDetails.Add($"{caseId}: present in committed baseline, missing from current local run (not waivable)");
                continue;
            }

            if (!hasReference)
            {
                result.OnlyLocal++;
                result.FailureDetails.Add($"{caseId}: present in current local run, missing from committed baseline (not waivable)");
                continue;
            }

            var (outcome, detail) = CompareFields(caseId, localFields!, referenceFields!);
            var matchingWaivers = Waivers.MatchAll(waivers, caseId);

            if (matchingWaivers.Count > 0)
            {
                result.MatchedByAnyWaiver++;
                foreach (var waiver in matchingWaivers)
                {
                    if (!waiverStats.TryGetValue(waiver, out var stats))
                    {
                        continue;
                    }
                    stats.Matched++;
                    // Only a row that would actually have FAILED counts toward "this
                    // waiver is earning its keep". A row that is merely ToleranceOk
                    // needed no excuse -- it already passes on its own -- so crediting
                    // it here would let a waiver whose matches are all within-tolerance
                    // (never an outright failure) dodge the stale-waiver check forever.
                    if (outcome == FieldOutcome.Fail)
                    {
                        stats.Waived++;
                    }
                }
            }

            if (outcome == FieldOutcome.Fail && matchingWaivers.Count > 0)
            {
                result.Waived++;
                var globs = string.Join(", ", matchingWaivers.Select(w => w.Glob));
                result.WaivedDetails.Add($"{detail} (waived by: {globs})");
                continue;
            }

            switch (outcome)
            {
                case FieldOutcome.Exact:
                    result.Exact++;
                    break;
                case FieldOutcome.ToleranceOk:
                    result.ToleranceOk++;
                    break;
                case FieldOutcome.Fail:
                    result.Fail++;
                    result.FailureDetails.Add(detail!);
                    break;
            }
        }

        return result;
    }

    private enum FieldOutcome { Exact, ToleranceOk, Fail }

    private static (FieldOutcome Outcome, string? Detail) CompareFields(string caseId, string[] local, string[] reference)
    {
        if (local.Length != reference.Length)
        {
            return (FieldOutcome.Fail, $"{caseId}: field count differs (local {local.Length} value fields, reference {reference.Length} value fields)");
        }

        var allExact = true;
        for (var i = 0; i < local.Length; i++)
        {
            var l = local[i];
            var r = reference[i];
            if (string.Equals(l, r, StringComparison.Ordinal))
            {
                continue;
            }

            allExact = false;

            // Field i is array index i in the row's value list; the raw TSV column is
            // i + 2 (column 1 is the case id, value columns start at 2).
            var location = $"array index {i}, raw column {i + 2}";

            if (TryParseDouble(l, out var lv) && TryParseDouble(r, out var rv))
            {
                if (!WithinTolerance(lv, rv))
                {
                    return (FieldOutcome.Fail, $"{caseId}: {location} beyond tolerance (local={l}, reference={r})");
                }
            }
            else
            {
                return (FieldOutcome.Fail, $"{caseId}: {location} exact-match mismatch (local=\"{l}\", reference=\"{r}\")");
            }
        }

        return (allExact ? FieldOutcome.Exact : FieldOutcome.ToleranceOk, null);
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static bool WithinTolerance(double a, double b)
    {
        if (a.Equals(b))
        {
            return true;
        }
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return double.IsNaN(a) && double.IsNaN(b);
        }
        if (double.IsInfinity(a) || double.IsInfinity(b))
        {
            return a == b;
        }

        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        var threshold = Math.Max(AbsoluteEpsilon, RelativeEpsilon * scale);
        return Math.Abs(a - b) <= threshold;
    }

    private static Dictionary<string, string[]> Index(IReadOnlyList<string> rows, string source)
    {
        var dict = new Dictionary<string, string[]>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Length == 0)
            {
                continue;
            }
            var tabIndex = row.IndexOf('\t');
            var caseId = tabIndex < 0 ? row : row[..tabIndex];
            var rest = tabIndex < 0 ? [] : row[(tabIndex + 1)..].Split('\t');
            if (!dict.TryAdd(caseId, rest))
            {
                throw new InvalidOperationException($"Duplicate case id \"{caseId}\" in {source}. Case ids must be unique within an area.");
            }
        }
        return dict;
    }
}
