using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SwissEphNet.Conformance.Tests.Corpus;

/// <summary>
/// Covers <see cref="SetestSourceModel"/> against the pinned submodule and
/// against synthetic sources that reproduce the parser's sharp edges (the
/// CHECK_EQUALS_* family, the shared checkers, an empty parse).
/// </summary>
public class SetestSourceModelTests
{
    [Fact]
    public void PinnedSubmodule_YieldsTheMeasuredNameSets()
    {
        var model = SetestSourceModel.Default;

        // Measured against external/swisseph @ v2.10.3final, still exact at v2.10.3bfinal
        // (setest/* is byte-identical between the two tags). Asserted exactly,
        // not as floors: the floors inside Load exist to catch a parser that
        // stopped matching, this exists to catch one that started matching the
        // wrong thing. If a submodule bump moves these, update them and say so
        // in the commit -- do not relax them into inequalities.
        Assert.Equal(39, model.InputNames.Count);
        Assert.Equal(60, model.TestCaseKeys.Count);
        Assert.Equal(10, model.TestCaseKeys.Select(k => k.Suite).Distinct().Count());
        Assert.Equal(5, model.SharedCheckerNames.Count);

        // 47 literal names; 46 once "xx[0]" (suite_06_houses.c:60, the only
        // CHECK_D whose operand carries its own subscript) folds onto "xx".
        Assert.Equal(47, model.AllCheckedNames.Count);
        Assert.Equal(
            46,
            model.AllCheckedNames.Select(SetestSourceModel.BaseName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SharedCheckersAreExpandedIntoTheirCallSites()
    {
        var model = SetestSourceModel.Default;

        // suite_01_calc.c testcase 1 contains no CHECK_* of its own -- every
        // name it asserts comes from check_swecalc_results (globals_suite.c:5).
        Assert.Equal(["rc", "serr", "xx"], model.CheckedNamesFor(1, 1)!.OrderBy(n => n, StringComparer.Ordinal));

        // Likewise suite_06_houses.c testcase 8 via check_swehouses_ex2_results.
        Assert.Equal(
            ["ascmc", "ascmc_speed", "cusp_speed", "cusps", "jd_ut", "rc"],
            model.CheckedNamesFor(6, 8)!.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void SameNameIsInputInOneTestcaseAndAssertedInAnother()
    {
        var model = SetestSourceModel.Default;

        // suite_05_date_time.c:60/65 -- testcase 5 reads tjd_lmt (GET_D) and
        // asserts tjd_lat; testcase 6 (lines 71/75) does the exact reverse.
        // This is why the CHECK_* map is per-testcase and not global.
        Assert.True(model.IsCheckedBy(5, 5, "tjd_lat"));
        Assert.False(model.IsCheckedBy(5, 5, "tjd_lmt"));
        Assert.True(model.IsCheckedBy(5, 6, "tjd_lmt"));
        Assert.False(model.IsCheckedBy(5, 6, "tjd_lat"));

        // Both are declared inputs somewhere, so the guard's input permission
        // alone would have let a dropped comparison through in both testcases.
        Assert.True(model.IsDeclaredInput("tjd_lat"));
        Assert.True(model.IsDeclaredInput("tjd_lmt"));
    }

    [Fact]
    public void IndexedCorpusKeysResolveToTheirCheckDdBaseName()
    {
        var model = SetestSourceModel.Default;

        // CHECK_DD(cusps,13) records "cusps"; t.exp records cusps[0]..cusps[12].
        Assert.True(model.IsCheckedBy(6, 1, "cusps[0]"));
        Assert.True(model.IsCheckedBy(6, 1, "cusps[12]"));
        Assert.True(model.IsCheckedBy(1, 1, "xx[5]"));

        // suite_06_houses.c:60 asserts the literal name "xx[0]" and nothing else
        // from that array, so a hypothetical xx[1] there is not covered.
        Assert.True(model.IsCheckedBy(6, 6, "xx[0]"));
        Assert.False(model.IsCheckedBy(6, 6, "xx[1]"));
    }

    [Fact]
    public void CheckEqualsFamilyIsNotMistakenForAnAssertedName()
    {
        // suite_10_solcross.c:24-29 in miniature: xcross is an *input*, read with
        // GET_D and then fed to CHECK_EQUALS_D as the value under test. Treating
        // CHECK_EQUALS_D's operand as an asserted t.exp name would oblige the
        // dispatcher to compare a field the C never records an expectation for.
        var model = LoadSynthetic(
            suiteSource: """
                TESTSUITE(10,"synthetic")
                TESTCASE(1,"crossing") {
                  double xcross = GET_D(xcross);
                  int rc = swe_solcross(xcross, jd, iflag, serr);
                  CHECK_I(rc);
                  CHECK_D(jx);
                  CHECK_EQUALS_D(xcross, xx[0]);
                  CHECK_EQUALS_I(rc, iflag);
                  CHECK_S(serr);
                }
                END_TESTSUITE
                """);

        Assert.Equal(["jx", "rc", "serr"], model.CheckedNamesFor(10, 1)!.OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(model.IsDeclaredInput("xcross"));
        Assert.False(model.IsCheckedBy(10, 1, "xcross"));
    }

    [Fact]
    public void UnknownSharedCheckerIsFatal()
    {
        // An upstream revision that adds a sixth helper must not silently drop
        // that helper's asserted names for every testcase calling it.
        var ex = Assert.Throws<InvalidOperationException>(() => LoadSynthetic(
            suiteSource: """
                TESTSUITE(1,"synthetic")
                TESTCASE(1,"calls an unknown helper") {
                  check_something_new(rc,xx,serr,ctx);
                }
                END_TESTSUITE
                """));

        Assert.Contains("check_something_new", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyParseIsFatalRatherThanASilentNoOp()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "globals_suite.c"), "void check_nothing(int rc) {\n  CHECK_I(rc);\n}\n");
            var ex = Assert.Throws<InvalidOperationException>(() => SetestSourceModel.Load(dir));
            Assert.Contains("no suite_*.c files", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ImplausiblySmallParseIsFatal()
    {
        // Parses cleanly, but yields one suite and two names -- the shape a
        // half-broken regex or a truncated checkout would produce.
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "globals_suite.c"), "void check_nothing(int rc) {\n  CHECK_I(rc);\n}\n");
            File.WriteAllText(
                Path.Combine(dir, "suite_01_tiny.c"),
                "TESTSUITE(1,\"tiny\")\nTESTCASE(1,\"tiny\") {\n  int ipl = GET_I(ipl);\n  CHECK_I(rc);\n}\nEND_TESTSUITE\n");

            var ex = Assert.Throws<InvalidOperationException>(() => SetestSourceModel.Load(dir));
            Assert.Contains("implausibly small", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SuiteWithNoCheckedNameAtAllIsFatalEvenWhenAggregateFloorsPass()
    {
        // Reproduces the gap a reviewer demonstrated by mutation: making CollectCheckedNames
        // return an empty set for one suiteId (here, suite 1) does not move distinctChecked or
        // nonEmpty at all, because that suite's real names ("rc", "serr", "xx") are also asserted
        // by other suites in this synthetic set -- so the aggregate floors alone cannot see suite
        // 1 go dark. Every other floor is sized to pass: 30 GET_* input names, 46 distinct CHECK_*
        // names (>= the 40 floor), 59 mapped testcases across 10 suites (5 + 9*6, comfortably
        // above the 55 floor -- see the exact count asserted just below), 5 shared checkers, all
        // four CHECK_D/I/S/DD families present, and every testcase in suites 2-10 carries a
        // non-empty set -- only suite 1's five testcases are empty.
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 30; i++)
        {
            inputNames.Add($"input{i}");
        }

        var checkedNames = new Dictionary<(int Suite, int TestCase), HashSet<string>>();
        // Suite 1: five testcases, every one mapped but empty -- exactly what the described
        // mutation (CollectCheckedNames returning {} for suiteId == 1) produces.
        for (var tc = 1; tc <= 5; tc++)
        {
            checkedNames[(1, tc)] = new HashSet<string>(StringComparer.Ordinal);
        }

        // Suites 2-10: 50 more testcases (55 total, clearing the "testcases mapped" and "suite
        // count" floors), each asserting names shared with what suite 1 would otherwise have
        // asserted ("rc", "serr", "xx") plus enough distinct names to clear the 40-name floor.
        var distinctNames = new List<string> { "rc", "serr", "xx" };
        for (var i = 0; i < 43; i++)
        {
            distinctNames.Add($"checked{i}");
        }

        var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["D"] = 1,
            ["I"] = 1,
            ["S"] = 1,
            ["DD"] = 1,
        };

        var nameIndex = 0;
        for (var suite = 2; suite <= 10; suite++)
        {
            for (var tc = 1; tc <= 6; tc++)
            {
                var names = new HashSet<string>(StringComparer.Ordinal) { "rc", "serr", "xx" };
                names.Add(distinctNames[3 + (nameIndex % (distinctNames.Count - 3))]);
                nameIndex++;
                checkedNames[(suite, tc)] = names;
            }
        }

        // 5 (suite 1) + 9*6 (suites 2-10) = 59 mapped testcases, well above the floor of 55.
        Assert.True(checkedNames.Count >= 55);
        Assert.Equal(
            46,
            checkedNames.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count());

        var model = SetestSourceModel.BuildForTesting(
            inputNames,
            checkedNames,
            sharedCheckerNames: new[] { "a", "b", "c", "d", "e" },
            checkFamilyCounts: familyCounts);

        var ex = Assert.Throws<InvalidOperationException>(() => model.AssertNonTrivialForTesting());
        Assert.Contains("TESTSUITE(s) 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no testcase with any CHECK_* name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleTestCaseWithNoCheckedNameIsFatalEvenWhenItsOwnSuiteHasOthers()
    {
        // Reproduces the narrower gap the per-suite floor above cannot see: blinding just one
        // testcase (here, suite 6 testcase 3, matching the real corpus's own TESTSUITE(6)/
        // TESTCASE(3) -- 1,080 iterations) inside a suite that still has other, non-empty
        // testcases moves neither distinctChecked/nonEmpty (the aggregate floors) nor
        // suitesWithoutACheckedName (the per-suite floor), because suite 6's other five
        // testcases keep it off that list. Every other floor is sized to pass the same way
        // SuiteWithNoCheckedNameAtAllIsFatalEvenWhenAggregateFloorsPass above does.
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 30; i++)
        {
            inputNames.Add($"input{i}");
        }

        var checkedNames = new Dictionary<(int Suite, int TestCase), HashSet<string>>();
        var distinctNames = new List<string> { "rc", "serr", "xx" };
        for (var i = 0; i < 43; i++)
        {
            distinctNames.Add($"checked{i}");
        }

        var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["D"] = 1,
            ["I"] = 1,
            ["S"] = 1,
            ["DD"] = 1,
        };

        var nameIndex = 0;
        for (var suite = 1; suite <= 10; suite++)
        {
            for (var tc = 1; tc <= 6; tc++)
            {
                // Suite 6 testcase 3 is the one blinded testcase -- everything else, including
                // every other testcase in suite 6, gets a real, non-empty set.
                if (suite == 6 && tc == 3)
                {
                    checkedNames[(suite, tc)] = new HashSet<string>(StringComparer.Ordinal);
                    continue;
                }

                var names = new HashSet<string>(StringComparer.Ordinal) { "rc", "serr", "xx" };
                names.Add(distinctNames[3 + (nameIndex % (distinctNames.Count - 3))]);
                nameIndex++;
                checkedNames[(suite, tc)] = names;
            }
        }

        // 10*6 = 60 mapped testcases, well above the floor of 55.
        Assert.True(checkedNames.Count >= 55);
        Assert.Equal(
            46,
            checkedNames.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count());

        var model = SetestSourceModel.BuildForTesting(
            inputNames,
            checkedNames,
            sharedCheckerNames: new[] { "a", "b", "c", "d", "e" },
            checkFamilyCounts: familyCounts);

        var ex = Assert.Throws<InvalidOperationException>(() => model.AssertNonTrivialForTesting());
        Assert.Contains("TESTSUITE(6)/TESTCASE(3)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no CHECK_* name at all", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownLegitimateEmptyTestCaseDoesNotTripThePerTestCaseFloor()
    {
        // TESTSUITE(1)/TESTCASE(4) is the one testcase the pinned corpus itself leaves with an
        // empty CHECK_* set (CHECK_EQUALS_I only -- see PinnedSubmodule_YieldsTheMeasuredNameSets
        // and CheckEqualsFamilyIsNotMistakenForAnAssertedName above), so the per-testcase floor
        // must tolerate exactly that one exception without also going blind to a second, real gap
        // elsewhere. This mirrors SingleTestCaseWithNoCheckedNameIsFatalEvenWhenItsOwnSuiteHasOthers
        // but leaves (1, 4) empty (the tolerated exception) while every other testcase, including
        // every other testcase in suite 1, is non-empty -- so a clean run here proves the
        // exception is honoured, not that the floor is silently disabled.
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 30; i++)
        {
            inputNames.Add($"input{i}");
        }

        var checkedNames = new Dictionary<(int Suite, int TestCase), HashSet<string>>();
        var distinctNames = new List<string> { "rc", "serr", "xx" };
        for (var i = 0; i < 43; i++)
        {
            distinctNames.Add($"checked{i}");
        }

        var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["D"] = 1,
            ["I"] = 1,
            ["S"] = 1,
            ["DD"] = 1,
        };

        var nameIndex = 0;
        for (var suite = 1; suite <= 10; suite++)
        {
            for (var tc = 1; tc <= 6; tc++)
            {
                if (suite == 1 && tc == 4)
                {
                    checkedNames[(suite, tc)] = new HashSet<string>(StringComparer.Ordinal);
                    continue;
                }

                var names = new HashSet<string>(StringComparer.Ordinal) { "rc", "serr", "xx" };
                names.Add(distinctNames[3 + (nameIndex % (distinctNames.Count - 3))]);
                nameIndex++;
                checkedNames[(suite, tc)] = names;
            }
        }

        Assert.True(checkedNames.Count >= 55);

        var model = SetestSourceModel.BuildForTesting(
            inputNames,
            checkedNames,
            sharedCheckerNames: new[] { "a", "b", "c", "d", "e" },
            checkFamilyCounts: familyCounts);

        // Must not throw: (1, 4) is the one named, tolerated exception.
        model.AssertNonTrivialForTesting();
    }

    private static SetestSourceModel LoadSynthetic(string suiteSource)
    {
        var dir = NewTempDir();
        try
        {
            // The real globals_suite.c, so the synthetic case exercises the same
            // shared-checker expansion the corpus does.
            File.Copy(
                Path.Combine(RepoLocator.SetestDir, "globals_suite.c"),
                Path.Combine(dir, "globals_suite.c"));
            File.WriteAllText(Path.Combine(dir, "suite_99_synthetic.c"), suiteSource);

            // Non-triviality floors are a property of the real corpus, so the
            // synthetic sources are parsed through the same entry point but only
            // ever inspected for their name sets -- LoadWithoutFloors skips the floors.
            return SetestSourceModel.LoadWithoutFloors(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "setest-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
