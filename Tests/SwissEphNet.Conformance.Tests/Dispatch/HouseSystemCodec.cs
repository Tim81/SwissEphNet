namespace SwissEphNet.Conformance.Tests.Dispatch;

/// <summary>
/// Decodes the "ihsy" field as it appears in t.exp into the house-system
/// character SwissEphNet's swe_houses* family actually expects.
/// </summary>
/// <remarks>
/// <para>
/// t.exp stores most ihsy values not as a plain ASCII code but as
/// charcode + 127*256 (e.g. 'P' = 80 shows up as 32592 = 0x7F50). This is not
/// a meaningful encoding -- it is uninitialized-stack-memory garbage from the
/// reference tool's own fixture parser
/// (external/swisseph/setest/multivalues.c: parse_int_range, the
/// <c>sscanf(p0 + 1, "%c", (char *) &i0)</c> branch writes one byte into an
/// otherwise-uninitialized <c>int i0</c>). It happens to be a *repeatable*
/// artifact of one specific compiled build, not a real value: the upper 3
/// bytes are always 0x00 0x7F 0x00, confirmed empirically across all 16
/// distinct house-system characters used in the fixture (P,K,O,R,C,E,V,W,X,
/// H,T,B,M,U,G,Y all reproduce exactly as charcode + 32512).
/// </para>
/// <para>
/// SwissEphNet's swe_houses/_ex/_armc take <c>char hsys</c> in C#, where
/// C's 8-bit truncating (char) cast becomes a 16-bit UTF-16 code unit cast --
/// passing 32592 straight through would select a nonsense Unicode code point,
/// not 'P'. Recovering the low byte (value &amp; 0xFF, when the raw value
/// isn't already a plain printable ASCII code) reconstructs the character the
/// original C run actually meant, and is what must be passed to reproduce the
/// recorded reference cusps.
/// </para>
/// </remarks>
public static class HouseSystemCodec
{
    public static char DecodeHsys(int rawValue)
    {
        // Already a plain, printable ASCII char code (as at least one
        // iteration in the corpus stores it) -- use as-is.
        if (rawValue is >= 32 and < 128)
        {
            return (char)rawValue;
        }

        return (char)(rawValue & 0xFF);
    }
}
