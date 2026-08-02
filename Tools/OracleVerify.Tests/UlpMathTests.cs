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
        Assert.Equal(UlpMath.Distance(-1.0, 1.0), UlpMath.Distance(1.0, -1.0));
    }

    [Fact]
    public void IsUnrelated_is_false_for_an_exactly_equal_pair()
    {
        Assert.False(UlpMath.IsUnrelated(0.033861348059854635, 0.033861348059854635));
    }

    [Fact]
    public void IsUnrelated_is_false_for_a_nan_involved_pair()
    {
        // NaN-involved pairs are UlpMath.CategoricalDistance's job -- a different axis ("not
        // comparable" rather than "comparable and far apart") -- see IsUnrelated's own remarks.
        Assert.False(UlpMath.IsUnrelated(double.NaN, 1.0));
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
