using System.Text;
using System.Text.RegularExpressions;

namespace BaselineVerify;

/// <summary>One glob-matched waiver: a case-id pattern, the PR it belongs to, and the reason it is expected to differ.</summary>
internal sealed record Waiver(string Glob, string PrNumber, string Reason, Regex Pattern);

/// <summary>Usage counters for one waiver, accumulated across every area in a single run.</summary>
internal sealed class WaiverStats
{
    /// <summary>Rows present on both sides whose case id matched this waiver's glob.</summary>
    public int Matched;

    /// <summary>Of those, rows that were not byte-for-byte identical (tolerance-ok or beyond-tolerance).</summary>
    public int Differed;
}

/// <summary>
/// Loads Tools/BaselineVerify/waivers.tsv: one "glob&lt;TAB&gt;PR-number&lt;TAB&gt;reason" per
/// line, all three fields required and non-empty. Malformed lines are a hard load-time
/// failure, not a silent skip.
///
/// Glob syntax is deliberately not shell/gitignore glob: a case id is a run of
/// pipe-delimited fields (e.g. "H|A|23.4392911|-89|0"), so '*' matches within a single
/// field (compiles to <c>[^|]*</c>) and '**' matches across fields (compiles to
/// <c>.*</c>). A bare "H*" therefore matches nothing real -- every case id has more
/// than one field -- which is intentional: it stops a two-character glob from silently
/// sweeping in every area whose prefix starts with the same letter (H, HP, HN, HS, HX,
/// HSUN all start with 'H'). To waive an entire area, spell it out: "H|**".
///
/// A waiver that is exactly "*" or "**", or whose compiled pattern matches the
/// synthetic probe case id below, is rejected at load time: both are catch-alls in
/// disguise and would make the comparison vacuous.
/// </summary>
internal static class Waivers
{
    /// <summary>
    /// A case id no real matrix case can ever produce (area prefixes are short,
    /// hand-written identifiers; this is deliberately long, multi-field, and
    /// nonsensical). Any waiver glob that matches this is too broad to be trusted.
    /// </summary>
    private const string ProbeCaseId = "ZZZ_WAIVER_PROBE|field-one|field-two|field-three|field-four|field-five";

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

            var pattern = ToRegex(glob);
            if (pattern.IsMatch(ProbeCaseId))
            {
                throw new InvalidOperationException(
                    $"{path}:{lineNumber + 1}: waiver glob '{glob}' matches the synthetic probe case id (\"{ProbeCaseId}\") and is too broad. Narrow it.");
            }

            waivers.Add(new Waiver(glob, prNumber, reason, pattern));
        }
        return waivers;
    }

    /// <summary>Fresh, zeroed usage counters for every loaded waiver, to be threaded through every area's comparison.</summary>
    public static Dictionary<Waiver, WaiverStats> InitStats(IReadOnlyList<Waiver> waivers) =>
        waivers.ToDictionary(w => w, _ => new WaiverStats());

    public static Waiver? Match(IReadOnlyList<Waiver> waivers, string caseId)
    {
        foreach (var waiver in waivers)
        {
            if (waiver.Pattern.IsMatch(caseId))
            {
                return waiver;
            }
        }
        return null;
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
