namespace BaselineVerify;

/// <summary>
/// Loads Tests/baseline/row-counts.tsv: one "area&lt;TAB&gt;count" line per area BaselineMatrix's
/// Areas.All currently knows about, all fields required.
///
/// This exists to close a specific hole: <see cref="Verdict.ForArea"/> checks only
/// <c>Fail</c>, <c>OnlyLocal</c>, <c>OnlyReference</c> and the two waiver fractions -- all of
/// which read zero when an area's committed TSV has been silently reduced to zero rows (a
/// truncated/emptied file compared against a generator that likewise emits nothing for that
/// area). Nothing before this file asserted a row count anywhere, so that scenario reported
/// PASS with an all-zero row.
///
/// A committed, per-area count is the thorough option, not the cheap one (failing only on
/// <c>Total == 0</c> would have caught the demonstrated all-the-way-to-zero attack but not a
/// partial reduction, e.g. a matrix sweep quietly narrowed from 27 GeoLats to 5). It is
/// deliberately a separate manifest from the TSVs themselves, in the same directory
/// (Tests/baseline/), so a change to it is covered by the same
/// scripts/verify-baseline-log.ps1 guard that already watches every Tests/baseline/*.tsv file
/// for an accompanying regenerations-log entry -- an attacker who wants to shrink a TSV
/// without tripping this check has to also edit row-counts.tsv, and doing that without a
/// scripts/regenerate-baseline.ps1 run (which rewrites both together, see that script) leaves
/// exactly the same trail finding 3's fix is built to catch.
///
/// A deliberate row-count change (the port adding house system 'J', the `_ex2` entry points,
/// or a new area entirely) is still possible: scripts/regenerate-baseline.ps1 rewrites this
/// file's counts from the freshly generated run in the same pass where it rewrites the TSVs,
/// gated behind -ExpectedScope (see Program.cs's --diff-scope mode) exactly like everything
/// else that pass touches.
/// </summary>
internal static class RowCounts
{
    public const string FileName = "row-counts.tsv";

    public static IReadOnlyDictionary<string, int> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(path);
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var raw = lines[lineNumber];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split('\t');
            if (parts.Length != 2 || parts.Any(static p => p.Trim().Length == 0) ||
                !int.TryParse(parts[1].Trim(), out var count) || count < 0)
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: malformed row-count line, expected 'area<TAB>count' with a non-negative integer count: \"{raw}\"");
            }

            var area = parts[0].Trim();
            if (!result.TryAdd(area, count))
            {
                throw new InvalidOperationException($"{path}:{lineNumber + 1}: duplicate row-count entry for area '{area}'.");
            }
        }

        return result;
    }
}
