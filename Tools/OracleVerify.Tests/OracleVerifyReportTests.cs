using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// OracleVerifyReport.Build's three-way check: a differing row absent from known-diff.tsv (or
/// listed under a category whose failure shape no longer fits) is a regression; a listed row that
/// now matches outright must be pruned (NewlyPassing); a listed row whose case_id has fallen out of
/// the grid is stale. Also the magnitude gate (RegressionKind.UlpGrew) and the categorical-state
/// flip this comparer is deliberately stricter about than the correctness oracle's known-fail.tsv.
/// </summary>
public class OracleVerifyReportTests
{
    private static RowOutcome Matching(string caseId) => new()
    {
        CaseId = caseId,
        CRetc = 0,
        NetRetc = 0,
        CErr = "",
        NetErr = "",
        FieldDiffs = [],
    };

    private static RowOutcome Differing(string caseId, ulong ulp) => new()
    {
        CaseId = caseId,
        CRetc = 0,
        NetRetc = 0,
        CErr = "",
        NetErr = "",
        FieldDiffs = [new FieldDiff(0, "xx[0]", 1.0, 1.0 + ulp * 1e-10, ulp)],
    };

    private static RowOutcome CategoricalDiffering(string caseId) => new()
    {
        CaseId = caseId,
        CRetc = 0,
        NetRetc = 0,
        CErr = "",
        NetErr = "",
        FieldDiffs = [new FieldDiff(0, "xx[0]", double.NaN, 1.0, UlpMath.CategoricalDistance)],
    };

    [Fact]
    public void A_differing_row_with_no_known_diff_entry_is_a_NotListed_regression()
    {
        var report = OracleVerifyReport.Build([Differing("A|1", 4)], new Dictionary<string, KnownDiffEntry>());
        var regression = Assert.Single(report.Regressions);
        Assert.Equal(RegressionKind.NotListed, regression.Kind);
        Assert.False(report.Passed);
    }

    [Fact]
    public void A_listed_row_that_now_matches_outright_is_reported_as_NewlyPassing()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, 4, "lon differs"),
        };
        var report = OracleVerifyReport.Build([Matching("A|1")], knownDiff);
        Assert.Empty(report.Regressions);
        var newlyPassing = Assert.Single(report.NewlyPassing);
        Assert.Equal("A|1", newlyPassing.CaseId);
        Assert.False(report.Passed);
    }

    [Fact]
    public void A_listed_case_id_absent_from_the_grid_is_Stale()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["GONE|1"] = new KnownDiffEntry("GONE|1", DiffCategory.PortVersion, 4, "lon differs"),
        };
        var report = OracleVerifyReport.Build([Matching("A|1")], knownDiff);
        var stale = Assert.Single(report.Stale);
        Assert.Equal("GONE|1", stale.CaseId);
        Assert.False(report.Passed);
    }

    [Fact]
    public void A_row_listed_under_a_category_whose_shape_no_longer_fits_is_a_CategoryMismatch()
    {
        // Listed as RETC (expects RetcDiffers) but the retc now matches and only a hex value
        // differs -- the category's claimed failure shape and the row's actual one disagree.
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.Retc, 0, "retc differed"),
        };
        var report = OracleVerifyReport.Build([Differing("A|1", 4)], knownDiff);
        var regression = Assert.Single(report.Regressions);
        Assert.Equal(RegressionKind.CategoryMismatch, regression.Kind);
    }

    [Fact]
    public void A_row_whose_current_ulp_exceeds_the_recorded_max_ulp_is_UlpGrew()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, 4, "lon differs"),
        };
        var report = OracleVerifyReport.Build([Differing("A|1", 9)], knownDiff);
        var regression = Assert.Single(report.Regressions);
        Assert.Equal(RegressionKind.UlpGrew, regression.Kind);
    }

    [Fact]
    public void A_row_whose_ulp_shrank_but_is_still_nonzero_is_not_a_regression()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, 9, "lon differs"),
        };
        var report = OracleVerifyReport.Build([Differing("A|1", 4)], knownDiff);
        Assert.Empty(report.Regressions);
        Assert.True(report.Passed);
    }

    [Fact]
    public void A_row_recorded_as_categorical_that_is_now_only_finite_is_CategoricalStateChanged()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            // MaxUlp: null means "recorded as categorical" -- see KnownDiffEntry.MaxUlp's remarks.
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, null, "NaN on one side"),
        };
        var report = OracleVerifyReport.Build([Differing("A|1", 4)], knownDiff);
        var regression = Assert.Single(report.Regressions);
        Assert.Equal(RegressionKind.CategoricalStateChanged, regression.Kind);
    }

    [Fact]
    public void A_row_recorded_numerically_that_is_now_categorical_is_also_CategoricalStateChanged()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, 4, "lon differs"),
        };
        var report = OracleVerifyReport.Build([CategoricalDiffering("A|1")], knownDiff);
        var regression = Assert.Single(report.Regressions);
        Assert.Equal(RegressionKind.CategoricalStateChanged, regression.Kind);
    }

    [Fact]
    public void A_row_that_still_matches_the_shape_and_magnitude_it_was_recorded_under_passes()
    {
        var knownDiff = new Dictionary<string, KnownDiffEntry>
        {
            ["A|1"] = new KnownDiffEntry("A|1", DiffCategory.PortVersion, 4, "lon differs"),
        };
        var report = OracleVerifyReport.Build([Differing("A|1", 4)], knownDiff);
        Assert.Empty(report.Regressions);
        Assert.Empty(report.NewlyPassing);
        Assert.Empty(report.Stale);
        Assert.True(report.Passed);
    }
}
