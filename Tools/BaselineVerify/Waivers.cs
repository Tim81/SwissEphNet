using System.Text;
using System.Text.RegularExpressions;

namespace BaselineVerify;

/// <summary>One glob-matched waiver: a case-id pattern, the PR it belongs to, and the reason it is expected to differ.</summary>
internal sealed record Waiver(string Glob, string PrNumber, string Reason, Regex Pattern);

/// <summary>Usage counters for one waiver, accumulated across every area in a single run.</summary>
internal sealed class WaiverStats
{
    /// <summary>Rows present on both sides whose case id matched this waiver's glob, regardless of outcome.</summary>
    public int Matched;

    /// <summary>Of those, rows that would have FAILED comparison without this waiver.</summary>
    public int Waived;
}

/// <summary>
/// Loads Tools/BaselineVerify/waivers.tsv: one "glob&lt;TAB&gt;PR-number&lt;TAB&gt;reason" per
/// line, all three fields required and non-empty. Malformed lines are a hard load-time
/// failure, not a silent skip.
///
/// Glob syntax is deliberately not shell/gitignore glob: a case id is a run of
/// pipe-delimited fields (e.g. "H|A|23.4392911|-89|0"), so '*' matches within a single
/// field (compiles to <c>[^|]*</c>) and '**' matches across fields (compiles to
/// <c>.*</c>). To waive an entire area, spell it out with the separator: "H|**".
///
/// The segment of the glob before its first '|' (the area prefix) must be written as a
/// literal string with no '*' in it at all -- this is enforced at load time, not just
/// documented. Without that rule, "H**" would compile to <c>^H.*$</c> and silently
/// sweep in every area whose prefix starts with 'H' (H, HP, HN, HS, HX, HSUN), and
/// "*|*|*|*|*" would match every five-field case id in the whole matrix -- both look
/// scoped at a glance and are not. "H|**" (with the pipe) is the correct, and only,
/// way to waive a whole area.
///
/// A waiver that is exactly "*" or "**", or whose compiled pattern matches any of the
/// synthetic probe case ids below, is also rejected at load time.
/// </summary>
internal static class Waivers
{
    /// <summary>
    /// Case ids no real matrix case can ever produce (area prefixes are short,
    /// hand-written identifiers; these are deliberately long, nonsensical, and of
    /// varying field count). Any waiver glob that matches one of these is too broad
    /// to be trusted. This is a backstop for shapes the leading-literal-prefix rule
    /// does not itself cover (e.g. a glob with a legitimate-looking literal prefix
    /// that is still unreasonably broad after it), not the primary defense.
    /// </summary>
    private static readonly string[] ProbeCaseIds =
    [
        "ZZZ_WAIVER_PROBE|field-one|field-two|field-three|field-four|field-five",
        "ZZZ_WAIVER_PROBE",
        "ZZZ_WAIVER_PROBE|field-one",
        "ZZZ_WAIVER_PROBE|field-one|field-two",
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<Waiver> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var waivers = new List<Waiver>();
        var lines = File.ReadAllLines(path);
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var raw = lines[lineNumber];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split('\t', 3);
            if (parts.Length != 3 || parts.Any(p => p.Trim().Length == 0))
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: malformed waiver line, expected 'glob<TAB>PR-number<TAB>reason' with all three fields non-empty: \"{raw}\"");
            }

            var glob = parts[0].Trim();
            var prNumber = parts[1].Trim();
            var reason = parts[2].Trim();

            if (glob is "*" or "**")
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: waiver glob '{glob}' is a catch-all and is not allowed. Scope it to a specific area and case shape.");
            }

            var firstPipe = glob.IndexOf('|');
            var leadingSegment = firstPipe < 0 ? glob : glob[..firstPipe];
            if (leadingSegment.Contains('*'))
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: waiver glob '{glob}' has a wildcard before its first '|' (area prefix \"{leadingSegment}\"). " +
                    "Write the area prefix literally and use the separator, e.g. \"H|**\" not \"H**\" -- " +
                    "otherwise a two-character glob can silently sweep in every area sharing a leading letter.");
            }

            var pattern = ToRegex(glob);
            var matchedProbe = Array.Find(ProbeCaseIds, pattern.IsMatch);
            if (matchedProbe is not null)
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: waiver glob '{glob}' matches the synthetic probe case id (\"{matchedProbe}\") and is too broad. Narrow it.");
            }

            waivers.Add(new Waiver(glob, prNumber, reason, pattern));
        }
        return waivers;
    }

    /// <summary>Fresh, zeroed usage counters for every loaded waiver, to be threaded through every area's comparison.</summary>
    public static Dictionary<Waiver, WaiverStats> InitStats(IReadOnlyList<Waiver> waivers) =>
        waivers.ToDictionary(w => w, _ => new WaiverStats());

    /// <summary>
    /// Every waiver whose glob matches <paramref name="caseId"/>, not just the first.
    /// A case id can legitimately fall under more than one waiver (a broad area waiver
    /// and a narrower one nested inside it); if only the first match got credit, the
    /// second would always show zero matches and get flagged stale by line-order
    /// accident.
    /// </summary>
    public static IReadOnlyList<Waiver> MatchAll(IReadOnlyList<Waiver> waivers, string caseId)
    {
        List<Waiver>? matches = null;
        foreach (var waiver in waivers)
        {
            if (waiver.Pattern.IsMatch(caseId))
            {
                (matches ??= []).Add(waiver);
            }
        }
        return (IReadOnlyList<Waiver>?)matches ?? [];
    }

    private static Regex ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            if (glob[i] == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    sb.Append(".*");
                    i += 2;
                }
                else
                {
                    sb.Append("[^|]*");
                    i += 1;
                }
            }
            else
            {
                sb.Append(Regex.Escape(glob[i].ToString()));
                i += 1;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled, RegexTimeout);
    }
}
