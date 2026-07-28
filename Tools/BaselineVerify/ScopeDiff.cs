using System.Text.RegularExpressions;

namespace BaselineVerify;

/// <summary>
/// Pure per-area case-id diff against a set of compiled <c>-ExpectedScope</c> globs, extracted
/// from Program.cs's <c>RunDiffScopeMode</c> so it is unit-testable directly on plain
/// <c>string[]</c> rows without touching disk. All file I/O (reading each side's
/// <c>baseline-&lt;area&gt;.tsv</c>, hashing the new file) stays in Program.cs; this only ever
/// sees rows already read into memory, the same separation <see cref="Verdict"/> and
/// <see cref="Comparer"/> already follow.
/// </summary>
internal static class ScopeDiff
{
    internal readonly record struct AreaResult(int Changed, int Added, int Removed, int NewRowCount, IReadOnlyList<string> Offenders);

    /// <summary>
    /// Indexes <paramref name="oldRows"/> and <paramref name="newRows"/> by case id (via
    /// <see cref="Comparer.Index"/>) and classifies every id present in the union as added,
    /// removed, or changed (a byte-for-byte field comparison, same as <see cref="Comparer"/>
    /// uses for existence/equality -- never the tolerance-aware numeric comparison, since scope
    /// checking cares whether a row moved at all, not by how much). An id is an "offender" if
    /// it changed/added/removed and does not match at least one of <paramref name="compiled"/>.
    /// </summary>
    public static AreaResult ComputeArea(
        string areaName,
        IReadOnlyList<string> oldRows,
        IReadOnlyList<string> newRows,
        IReadOnlyList<(string Glob, Regex Pattern)> compiled)
    {
        var oldIndex = Comparer.Index(oldRows, $"{areaName} (old)");
        var newIndex = Comparer.Index(newRows, $"{areaName} (new)");

        var allIds = new SortedSet<string>(StringComparer.Ordinal);
        allIds.UnionWith(oldIndex.Keys);
        allIds.UnionWith(newIndex.Keys);

        var changed = 0;
        var added = 0;
        var removed = 0;
        var offenders = new List<string>();

        foreach (var id in allIds)
        {
            var hasOld = oldIndex.TryGetValue(id, out var oldFields);
            var hasNew = newIndex.TryGetValue(id, out var newFields);

            string kind;
            if (!hasOld)
            {
                kind = "added";
                added++;
            }
            else if (!hasNew)
            {
                kind = "removed";
                removed++;
            }
            else if (!oldFields!.SequenceEqual(newFields!))
            {
                kind = "changed";
                changed++;
            }
            else
            {
                continue;
            }

            if (!compiled.Any(c => c.Pattern.IsMatch(id)))
            {
                offenders.Add($"{areaName}\t{id}\t{kind}");
            }
        }

        return new AreaResult(changed, added, removed, newIndex.Count, offenders);
    }
}
