namespace OracleVerify;

/// <summary>
/// Distance between two doubles measured in representable steps (ULPs), used to gate on
/// magnitude the way plain hex-equality alone cannot -- see the remarks on
/// <see cref="OracleVerifyReport"/> for why a row that stays listed can still fail this gate.
///
/// Built on the IEEE 754 "totalOrder" bit transform: reinterpret a double's raw bits as an
/// unsigned 64-bit integer, then, for negative values, complement every bit; for non-negative
/// values, set the sign bit. The result is a single unsigned key that sorts in the same order as
/// the doubles themselves (excluding NaN, which has no order and is handled separately below), so
/// the ULP distance between two doubles is just the unsigned difference between their keys -- no
/// separate-sign special case, no overflow, and it degrades correctly across the positive/negative
/// boundary and across +/-0.0.
/// </summary>
internal static class UlpMath
{
    /// <summary>
    /// Recorded when the two values cannot be compared as points on the same ordered line --
    /// one side is NaN and the other is not. Using the ceiling of the ulong range as a
    /// magnitude sentinel used to disable the drift check entirely for a row at this value,
    /// since nothing can ever numerically exceed it -- see <see cref="KnownDiffEntry.MaxUlp"/>
    /// and <see cref="OracleVerifyReport"/> for why a categorical difference is now tracked as
    /// its own state (present/absent) rather than as a number to compare against. This constant
    /// still identifies a single field's distance as categorical; it is no longer written
    /// verbatim into known-diff.tsv's max_ulp column.
    /// </summary>
    public const ulong CategoricalDistance = ulong.MaxValue;

    /// <summary>
    /// Above this many representable-double steps, a totalOrder distance is reported as
    /// "unrelated" rather than as a ULP count -- see <c>Program.DescribeField</c>. 2^52 is the
    /// number of distinct mantissa bit patterns in one binade: two finite doubles less than 2^52
    /// apart in totalOrder order differ by less than one full doubling of magnitude (still
    /// plausibly "the same quantity, off in the last few digits" territory), while two doubles
    /// further apart than that differ in order of magnitude, not in precision, and calling the
    /// distance between them a ULP count implies a level of numerical closeness that is not
    /// there.
    ///
    /// Measured against the current known-diff.tsv: every distance that reflects a genuine
    /// near-value rounding difference tops out at 195,911,459,571,320 (~2^47.5), and every
    /// distance that reflects two unrelated finite values (e.g. a cusp read as 0 instead of 270)
    /// starts at 4,457,293,557,087,583,675 (~2^62). The two clusters are about four orders of
    /// magnitude apart with nothing in between, and 2^52 sits in that gap with headroom on both
    /// sides, so the threshold does not need to be exact to separate them correctly.
    /// </summary>
    public const ulong UnrelatedThreshold = 1UL << 52;

    public static ulong Distance(double a, double b)
    {
        // double.Equals (unlike ==) treats NaN as equal to NaN and -0.0 as equal to 0.0, which is
        // exactly the pair of special cases this function needs handled before the bit transform
        // below, which has no representation for "these are the same point" when both sides are NaN.
        if (a.Equals(b))
        {
            return 0;
        }

        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return CategoricalDistance;
        }

        var keyA = OrderedKey(a);
        var keyB = OrderedKey(b);
        return keyA > keyB ? keyA - keyB : keyB - keyA;
    }

    private static ulong OrderedKey(double value)
    {
        const ulong SignBit = 0x8000000000000000UL;
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        return (bits & SignBit) != 0 ? ~bits : bits | SignBit;
    }
}
