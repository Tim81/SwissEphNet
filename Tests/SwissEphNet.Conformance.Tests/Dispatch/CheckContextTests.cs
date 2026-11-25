using SwissEphNet.Conformance.Tests.Corpus;
using Xunit;

namespace SwissEphNet.Conformance.Tests.Dispatch;

public class CheckContextTests
{
    private static ExpFields FieldsWith(params (string Name, string Value)[] entries)
    {
        var fields = new ExpFields();
        foreach (var (name, value) in entries)
        {
            fields.Set(name, value);
        }

        return fields;
    }

    private static CheckContext ContextFor(ExpFields expected, int suite, int testCase, Precision precision) =>
        new(expected, new TestCaseScope(suite, testCase, precision));

    private static readonly Precision LooseAllTightXx = new(1e-3, [1e-9, 1e-9, 1e-9, 1e-9, 1e-9, 1e-9]);

    [Fact]
    public void CheckDD_UnclassifiedXxPrefixedArray_FallsBackToPerComponentPrecision()
    {
        // checkpoints.c:14 tests strncmp(name,"xx",2)==0 (a prefix test), not name=="xx". A name that
        // tolerances.c does not classify keeps the t.fix precision, which is where that choice shows.
        var expected = FieldsWith(("xxzzz[0]", "1.00000001000"));
        var ctx = ContextFor(expected, 7, 1, LooseAllTightXx);

        // Differs by 1e-8: inside the loose "all" precision (1e-3), outside the tight xx[0] (1e-9).
        ctx.CheckDD("xxzzz", [1.00000002000]);

        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void CheckDD_UnclassifiedNonXxArray_FallsBackToOverallPrecision()
    {
        var expected = FieldsWith(("zzz[0]", "1.00000001000"));
        var ctx = ContextFor(expected, 6, 1, LooseAllTightXx);

        ctx.CheckDD("zzz", [1.00000002000]);

        Assert.Empty(ctx.Mismatches);
    }

    [Fact]
    public void CheckDD_ClassifiedName_UsesTheQuantityToleranceNotTheT_FixPrecision()
    {
        // tolerances.c: cusps[] is an angle, 3e-8 degrees, whatever t.fix says (v2.10.04, f952691).
        var expected = FieldsWith(("cusps[0]", "100.00000000000"));
        var tight = new Precision(1e-12, [1e-12, 1e-12, 1e-12, 1e-12, 1e-12, 1e-12]);

        var within = ContextFor(expected, 6, 1, tight);
        within.CheckDD("cusps", [100.00000001]);   // 1e-8 < 3e-8
        Assert.Empty(within.Mismatches);

        var beyond = ContextFor(expected, 6, 1, LooseAllTightXx);
        beyond.CheckDD("cusps", [100.0000001]);    // 1e-7 > 3e-8, though inside the loose 1e-3
        Assert.Single(beyond.Mismatches);
    }

    [Fact]
    public void CheckD_RelativeFloorWidensTheToleranceForLargeValues()
    {
        // tolerances.c: TOL_RELATIVE * |exp| is a lower limit. A Julian day of 2.5e6 allows
        // 2.5e-8, above TOL_TIME_DAY (1e-8).
        var expected = FieldsWith(("jd", "2500000.0"));

        var inside = ContextFor(expected, 5, 1, Precision.Default);
        inside.CheckD("jd", 2500000.0 + 2.0e-8);
        Assert.Empty(inside.Mismatches);

        var outside = ContextFor(expected, 5, 1, Precision.Default);
        outside.CheckD("jd", 2500000.0 + 3.0e-8);
        Assert.Single(outside.Mismatches);
    }

    [Fact]
    public void CheckD_RadiansFlagSelectsTheRadianAngleTolerance()
    {
        // kind_position() gives Q_ANGLE for xx[0]; with SEFLG_RADIANS (the iteration's iflag) the
        // tolerance is TOL_ANGLE_DEG * pi / 180 = 5.2e-10.
        var expected = FieldsWith(("iflag", "8192"), ("xx[0]", "1.0"));

        var ctx = ContextFor(expected, 1, 1, Precision.Default);
        ctx.CheckD("xx[0]", 1.0 + 1e-9);   // inside 3e-8, outside 5.2e-10
        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void CheckD_XyzFlagMakesComponentTwoADistance()
    {
        var expected = FieldsWith(("iflag", "4096"), ("xx[2]", "1.0"));

        var ctx = ContextFor(expected, 1, 1, Precision.Default);
        ctx.CheckD("xx[2]", 1.0 + 5e-10);   // TOL_DIST_AU is 1e-10
        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void CheckD_ReadingIflagForClassificationDoesNotMarkItConsumed()
    {
        var expected = FieldsWith(("iflag", "0"), ("xx[0]", "1.0"));
        var ctx = ContextFor(expected, 1, 1, Precision.Default);

        ctx.CheckD("xx[0]", 1.0);

        Assert.DoesNotContain("iflag", expected.ConsumedKeys);
    }

    [Fact]
    public void CheckD_NaNFailsEvenWithAGenerousTolerance()
    {
        var expected = FieldsWith(("jd", "1.0"));
        var ctx = ContextFor(expected, 5, 1, Precision.Default);

        ctx.CheckD("jd", double.NaN);

        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void CheckEqualsD_UsesTheSearchToleranceInSuite10()
    {
        // suite_10_solcross.c:31 CHECK_EQUALS_D(xcross, xx[0]): the name is "xcross", Q_SEARCH, 1 mas.
        var ctx = ContextFor(new ExpFields(), 10, 1, Precision.Default);

        ctx.CheckEqualsD("xcross", 100.0, 100.0 + 2.0e-7);   // 2e-7 < 2.78e-7
        Assert.Empty(ctx.Mismatches);

        ctx.CheckEqualsD("xcross", 100.0, 100.0 + 4.0e-7);
        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void RuntimeSelfChecks_AreMarked_AndFileBackedChecksAreNot()
    {
        // CHECK_EQUALS_D / CHECK_EQUALS_I compare two values the run produced: no t.exp side, so the
        // mismatch is marked and the pin gate pins both operands (see PinGate).
        var self = ContextFor(new ExpFields(), 10, 1, Precision.Default);
        self.CheckEqualsD("xcross", 30.0, 36.5);
        self.CheckEqualsI("rc==iflag", 2, 1);
        Assert.All(self.Mismatches, m => Assert.True(m.SelfCheck));
        Assert.Equal("30", self.Mismatches[0].Actual);      // the macro's first operand
        Assert.Equal("36.5", self.Mismatches[0].Expected);  // its second operand
        Assert.Equal("2", self.Mismatches[1].Actual);
        Assert.Equal("1", self.Mismatches[1].Expected);

        var backed = ContextFor(FieldsWith(("jd", "1.0")), 10, 1, Precision.Default);
        backed.CheckD("jd", 2.0);
        Assert.False(Assert.Single(backed.Mismatches).SelfCheck);
    }

    [Fact]
    public void CheckEqualsD_UnclassifiedNameUsesTheOverallPrecision()
    {
        var ctx = ContextFor(new ExpFields(), 11, 1, new Precision(1e-3, [1e-9, 1e-9, 1e-9, 1e-9, 1e-9, 1e-9]));

        ctx.CheckEqualsD("zzz", 1.0, 1.0005);

        Assert.Empty(ctx.Mismatches);
    }

    [Theory]
    [InlineData("xx[0]", 1, 1, 0, 3e-8)]
    [InlineData("xx[1]", 1, 1, 0, 3e-8)]
    [InlineData("xx[2]", 1, 1, 0, 1e-10)]
    [InlineData("xx[3]", 1, 1, 0, 1e-9)]
    [InlineData("xx[1]", 6, 6, 0, 3e-8)]
    [InlineData("xx[5]", 6, 6, 0, 3e-8)]
    [InlineData("xx[2]", 10, 5, 0, 1.0 / 3600000.0)]
    [InlineData("xxdret[1]", 7, 3, 0, 1e-7)]
    [InlineData("xxdret[10]", 7, 3, 0, 1e-8 / 365.25)]
    [InlineData("xxattr[3]", 8, 1, 0, 0.015)]
    [InlineData("xxattr[3]", 8, 9, 0, 1e-7)]
    [InlineData("xxtret[0]", 9, 4, 0, 3e-8)]
    [InlineData("xxtret[0]", 9, 5, 0, 1e-7)]
    [InlineData("xxtret[1]", 9, 5, 0, 3e-8)]
    [InlineData("xxtret[0]", 8, 1, 0, 1e-8)]
    [InlineData("ut", 5, 1, 0, 1e-8 * 24.0)]
    [InlineData("hp", 6, 6, 0, 1e-9)]
    [InlineData("xpn[2]", 11, 1, 0, 1e-10)]
    public void ToleranceFor_ClassifiesLikeTolerancesC(string name, int suite, int testCase, int iflag, double expectedTolerance)
    {
        Assert.Equal(expectedTolerance, Tolerances.tolerance_for(name, 0.0, suite, testCase, iflag), 15);
    }

    [Fact]
    public void ToleranceFor_UnknownNameIsMinusOne()
    {
        Assert.Equal(-1.0, Tolerances.tolerance_for("zzz[0]", 1.0, 1, 1, 0));
        Assert.Equal(-1.0, Tolerances.tolerance_for("xxdret[17]", 1.0, 7, 3, 0));
    }

    [Fact]
    public void SplitName_MirrorsTolerancesC()
    {
        Assert.Equal(3, Tolerances.split_name("xxattr[3]", out var b1));
        Assert.Equal("xxattr", b1);
        Assert.Equal(-1, Tolerances.split_name("tret", out var b2));
        Assert.Equal("tret", b2);
        Assert.Equal(12, Tolerances.split_name("cusps[12]", out _));
    }

    [Fact]
    public void ToleranceConstants_MatchTolerancesC()
    {
        Assert.Equal(3e-8 * System.Math.PI / 180.0, Tolerances.TOL_ANGLE_RAD);
        Assert.Equal(1e-8 * 24.0, Tolerances.TOL_TIME_HOUR);
        Assert.Equal(1e-8 / 365.25, Tolerances.TOL_TIME_YEAR);
    }

    [Fact]
    public void CheckS_NullActual_ComparesAsEmptyString()
    {
        var expected = FieldsWith(("serr", ""));
        var ctx = ContextFor(expected, 3, 1, Precision.Default);

        ctx.CheckS("serr", null);

        Assert.Empty(ctx.Mismatches);
    }
}
