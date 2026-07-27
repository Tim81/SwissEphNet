using System.Globalization;

namespace BaselineVerify;

internal sealed class CompareResult
{
    public int Total;
    public int Exact;
    public int ToleranceOk;
    public int Fail;
    public int Waived;
    public int OnlyLocal;
    public int OnlyReference;
    public List<string> FailureDetails { get; } = [];
}

/// <summary>
/// Row-by-row, field-by-field comparison keyed by case id (not by line position),
/// so a grid resize that changes row order or count does not itself register as a
/// wall of failures. Numeric fields are compared with a relative epsilon; every
/// other field must match exactly.
/// </summary>
internal static class Comparer
{
    private const double RelativeEpsilon = 1e-13;

    public static CompareResult Compare(IReadOnlyList<string> localRows, IReadOnlyList<string> referenceRows, IReadOnlyList<Waiver> waivers)
    {
        var local = Index(localRows);
        var reference = Index(referenceRows);
        var result = new CompareResult();

        var allCaseIds = new SortedSet<string>(StringComparer.Ordinal);
        allCaseIds.UnionWith(local.Keys);
        allCaseIds.UnionWith(reference.Keys);
        result.Total = allCaseIds.Count;

        foreach (var caseId in allCaseIds)
        {
            var waiver = Waivers.Match(waivers, caseId);
            var hasLocal = local.TryGetValue(caseId, out var localFields);
            var hasReference = reference.TryGetValue(caseId, out var referenceFields);

            if (waiver is not null)
            {
                result.Waived++;
                continue;
            }

            if (!hasLocal)
            {
                result.OnlyReference++;
                result.FailureDetails.Add($"{caseId}: present in committed baseline, missing from current local run");
                continue;
            }

            if (!hasReference)
            {
                result.OnlyLocal++;
                result.FailureDetails.Add($"{caseId}: present in current local run, missing from committed baseline");
                continue;
            }

            var (outcome, detail) = CompareFields(caseId, localFields!, referenceFields!);
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
            return (FieldOutcome.Fail, $"{caseId}: field count differs (local {local.Length}, reference {reference.Length})");
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

            if (TryParseDouble(l, out var lv) && TryParseDouble(r, out var rv))
            {
                if (!WithinTolerance(lv, rv))
                {
                    return (FieldOutcome.Fail, $"{caseId}: field {i} beyond tolerance (local={l}, reference={r})");
                }
            }
            else
            {
                return (FieldOutcome.Fail, $"{caseId}: field {i} exact-match mismatch (local=\"{l}\", reference=\"{r}\")");
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
        if (scale < 1e-300)
        {
            return true; // both effectively zero
        }
        return Math.Abs(a - b) / scale <= RelativeEpsilon;
    }

    private static Dictionary<string, string[]> Index(IReadOnlyList<string> rows)
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
            dict[caseId] = rest;
        }
        return dict;
    }
}
