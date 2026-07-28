using System.Text.RegularExpressions;
using BaselineVerify;
using Xunit;

namespace BaselineVerify.Tests;

public class ScopeDiffTests
{
    private static List<(string Glob, Regex Pattern)> Compile(params string[] globs) =>
        globs.Select(g => (g, Waivers.CompileGlob(g, "test", "-ExpectedScope glob"))).ToList();

    [Fact]
    public void ComputeArea_ClassifiesAddedRemovedAndChangedRows()
    {
        string[] oldRows = ["H|A|1\tfoo\tbar", "H|B|1\tfoo\tbar"];
        string[] newRows = ["H|A|1\tfoo\tCHANGED", "H|C|1\tfoo\tbar"];

        var result = ScopeDiff.ComputeArea("houses", oldRows, newRows, Compile("H|**"));

        Assert.Equal(1, result.Changed); // H|A|1
        Assert.Equal(1, result.Added);   // H|C|1
        Assert.Equal(1, result.Removed); // H|B|1
        Assert.Empty(result.Offenders);  // all covered by H|**
        Assert.Equal(2, result.NewRowCount);
    }

    [Fact]
    public void ComputeArea_UnchangedRowsAreNeitherCountedNorOffenders()
    {
        string[] oldRows = ["H|A|1\tfoo\tbar"];
        string[] newRows = ["H|A|1\tfoo\tbar"];

        var result = ScopeDiff.ComputeArea("houses", oldRows, newRows, []);

        Assert.Equal(0, result.Changed + result.Added + result.Removed);
        Assert.Empty(result.Offenders);
    }

    [Fact]
    public void ComputeArea_EmptyScope_EveryChangeIsAnOffender()
    {
        // An empty glob list (Cli.Parse itself refuses this at the CLI boundary, but the pure
        // diff function should still fail closed on its own: nothing matches anything, so
        // every changed/added/removed id is reported as an offender, not silently accepted).
        string[] oldRows = ["H|A|1\tfoo\tbar"];
        string[] newRows = ["H|A|1\tfoo\tCHANGED"];

        var result = ScopeDiff.ComputeArea("houses", oldRows, newRows, []);

        Assert.Single(result.Offenders);
        Assert.Contains("H|A|1", result.Offenders[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeArea_ScopeMatchingNothing_EveryChangeIsAnOffender()
    {
        string[] oldRows = ["H|A|1\tfoo\tbar"];
        string[] newRows = ["H|A|1\tfoo\tCHANGED"];

        // A glob for a wholly unrelated area -- matches zero of the ids that actually changed.
        var result = ScopeDiff.ComputeArea("houses", oldRows, newRows, Compile("C|**"));

        Assert.Single(result.Offenders);
    }

    [Fact]
    public void ComputeArea_CaseIdWithLiteralCommaAndRegexMetacharacters_MatchesExactLiteralGlob()
    {
        // 2,782 of 106,095 real case ids contain a literal comma; several areas' case ids also
        // contain regex metacharacters ('.', '(', ')', '+') as ordinary literal text (e.g. a
        // formatted coordinate or a parenthesized qualifier). Waivers.ToRegex escapes every
        // literal character via Regex.Escape, so a glob written with the same literal text
        // must match the row exactly and never behave like a regex special character.
        var caseId = "calc|defaulteph|1,2|(3.4+5)|probe";
        string[] oldRows = [$"{caseId}\tfoo\tbar"];
        string[] newRows = [$"{caseId}\tfoo\tCHANGED"];

        // The glob is the case id's literal area/subfield prefix, followed by a field-local '*'.
        var result = ScopeDiff.ComputeArea("calc-defaulteph", oldRows, newRows, Compile("calc|defaulteph|1,2|(3.4+5)|*obe"));

        Assert.Empty(result.Offenders);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void ComputeArea_CaseIdWithCommaNotCoveredByGlob_IsAnOffender()
    {
        var caseId = "calc|defaulteph|1,2|x";
        string[] oldRows = [$"{caseId}\tfoo\tbar"];
        string[] newRows = [$"{caseId}\tfoo\tCHANGED"];

        // A glob that (mis)covers a different literal comma shape must not match this one.
        var result = ScopeDiff.ComputeArea("calc-defaulteph", oldRows, newRows, Compile("calc|defaulteph|9,9|**"));

        Assert.Single(result.Offenders);
        Assert.Contains(caseId, result.Offenders[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeArea_MissingRowOnOneSideIsClassifiedByPresence()
    {
        string[] oldRows = [];
        string[] newRows = ["H|A|1\tfoo\tbar"];

        var result = ScopeDiff.ComputeArea("houses", oldRows, newRows, Compile("H|**"));

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Changed);
        Assert.Equal(0, result.Removed);
        Assert.Equal(1, result.NewRowCount);
    }
}
