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

    [Fact]
    public void CheckDD_UsesPerComponentPrecisionForAnyXxPrefixedArray_NotJustLiteralXx()
    {
        // Regression test for the false-pass bug: checkpoints.c:14 tests
        // strncmp(name,"xx",2)==0 (a prefix test), not name=="xx". xxperi is
        // exactly the kind of array this must cover.
        var expected = FieldsWith(("xxperi[0]", "1.00000001000"));
        var precision = new Precision(1e-3, [1e-9, 1e-9, 1e-9, 1e-9, 1e-9, 1e-9]);
        var ctx = new CheckContext(expected, precision);

        // Differs from expected by 1e-8: within the loose "all" precision
        // (1e-3) but outside the tight per-component xx precision (1e-9).
        // Must be reported as a mismatch -- proving xxperi got the tight
        // precision, not the loose one.
        ctx.CheckDD("xxperi", [1.00000002000]);

        Assert.Single(ctx.Mismatches);
    }

    [Fact]
    public void CheckDD_NonXxPrefixedArray_UsesOverallPrecision()
    {
        var expected = FieldsWith(("cusps[0]", "1.00000001000"));
        var precision = new Precision(1e-3, [1e-9, 1e-9, 1e-9, 1e-9, 1e-9, 1e-9]);
        var ctx = new CheckContext(expected, precision);

        // Same tiny difference, but "cusps" doesn't start with "xx" -- must
        // pass under the loose overall precision.
        ctx.CheckDD("cusps", [1.00000002000]);

        Assert.Empty(ctx.Mismatches);
    }

    [Fact]
    public void CheckS_NullActual_ComparesAsEmptyString()
    {
        var expected = FieldsWith(("serr", ""));
        var ctx = new CheckContext(expected, Precision.Default);

        ctx.CheckS("serr", null);

        Assert.Empty(ctx.Mismatches);
    }
}
