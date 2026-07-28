namespace BaselineVerify;

/// <summary>
/// Derives, from actual row data, the set of case-id glob prefixes (the text before the first
/// '|' in a case id -- see Waivers.CompileGlob) an area actually uses. Exists to close a
/// documentation gap: nothing before this recorded the mapping from an area name (e.g.
/// "gauquelin", "house-pos") to the prefix(es) its case ids start with (e.g. "GQ", "HN"/"HP"),
/// so the only way to discover one was to deliberately write a too-narrow -ExpectedScope glob,
/// let it fail with SCOPE-VIOLATION, and read the OFFENDER lines.
///
/// Deliberately computed from rows rather than hand-maintained, so it can never drift from the
/// code that actually produces case ids: see RunDiffScopeMode's PREFIX lines (computed from the
/// freshly regenerated rows on SCOPE-OK) and Tools/BaselineGen/README.md's prefix table (a
/// snapshot produced by the same method, with instructions to regenerate it).
///
/// An area can and does use more than one prefix -- e.g. "house-pos" mixes HouseName rows
/// ("HN|...") with HousePos rows ("HP|..."), and "houses-armc" mixes "H|...", "HSTATE|...", and
/// "HSUN|...". A single -ExpectedScope glob scoped to just one of an area's prefixes does not
/// cover the others; see Program.cs's RunDiffScopeMode and its scope-magnitude reporting.
/// </summary>
internal static class PrefixMap
{
    /// <summary>
    /// Every distinct case-id prefix present in <paramref name="rows"/>, sorted for stable
    /// output. Each row is "case-id&lt;TAB&gt;...rest of the row"; the case id itself is
    /// "PREFIX|rest-of-case-id". A case id with no '|' at all contributes its whole value as
    /// its own "prefix" (defensive -- every area in this repo uses '|', but nothing enforces
    /// that structurally).
    /// </summary>
    public static IReadOnlyList<string> Discover(IEnumerable<string> rows)
    {
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var tab = row.IndexOf('\t');
            var caseId = tab < 0 ? row : row[..tab];
            var pipe = caseId.IndexOf('|');
            prefixes.Add(pipe < 0 ? caseId : caseId[..pipe]);
        }
        return prefixes.ToList();
    }
}
