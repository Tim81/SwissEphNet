using Xunit;

namespace SwissEphNet.Conformance.Tests.Dispatch;

public class HouseSystemCodecTests
{
    [Theory]
    [InlineData(32578, 'B')]
    [InlineData(32579, 'C')]
    [InlineData(32581, 'E')]
    [InlineData(32583, 'G')]
    [InlineData(32584, 'H')]
    [InlineData(32587, 'K')]
    [InlineData(32589, 'M')]
    [InlineData(32591, 'O')]
    [InlineData(32592, 'P')]
    [InlineData(32594, 'R')]
    [InlineData(32596, 'T')]
    [InlineData(32597, 'U')]
    [InlineData(32598, 'V')]
    [InlineData(32599, 'W')]
    [InlineData(32600, 'X')]
    [InlineData(32601, 'Y')]
    public void DecodesGarbageEncodedValuesObservedInTExp(int raw, char expected)
    {
        Assert.Equal(expected, HouseSystemCodec.DecodeHsys(raw));
    }

    [Fact]
    public void PassesThroughPlainAsciiUnchanged()
    {
        Assert.Equal('G', HouseSystemCodec.DecodeHsys(71));
    }
}
