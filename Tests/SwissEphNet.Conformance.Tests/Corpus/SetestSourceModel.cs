using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwissEphNet.Conformance.Tests.Corpus;

/// <summary>
/// The half of the corpus t.exp does not record: which of an iteration's
/// "name: value" lines the reference tool <em>asserts</em> and which it merely
/// <em>reads back as input</em>.
/// </summary>
/// <remarks>
/// <para>
/// t.exp carries no syntactic marker for this. Inside one ITERATION block,
/// "iflag: 258" (an input the C feeds to swe_calc) and "rc: 258" (a return
/// value the C asserts) are the same shape, so nothing in the data file can
/// tell the completeness guard which keys a dispatcher is obliged to compare.
/// </para>
/// <para>
/// The C makes the distinction mechanically, through two disjoint families of
/// macros (external/swisseph/setest/testsuite_facade.h:14-30, whose real
/// definitions are supplied by testsuite.m4):
/// </para>
/// <list type="bullet">
///   <item><description>GET_I / GET_D / GET_S / GET_P read an <em>input</em> out of the block.</description></item>
///   <item><description>CHECK_D / CHECK_DD / CHECK_I / CHECK_II / CHECK_S emit an <em>expected value</em> the run is compared against.</description></item>
/// </list>
/// <para>
/// So this type recovers the distinction by parsing setest/suite_*.c and
/// setest/globals_suite.c at run time. Deliberately at run time, from the
/// pinned submodule, and never from a table committed to this repo: a recorded
/// copy silently goes stale the moment external/swisseph is bumped, and a
/// stale table here turns the guard that depends on it into a no-op.
/// </para>
/// <para>
/// The CHECK_* set is built per (TESTSUITE id, TESTCASE id), not globally,
/// because the same name is an input in one testcase and an asserted value in
/// another: suite_05_date_time.c:60 reads tjd_lmt with GET_D and asserts
/// tjd_lat, while testcase 6 (line 71) does exactly the reverse. A global set
/// would either miss half the real assertions or reject half the legitimate
/// inputs. Cross-iteration key-set consistency is not assumed anywhere -- it is
/// not an invariant of this corpus (43 of the 60 testcases have iterations
/// whose key sets differ).
/// </para>
/// <para>
/// The GET_* set, by contrast, is global. It is only ever consulted as a
/// permission ("this name is something the C reads somewhere"), never as an
/// obligation, and the per-testcase CHECK_* set is tested first, so a name that
/// is an input in one testcase and asserted in another still resolves
/// correctly.
/// </para>
/// <para>
/// The five shared checkers in globals_suite.c (check_swecalc_results and the
/// four check_swehouses_* variants) are expanded into their call sites: a
/// testcase that calls one inherits that helper's CHECK_* names, and without
/// the expansion suites 1 and 6 would come out very nearly empty. The
/// "if (ihsy == 'G')" branch inside those helpers is not modelled, and does not
/// need to be: it selects an array <em>length</em> (cusps[0..36] vs
/// cusps[0..12]), never a different base name, and this model works at
/// name level.
/// </para>
/// </remarks>
public sealed class SetestSourceModel
{
    // Deliberately literal on "CHECK_D(" / "CHECK_I(" / "CHECK_S(" rather than a
    // "CHECK_.*" wildcard: the CHECK_EQUALS_* family is NOT file-backed (it
    // compares a computed value against another computed value or a literal, see
    // suite_10_solcross.c:29 "CHECK_EQUALS_D(xcross, xx[0])" where xcross is an
    // input read with GET_D), so its operands must never be taken for asserted
    // t.exp names. "CHECK_EQUALS_D(" does not contain the substring "CHECK_D(",
    // so the literal prefix excludes the whole family for free.
    private static readonly Regex CheckScalarRegex =
        new(@"(?<![A-Za-z0-9_])CHECK_(D|I|S)\s*\(\s*([^,()]+?)\s*\)", RegexOptions.Compiled);

    private static readonly Regex CheckArrayRegex =
        new(@"(?<![A-Za-z0-9_])CHECK_(DD|II)\s*\(\s*([^,()]+?)\s*,", RegexOptions.Compiled);

    private static readonly Regex GetRegex =
        new(@"(?<![A-Za-z0-9_])GET_(?:I|D|S|P)\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex TestSuiteRegex =
        new(@"(?<![A-Za-z0-9_])TESTSUITE\s*\(\s*(\d+)\s*,", RegexOptions.Compiled);

    private static readonly Regex TestCaseRegex =
        new(@"(?<![A-Za-z0-9_])TESTCASE\s*\(\s*(\d+)\s*,", RegexOptions.Compiled);

    private static readonly Regex HelperDefinitionRegex =
        new(@"(?m)^[ \t]*(?:void|int|static\s+\w+)\s+(check_[A-Za-z0-9_]+)\s*\([^;{]*\)\s*\{", RegexOptions.Compiled);

    private static readonly Regex HelperCallRegex =
        new(@"(?<![A-Za-z0-9_])(check_[A-Za-z0-9_]+)\s*\(", RegexOptions.Compiled);

    private readonly HashSet<string> _inputNames;

    /// <summary>
    /// How many times each CHECK_* family matched, keyed by the family letters
    /// (D, I, S, DD, II). Exists so <see cref="AssertNonTrivial"/> can see one
    /// family going dark, which the aggregate floors provably cannot.
    /// </summary>
    private readonly Dictionary<string, int> _checkFamilyCounts;

    private static void BumpFamily(Dictionary<string, int> counts, string family)
    {
        counts.TryGetValue(family, out var n);
        counts[family] = n + 1;
    }
    private readonly Dictionary<(int Suite, int TestCase), HashSet<string>> _checkedNames;

    private SetestSourceModel(
        HashSet<string> inputNames,
        Dictionary<(int, int), HashSet<string>> checkedNames,
        IReadOnlyCollection<string> sharedCheckerNames,
        string sourceDirectory,
        Dictionary<string, int> checkFamilyCounts)
    {
        _inputNames = inputNames;
        _checkedNames = checkedNames;
        _checkFamilyCounts = checkFamilyCounts;
        SharedCheckerNames = sharedCheckerNames;
        SourceDirectory = sourceDirectory;
    }

    /// <summary>The setest directory the model was parsed from.</summary>
    public string SourceDirectory { get; }

    /// <summary>Names of the globals_suite.c shared checkers that were expanded.</summary>
    public IReadOnlyCollection<string> SharedCheckerNames { get; }

    /// <summary>Every name the C reads as an input, anywhere, via GET_I/GET_D/GET_S/GET_P.</summary>
    public IReadOnlyCollection<string> InputNames => _inputNames;

    /// <summary>(suite id, testcase id) pairs the model found a TESTCASE block for.</summary>
    public IReadOnlyCollection<(int Suite, int TestCase)> TestCaseKeys => _checkedNames.Keys;

    /// <summary>Distinct names asserted by any CHECK_* macro across every testcase.</summary>
    public IReadOnlyCollection<string> AllCheckedNames =>
        _checkedNames.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Lazily parsed once per process from <see cref="RepoLocator.SetestDir"/>.
    /// Any failure -- missing directory, no suite files, an unrecognised shared
    /// checker, an implausibly small result -- throws here, before the corpus
    /// run starts.
    /// </summary>
    public static SetestSourceModel Default => LazyDefault.Value;

    private static readonly Lazy<SetestSourceModel> LazyDefault =
        new(() => Load(RepoLocator.SetestDir));

    /// <summary>Names the C asserts for this testcase, or null if it has no such TESTCASE block.</summary>
    public IReadOnlyCollection<string>? CheckedNamesFor(int suiteId, int testCaseId) =>
        _checkedNames.TryGetValue((suiteId, testCaseId), out var names) ? names : null;

    /// <summary>
    /// Whether the C asserts <paramref name="key"/> in this testcase. Accepts
    /// either the base name or an indexed element of it, because CHECK_DD(cusps,13)
    /// records the base name "cusps" while t.exp records "cusps[0]".."cusps[12]"
    /// -- and, in the one place the C indexes a CHECK_D directly
    /// (suite_06_houses.c:60, "CHECK_D(xx[0])"), the recorded name already carries
    /// its own subscript, so the literal form is matched too.
    /// </summary>
    public bool IsCheckedBy(int suiteId, int testCaseId, string key)
    {
        if (!_checkedNames.TryGetValue((suiteId, testCaseId), out var names))
        {
            return false;
        }

        return names.Contains(key) || names.Contains(BaseName(key));
    }

    /// <summary>Whether the C reads <paramref name="key"/> as an input anywhere.</summary>
    public bool IsDeclaredInput(string key) =>
        _inputNames.Contains(key) || _inputNames.Contains(BaseName(key));

    /// <summary>"cusps[12]" -> "cusps"; anything without a subscript is returned unchanged.</summary>
    public static string BaseName(string key)
    {
        var bracket = key.IndexOf('[');
        return bracket > 0 && key.EndsWith(']') ? key[..bracket] : key;
    }

    public static SetestSourceModel Load(string setestDirectory) => Load(setestDirectory, applyFloors: true);

    /// <summary>
    /// Same parse, without the corpus-sized non-triviality floors. Exists only
    /// so the parser's edge cases can be exercised against small synthetic
    /// sources; the corpus itself always goes through <see cref="Load(string)"/>.
    /// </summary>
    internal static SetestSourceModel LoadWithoutFloors(string setestDirectory) =>
        Load(setestDirectory, applyFloors: false);

    private static SetestSourceModel Load(string setestDirectory, bool applyFloors)
    {
        // Accumulated across every testcase parsed below, then handed to the model so
        // AssertNonTrivial can require each CHECK_* family to have matched at least once.
        var checkFamilyCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!Directory.Exists(setestDirectory))
        {
            throw new InvalidOperationException(
                $"Cannot build the setest source model: '{setestDirectory}' does not exist. The completeness guard " +
                "needs external/swisseph/setest/*.c on disk (CI gets them from conformance.yml's '/setest/*' " +
                "sparse-checkout pattern). Run 'git submodule update --init external/swisseph'.");
        }

        var suiteFiles = Directory.GetFiles(setestDirectory, "suite_*.c").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (suiteFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot build the setest source model: no suite_*.c files under '{setestDirectory}'. " +
                "An empty parse would silently disable the conformance completeness guard, so this is fatal.");
        }

        var globalsPath = Path.Combine(setestDirectory, "globals_suite.c");
        if (!File.Exists(globalsPath))
        {
            throw new InvalidOperationException(
                $"Cannot build the setest source model: '{globalsPath}' is missing. Suites 1 and 6 assert almost " +
                "everything through its shared checkers, so without it the per-testcase CHECK_* map would be " +
                "badly incomplete rather than merely empty.");
        }

        var helpers = ParseSharedCheckers(File.ReadAllText(globalsPath), globalsPath);

        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        var checkedNames = new Dictionary<(int, int), HashSet<string>>();

        foreach (var file in suiteFiles)
        {
            var text = File.ReadAllText(file);

            var suiteMatch = TestSuiteRegex.Match(text);
            if (!suiteMatch.Success)
            {
                throw new InvalidOperationException(
                    $"Cannot build the setest source model: '{file}' matches suite_*.c but contains no " +
                    "TESTSUITE(<id>, ...) declaration. The file layout this parser assumes has changed.");
            }

            var suiteId = int.Parse(suiteMatch.Groups[1].Value);

            foreach (Match m in GetRegex.Matches(text))
            {
                inputNames.Add(m.Groups[1].Value);
            }

            var testCases = TestCaseRegex.Matches(text);
            if (testCases.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot build the setest source model: '{file}' declares TESTSUITE({suiteId}) but no TESTCASE blocks.");
            }

            for (var i = 0; i < testCases.Count; i++)
            {
                var start = testCases[i].Index;
                var end = i + 1 < testCases.Count ? testCases[i + 1].Index : text.Length;
                var body = text[start..end];
                var testCaseId = int.Parse(testCases[i].Groups[1].Value);

                var names = CollectCheckedNames(body, helpers, file, suiteId, testCaseId, checkFamilyCounts);

                if (!checkedNames.TryAdd((suiteId, testCaseId), names))
                {
                    throw new InvalidOperationException(
                        $"Cannot build the setest source model: TESTCASE({testCaseId}) appears twice under " +
                        $"TESTSUITE({suiteId}) (second occurrence in '{file}').");
                }
            }
        }

        var model = new SetestSourceModel(inputNames, checkedNames, helpers.Keys.ToList(), setestDirectory, checkFamilyCounts);
        if (applyFloors)
        {
            model.AssertNonTrivial();
        }

        return model;
    }

    /// <summary>
    /// Floors, not exact counts. Measured against the pinned submodule
    /// (v2.10.3final, unchanged in v2.10.3bfinal -- setest/* is byte-identical between the
    /// two tags): 39 distinct GET_* input names, 46 distinct CHECK_* names
    /// once an indexed form like "xx[0]" is folded onto its base, 60 testcases
    /// across 10 suites, 5 shared checkers. The floors sit below those so an
    /// upstream bump that adds or renames a testcase does not trip them, but far
    /// enough above zero that a parser which silently stops matching -- the
    /// failure mode that would turn the guard into a permanent no-op -- cannot
    /// get past this.
    /// </summary>
    private void AssertNonTrivial()
    {
        var problems = new List<string>();

        if (_inputNames.Count < 30)
        {
            problems.Add($"only {_inputNames.Count} GET_* input name(s) extracted (expected ~39, floor 30)");
        }

        var distinctChecked = AllCheckedNames.Count;
        if (distinctChecked < 40)
        {
            problems.Add($"only {distinctChecked} distinct CHECK_* name(s) extracted (expected ~47, floor 40)");
        }

        // Per family, because the aggregate floor above cannot see one family stop
        // matching. Measured against v2.10.3final: CHECK_D 38 occurrences, CHECK_I 41,
        // CHECK_S 23, CHECK_DD 46. Blinding CHECK_S alone still leaves 42 distinct names
        // and blinding CHECK_I leaves 41, both of which clear a floor of 40 -- so the
        // aggregate count would report a healthy parse while a whole family of assertions
        // had silently stopped being extracted, and every field only that family asserts
        // would drop out of the guard unnoticed.
        //
        // The floor is presence, not a count: one occurrence is enough to prove the family
        // still matches, and anything higher would have to be revised on an upstream bump
        // that merely moves assertions around. CHECK_II is deliberately absent from this
        // list -- it is a real macro (testsuite.m4:76-89) with zero uses at v2.10.3final
        // (still zero at v2.10.3bfinal; testsuite.m4 is byte-identical between the two
        // tags), so requiring it would fail on a correct parse of the pinned source.
        foreach (var family in new[] { "D", "I", "S", "DD" })
        {
            _checkFamilyCounts.TryGetValue(family, out var used);
            if (used == 0)
            {
                problems.Add(
                    $"CHECK_{family}(...) matched nothing. Every other floor here can be cleared with " +
                    "this family missing entirely, so its absence means the extraction is broken rather " +
                    "than that upstream stopped using it.");
            }
        }

        if (_checkedNames.Count < 55)
        {
            problems.Add($"only {_checkedNames.Count} TESTCASE block(s) mapped (expected 60, floor 55)");
        }

        var suiteCount = _checkedNames.Keys.Select(k => k.Suite).Distinct().Count();
        if (suiteCount < 10)
        {
            problems.Add($"only {suiteCount} TESTSUITE(s) mapped (expected 10)");
        }

        if (SharedCheckerNames.Count < 5)
        {
            problems.Add($"only {SharedCheckerNames.Count} shared checker(s) found in globals_suite.c (expected 5)");
        }

        // A map whose every entry is empty parses "successfully" and asserts
        // nothing -- exactly the silent-no-op shape the floors above exist to
        // stop, so it gets its own check rather than relying on the totals.
        var nonEmpty = _checkedNames.Count(kv => kv.Value.Count > 0);
        if (nonEmpty < 55)
        {
            problems.Add($"only {nonEmpty} of {_checkedNames.Count} mapped testcases have any CHECK_* name at all (floor 55)");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The setest source model parsed from '{SourceDirectory}' is implausibly small, which would silently " +
                "disable the conformance completeness guard rather than fail it: " + string.Join("; ", problems) +
                ". Either the submodule checkout is incomplete or setest's source layout changed and this parser " +
                "needs updating -- do not lower these floors to get past it.");
        }
    }

    private static Dictionary<string, string> ParseSharedCheckers(string text, string path)
    {
        var helpers = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = HelperDefinitionRegex.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            helpers[matches[i].Groups[1].Value] = text[start..end];
        }

        if (helpers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot build the setest source model: no shared checker definitions found in '{path}'.");
        }

        return helpers;
    }

    /// <summary>
    /// CHECK_* names in <paramref name="body"/>, with every shared-checker call
    /// expanded transitively. An unrecognised check_* call is fatal rather than
    /// ignored: silently skipping it would drop that helper's whole set of
    /// asserted names for every testcase that calls it, which is precisely the
    /// hole this model exists to close.
    /// </summary>
    private static HashSet<string> CollectCheckedNames(
        string body,
        IReadOnlyDictionary<string, string> helpers,
        string file,
        int suiteId,
        int testCaseId,
        Dictionary<string, int> familyCounts)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(body);

        while (pending.Count > 0)
        {
            var text = pending.Dequeue();

            foreach (Match m in CheckScalarRegex.Matches(text))
            {
                names.Add(m.Groups[2].Value);
                BumpFamily(familyCounts, m.Groups[1].Value);
            }

            foreach (Match m in CheckArrayRegex.Matches(text))
            {
                names.Add(m.Groups[2].Value);
                BumpFamily(familyCounts, m.Groups[1].Value);
            }

            foreach (Match m in HelperCallRegex.Matches(text))
            {
                var helper = m.Groups[1].Value;
                if (!helpers.TryGetValue(helper, out var helperBody))
                {
                    throw new InvalidOperationException(
                        $"Cannot build the setest source model: TESTCASE({testCaseId}) of TESTSUITE({suiteId}) in " +
                        $"'{file}' calls '{helper}', which is not one of the shared checkers defined in " +
                        $"globals_suite.c ({string.Join(", ", helpers.Keys)}). Its CHECK_* names would be lost, so " +
                        "this parser must be taught about it rather than skipping it.");
                }

                if (expanded.Add(helper))
                {
                    pending.Enqueue(helperBody);
                }
            }
        }

        return names;
    }
}
