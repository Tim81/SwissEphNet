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
