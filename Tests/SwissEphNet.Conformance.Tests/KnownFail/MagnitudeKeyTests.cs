using SwissEphNet.Conformance.Tests.Dispatch;
using Xunit;

namespace SwissEphNet.Conformance.Tests.KnownFail;

public class MagnitudeKeyTests
{
    [Fact]
    public void Compute_NoMismatches_ReturnsNotApplicable()
    {
        Assert.Equal(MagnitudeKey.NotApplicable, MagnitudeKey.Compute([]));
    }

    [Fact]
    public void Compute_NonNumericMismatchOnly_ReturnsNotApplicable()
    {
        // CheckI/CheckEqualsI/CheckS never set Diff -- an integer or string mismatch carries no
        // magnitude of its own.
        var mismatches = new[] { new FieldMismatch("iflag", "4", "6", null) };

        Assert.Equal(MagnitudeKey.NotApplicable, MagnitudeKey.Compute(mismatches));
    }

    [Fact]
    public void Compute_ExpectedZero_IsExcluded()
    {
        // Relative error against an expected value of exactly 0 is undefined, and t.exp carries
        // genuinely run-dependent zeros -- excluding it must not crash or produce -Infinity/NaN.
        var mismatches = new[] { new FieldMismatch("xx[2]", "0", "1e-10", -1e-10) };

        Assert.Equal(MagnitudeKey.NotApplicable, MagnitudeKey.Compute(mismatches));
    }

    [Fact]
    public void Compute_SingleField_FloorsLog10OfRelativeError()
    {
        // expected 100, actual 100 - 1e-7: diff = 1e-7, relative error = 1e-7 / 100 = 1e-9.
        // floor(log10(1e-9)) = -9.
        var mismatches = new[] { new FieldMismatch("xx[0]", "100", "99.9999999", 1e-7) };

        Assert.Equal("-9", MagnitudeKey.Compute(mismatches));
    }

    [Fact]
    public void Compute_TakesTheWorstFieldAcrossTheRow()
    {
        // Two fields on the same row: one off by a relative 1e-9 (decade -9), the other by a
        // relative 0.5 (decade -1, since floor(log10(0.5)) = -1). The row's magnitude_key is the
        // worst (least negative) of the two, matching "max over its recorded fields".
        var mismatches = new[]
        {
            new FieldMismatch("xx[0]", "100", "99.9999999", 1e-7), // relative 1e-9 -> decade -9
            new FieldMismatch("xx[1]", "2", "1", 1.0), // relative 0.5 -> decade -1
        };

        Assert.Equal("-1", MagnitudeKey.Compute(mismatches));
    }

    [Fact]
    public void Compute_ExcludesExpectedZeroButStillUsesOtherFields()
    {
        var mismatches = new[]
        {
            new FieldMismatch("xx[2]", "0", "1e-10", -1e-10), // excluded: expected is 0
            new FieldMismatch("xx[0]", "100", "99.9999999", 1e-7), // relative 1e-9 -> decade -9
        };

        Assert.Equal("-9", MagnitudeKey.Compute(mismatches));
    }

    [Fact]
    public void Compute_OrderOfMagnitudeWidening_ChangesTheBucket()
    {
        // The worked scenario item 4 exists for: a 1e-9-relative mismatch (decade -9) widening to
        // a 1e-3-relative one (decade -3) must land in a different bucket, not the same one --
        // that is what lets ConformanceReport.Build's drift check catch it.
        var before = new[] { new FieldMismatch("deltat", "100", "99.9999999", 1e-7) }; // relative 1e-9
        var after = new[] { new FieldMismatch("deltat", "100", "99.9", 0.1) }; // relative 1e-3

        var beforeKey = MagnitudeKey.Compute(before);
        var afterKey = MagnitudeKey.Compute(after);

        Assert.Equal("-9", beforeKey);
        Assert.Equal("-3", afterKey);
        Assert.NotEqual(beforeKey, afterKey);
    }

    [Fact]
    public void Compute_UlpLevelNoiseWithinTheSameDecade_DoesNotChangeTheBucket()
    {
        // Two relative errors that are both "about 1e-9" but not bit-identical must floor to the
        // same decade -- this is the ULP-jitter tolerance the magnitude gate is built around.
        var a = new[] { new FieldMismatch("xx[0]", "100", "99.9999999", 1.0e-7) }; // relative 1.0e-9
        var b = new[] { new FieldMismatch("xx[0]", "100", "99.99999989", 1.1e-7) }; // relative 1.1e-9

        Assert.Equal(MagnitudeKey.Compute(a), MagnitudeKey.Compute(b));
    }
}
