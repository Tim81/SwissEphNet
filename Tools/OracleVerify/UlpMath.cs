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
    /// Above this fraction of the larger operand's magnitude, two doubles are reported as
    /// "unrelated" rather than as a ULP count -- see <c>Program.DescribeField</c>. Relative, not
    /// a fixed ULP count, because a totalOrder ULP distance is itself magnitude-dependent: one
    /// binade (a value doubling) is exactly 2^52 ULPs everywhere, so whether a given ULP count
    /// means "a small fraction of a doubling" or "most of one" depends entirely on how wide the
    /// binade the two values sit in happens to be. A fixed absolute cutoff (this used to be
    /// <c>1UL &lt;&lt; 52</c>) reads correctly only for comparisons that never approach a full
    /// doubling on their own, which held for the analytic grid's planet positions and house
    /// cusps (0-360 degrees, AU-scale distances) but not for the files grid's fixed-star
    /// proper-motion speeds, which run 0.001-0.08 degrees/day: two such speeds can disagree by
    /// most of their own value and still land inside one binade, so a pair 61% apart
    /// (FIXSTAR|Galactic Center|2195878|SPEED, c=0.033861348059854635, net=0.054671738039122175)
    /// produced a totalOrder distance of 2,999,093,265,794,048 (~2^51.4) -- under the old 2^52
    /// cutoff, so it printed as a bare ULP count, claiming a closeness the two values do not
    /// have.
    ///
    /// Measured against the current known-diff.tsv and known-diff-files.tsv: every pair this
    /// comparer calls the same quantity (a numeric max_ulp is recorded) differs by at most 0.94%
    /// of its own magnitude (HOUSES|G|66|-118.24|1533333.3333333335,
    /// c=73.37399496883465, net=74.07304714772509); every pair it now calls two different
    /// quantities differs by at least 1.37% (FIXSTARUT|Spica|2382332|SPEED,
    /// c=0.01222197902731036, net=0.012392236699196889). 1% sits in that gap. Unlike the old
    /// ULP-count gap, this one is not an artefact of the grids' own value ranges: it is a
    /// statement about relative closeness, so it reads the same way whether the pair being
    /// compared is a fixed-star speed near 0.03 or a planet longitude near 300.
    /// </summary>
    public const double UnrelatedRelativeThreshold = 0.01;

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

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> differ by more than
    /// <see cref="UnrelatedRelativeThreshold"/> of the larger operand's magnitude -- see that
    /// constant's remarks for why "unrelated" checks this rather than a ULP count. Always false
    /// for an exactly-equal pair and for a pair involving NaN: a NaN-involved pair is
    /// <see cref="CategoricalDistance"/>'s job, a different axis ("not comparable" rather than
    /// "comparable and far apart").
    /// </summary>
    public static bool IsUnrelated(double a, double b)
    {
        if (a.Equals(b))
        {
            return false;
        }

        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return false;
        }

        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        if (scale == 0.0)
        {
            // a.Equals(b) above already covers both being zero; scale can only be 0.0 here if
            // that check somehow let a -0.0/+0.0 pair through, which it does not -- kept as a
            // guard against a division by zero rather than as a reachable branch.
            return false;
        }

        return Math.Abs(a - b) > UnrelatedRelativeThreshold * scale;
    }

    private static ulong OrderedKey(double value)
    {
        const ulong SignBit = 0x8000000000000000UL;
        var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        return (bits & SignBit) != 0 ? ~bits : bits | SignBit;
    }
}
