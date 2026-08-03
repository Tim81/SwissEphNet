using OracleVerify;
using Xunit;

namespace OracleVerify.Tests;

/// <summary>
/// UlpMath.Distance compares raw 64-bit patterns, not double.Equals (see its own remarks) -- the
/// -0.0/+0.0 and same-value-different-hex cases below are the ones the review measured directly:
/// flipping a dump row's hex from 0000000000000000 to 8000000000000000 produced zero detected
/// field diffs under an earlier double.Equals-based check.
/// </summary>
public class UlpMathTests
{
    [Fact]
    public void Distance_is_zero_for_bit_identical_values()
    {
        Assert.Equal(0UL, UlpMath.Distance(1.5, 1.5));
    }

    [Fact]
    public void Distance_is_nonzero_between_negative_zero_and_positive_zero()
    {
        // Same double.Equals-visible value, different bit patterns (0x8000000000000000 vs
        // 0x0000000000000000) -- this is the exact reproduction from UlpMath.cs's own remarks.
        Assert.NotEqual(0UL, UlpMath.Distance(-0.0, 0.0));
    }

    [Fact]
    public void Distance_is_categorical_for_a_nan_and_a_finite_value()
    {
        Assert.Equal(UlpMath.CategoricalDistance, UlpMath.Distance(double.NaN, 1.0));
        Assert.Equal(UlpMath.CategoricalDistance, UlpMath.Distance(1.0, double.NaN));
    }

    [Fact]
    public void Distance_is_categorical_for_two_different_nan_payloads()
    {
        var nanA = BitConverter.Int64BitsToDouble(0x7FF8000000000001);
        var nanB = BitConverter.Int64BitsToDouble(0x7FF8000000000002);
        Assert.Equal(UlpMath.CategoricalDistance, UlpMath.Distance(nanA, nanB));
    }

    [Fact]
    public void Distance_of_adjacent_representable_doubles_is_one()
    {
        var a = 1.0;
        var b = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(a) + 1);
        Assert.Equal(1UL, UlpMath.Distance(a, b));
        Assert.Equal(1UL, UlpMath.Distance(b, a));
    }

    [Fact]
    public void Distance_is_symmetric_across_the_zero_boundary()
    {
        // MEDIUM 4: asserted against an independently-derived expected magnitude, not just the two
        // computed calls against each other -- Distance(a,b) == Distance(b,a) alone would still
        // pass if Distance had, say, a sign or ordering bug applied identically on both sides, or
        // even always returned the same constant regardless of its arguments. 9214364837600034817
        // was computed once, offline (1.0's totalOrder key 0xBFF0000000000000 -- 1.0's raw bits
        // 0x3FF0000000000000 with the sign bit set to mark it non-negative -- minus -1.0's
        // totalOrder key 0x400FFFFFFFFFFFFF, the ones' complement of -1.0's own raw bits
        // 0xBFF0000000000000, per UlpMath's own remarks on the totalOrder transform), and is
        // pinned here as a literal rather than recomputed from BitConverter calls in this test, so
        // this assertion does not just restate the implementation under test with different syntax.
        const ulong expected = 9214364837600034817UL;
        Assert.Equal(expected, UlpMath.Distance(-1.0, 1.0));
        Assert.Equal(expected, UlpMath.Distance(1.0, -1.0));
    }

    [Fact]
    public void IsUnrelated_is_false_for_an_exactly_equal_pair()
    {
        // MEDIUM 4: this input alone passes even with IsUnrelated's leading `a.Equals(b)` guard
        // deleted -- for a==b, scale = Math.Abs(a) and Math.Abs(a-b) is exactly 0, so
        // `0 > UnrelatedRelativeThreshold * scale` is false regardless of that guard, and the
        // method falls through to the same answer by arithmetic alone. Measured directly (guard
        // removed, this assertion still passes). What DOES distinguish the guard's presence is
        // -0.0 vs +0.0, covered by the dedicated case immediately below: double.Equals(double)
        // returns true for that pair (unlike ==), which is the one place this guard's specific
        // wording -- Equals, not == -- has an observable effect at all.
        Assert.False(UlpMath.IsUnrelated(0.033861348059854635, 0.033861348059854635));
    }

    [Fact]
    public void IsUnrelated_is_false_for_negative_zero_vs_positive_zero()
    {
        // MEDIUM 4: closes a real coverage gap -- no existing test called IsUnrelated with a
        // -0.0/+0.0 pair, even though the method's own scale==0.0 branch comment discusses exactly
        // this pair by name ("a.Equals(b) above already covers both being zero"). Measured:
        // (-0.0).Equals(0.0) is true (double.Equals's own equality, unlike ==, per
        // UlpMathTests' own header remarks on UlpMath.Distance), so this is actually caught by the
        // leading guard, before the scale==0.0 branch it is discussed alongside would ever run.
        Assert.False(UlpMath.IsUnrelated(-0.0, 0.0));
    }

    [Fact]
    public void IsUnrelated_is_false_for_a_nan_involved_pair()
    {
        // NaN-involved pairs are UlpMath.CategoricalDistance's job -- a different axis ("not
        // comparable" rather than "comparable and far apart") -- see IsUnrelated's own remarks.
        //
        // MEDIUM 4: this input also passes with the `double.IsNaN` guard deleted, for a reason
        // specific to floating point rather than to this method's own logic: Math.Max(NaN, x) is
        // NaN, so scale becomes NaN; Math.Abs(NaN - x) is also NaN; and any comparison against NaN
        // (`NaN > y`) is false by IEEE 754 definition, so the final `return` expression evaluates
        // to false regardless of whether the IsNaN guard ran first. Measured directly (guard
        // removed, this assertion still passes) -- NaN propagation through the arithmetic already
        // implements the same answer this guard states explicitly. See the two-NaN-payloads case
        // below for the one shape not yet covered by this fact alone.
        Assert.False(UlpMath.IsUnrelated(double.NaN, 1.0));
    }

    [Fact]
    public void IsUnrelated_is_false_for_two_different_nan_payloads()
    {
        // Closes a coverage gap distinct from the finite/NaN pair above: Distance already has a
        // dedicated two-different-payloads test (Distance_is_categorical_for_two_different_nan_payloads),
        // but before this fact, no test called IsUnrelated itself with two NaNs of differing
        // payload -- only NaN-vs-finite. double.Equals(NaN, NaN) is true regardless of payload
        // (see this class's own header remarks), so this pair is caught by the leading guard
        // before double.IsNaN ever runs, a third route to "false" alongside the two above.
        var nanA = BitConverter.Int64BitsToDouble(0x7FF8000000000001);
        var nanB = BitConverter.Int64BitsToDouble(0x7FF8000000000002);
        Assert.False(UlpMath.IsUnrelated(nanA, nanB));
    }

    [Fact]
    public void IsUnrelated_is_true_for_the_measured_61_percent_fixed_star_speed_pair()
    {
        // FIXSTAR|Galactic Center|2195878|SPEED -- UlpMath.cs's own remarks record this pair
        // (61% apart, same binade) as the reason UnrelatedRelativeThreshold replaced a fixed
        // absolute ULP cutoff.
        Assert.True(UlpMath.IsUnrelated(0.033861348059854635, 0.054671738039122175));
    }

    [Fact]
    public void IsUnrelated_is_false_for_the_measured_0_94_percent_houses_pair()
    {
        // HOUSES|G|66|-118.24|1533333.3333333335 -- recorded in UlpMath.cs's remarks as the
        // closest pair this comparer still calls the same quantity (a numeric max_ulp).
        Assert.False(UlpMath.IsUnrelated(73.37399496883465, 74.07304714772509));
    }

    [Fact]
    public void IsUnrelated_is_true_for_the_measured_1_37_percent_fixstarut_pair()
    {
        // FIXSTARUT|Spica|2382332|SPEED -- recorded in UlpMath.cs's remarks as the closest pair
        // this comparer now calls two different quantities.
        Assert.True(UlpMath.IsUnrelated(0.01222197902731036, 0.012392236699196889));
    }
}
