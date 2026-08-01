namespace BaselineMatrix;

/// <summary>
/// Reachability checks for the handful of sweeps that exist to drive one specific
/// library branch rather than to sample a grid.
///
/// A sweep of that kind can go on emitting exactly the rows it always emitted while
/// no longer reaching the branch it was built for. Three of them did, and every gate
/// stayed green the whole time: a row that reaches nothing is still a row, still
/// counted in row-counts.tsv, still inside its -ExpectedScope, and still matching the
/// committed baseline byte for byte. Nothing downstream of generation can tell the
/// difference, because the difference is not in the output -- it is in what produced
/// it.
///
/// The checks here therefore assert on the only evidence of reaching that is visible
/// from outside the library: an input that must land inside a documented numeric
/// window, and rows that must differ from one another because the sole thing varying
/// between them is the switch the branch is gated on. Two rows obliged to differ that
/// come out byte-identical is precisely the symptom each of the three defects
/// presented -- and "the sweep still emits rows" was true of all three throughout.
///
/// Every check runs inside an area's AddRows, which Areas.Generate calls before that
/// area's rows reach a file at all, and an unhandled throw takes BaselineGen down with
/// a non-zero exit. Both callers stop there: scripts/regenerate-baseline.ps1 generates
/// into temp directories and bails on the exit code, so Tests/baseline/ is never
/// touched, and Tools/BaselineVerify never gets rows to compare. That is the same
/// "bookkeeping check that throws before the write" shape Tools/OracleDump/Program.cs
/// uses for its zero-row and stray-sid_mode guards.
///
/// These are assertions about the sweep, never about the library's numbers. They only
/// ever ask whether two rows are identical, never whether either one is correct --
/// correctness is the baseline comparison's job, and a check that duplicated it would
/// go red for changes the baseline is there to characterize.
/// </summary>
internal static class Reachability
{
    /// <summary>
    /// Indexes emitted rows by case id, mapping each to its value fields (everything
    /// after the first tab). Duplicate case ids throw: the checks below look rows up
    /// by id, and a duplicate would silently hide one of the two from them --
    /// Tools/BaselineVerify/Comparer.cs rejects duplicates for the same reason.
    /// </summary>
    public static Dictionary<string, string> IndexPayloads(List<string> rows)
    {
        var index = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var tab = row.IndexOf('\t');
            var caseId = tab < 0 ? row : row[..tab];
            var payload = tab < 0 ? string.Empty : row[(tab + 1)..];
            if (!index.TryAdd(caseId, payload))
            {
                throw new InvalidOperationException(
                    $"Duplicate case id \"{caseId}\" emitted within one area. Case ids must be unique within an area, " +
                    "and a reachability check that looks rows up by id cannot see the second one at all.");
            }
        }
        return index;
    }

    /// <summary>
    /// The value fields of <paramref name="caseId"/>, or a throw naming the branch the
    /// missing row was supposed to be evidence for. A sweep that stops emitting the row
    /// entirely is the same failure as one that stops reaching the branch, and it must
    /// not degrade into a check that quietly compares nothing.
    /// </summary>
    public static string Payload(Dictionary<string, string> index, string caseId, string sweep, string target)
    {
        if (!index.TryGetValue(caseId, out var payload))
        {
            throw new InvalidOperationException(
                $"{sweep} sweep is no longer reaching {target}: it emitted no row with case id \"{caseId}\", so the " +
                "reachability check for that branch has nothing left to compare. Either restore the row or, if the " +
                "sweep's shape genuinely changed, update the check to name the rows that carry the evidence now.");
        }
        return payload;
    }

    /// <summary>
    /// Requires every one of <paramref name="caseIds"/> to carry value fields distinct
    /// from every other's. The caller picks ids that differ only in the input the branch
    /// is gated on, so identical output means the input stopped varying where it counts.
    /// </summary>
    /// <param name="index">Case id to value fields, from <see cref="IndexPayloads"/>.</param>
    /// <param name="sweep">Case-id prefix of the sweep being checked, for the message.</param>
    /// <param name="target">The library branch the sweep exists to reach.</param>
    /// <param name="reachingRequires">One sentence on what reaching that branch takes, so the message says how to fix it.</param>
    /// <param name="caseIds">The rows carrying the evidence; at least two, differing only in the branch's gating input.</param>
    public static void RequireDistinctPayloads(
        Dictionary<string, string> index,
        string sweep,
        string target,
        string reachingRequires,
        params string[] caseIds)
    {
        if (caseIds.Length < 2)
        {
            throw new InvalidOperationException(
                $"{sweep} sweep's reachability check was handed {caseIds.Length} case id(s) but needs at least 2 to " +
                $"establish that {target} is reached. A check with nothing to compare passes vacuously, which is the " +
                "one outcome it must never have.");
        }

        for (var i = 0; i < caseIds.Length; i++)
        {
            var left = Payload(index, caseIds[i], sweep, target);
            for (var j = i + 1; j < caseIds.Length; j++)
            {
                var right = Payload(index, caseIds[j], sweep, target);
                if (string.Equals(left, right, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{sweep} sweep is no longer reaching {target}: rows \"{caseIds[i]}\" and \"{caseIds[j]}\" " +
                        $"carry identical value fields (\"{left}\"), yet they differ in exactly the input that branch " +
                        $"is gated on. {reachingRequires} Byte-identical output from inputs that differ only in that " +
                        "respect is what a sweep that has stopped reaching the branch produces, and no other gate can " +
                        "see it: the rows are still emitted, still counted, and still match the committed baseline.");
                }
            }
        }
    }
}
