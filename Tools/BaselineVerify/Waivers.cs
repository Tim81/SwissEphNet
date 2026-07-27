using System.Text.RegularExpressions;

namespace BaselineVerify;

/// <summary>One glob-matched waiver: a case-id pattern and the reason it is expected to differ.</summary>
internal sealed record Waiver(string Glob, string Reason, Regex Pattern);

/// <summary>
/// Loads Tools/BaselineVerify/waivers.tsv: one "glob&lt;TAB&gt;reason" per line.
/// '*' in the glob matches any run of characters within a case id. A waived case is
/// reported separately from PASS/FAIL and never fails the run, regardless of what it
/// changed to.
/// </summary>
internal static class Waivers
{
    public static IReadOnlyList<Waiver> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var waivers = new List<Waiver>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var tabIndex = trimmed.IndexOf('\t');
            if (tabIndex < 0)
            {
                continue;
            }

            var glob = trimmed[..tabIndex];
            var reason = trimmed[(tabIndex + 1)..];
            waivers.Add(new Waiver(glob, reason, ToRegex(glob)));
        }
        return waivers;
    }

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
        var pattern = "^" + string.Join(".*", glob.Split('*').Select(Regex.Escape)) + "$";
        return new Regex(pattern, RegexOptions.Compiled);
    }
}
