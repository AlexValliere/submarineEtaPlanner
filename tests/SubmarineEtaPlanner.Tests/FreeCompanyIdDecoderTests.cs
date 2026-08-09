using MessagePack;
using SubmarineEtaPlanner.TrackerData;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FreeCompanyIdDecoderTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(4_611_686_018_427_387_904UL)]
    [InlineData(ulong.MaxValue)]
    public void DecodesMessagePackUlong(ulong value)
    {
        var blob = MessagePackSerializer.Serialize(value);

        var decoded = FreeCompanyIdDecoder.TryDecode(blob);

        Assert.Equal(value, decoded);
    }

    [Fact]
    public void InvalidBytesReturnNull()
    {
        Assert.Null(FreeCompanyIdDecoder.TryDecode(new byte[] { 0xc1 }));
    }
}
