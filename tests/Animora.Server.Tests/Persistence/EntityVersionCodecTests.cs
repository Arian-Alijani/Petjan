using Animora.Persistence.PostgreSql.Concurrency;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class EntityVersionCodecTests
{
    [Fact]
    public void RoundTrips_CanonicalLowercase()
    {
        var guid = Guid.NewGuid();
        var wire = EntityVersionCodec.ToWire(guid);

        Assert.Equal(wire.ToLowerInvariant(), wire);
        Assert.Equal(36, wire.Length);
        Assert.True(EntityVersionCodec.TryParse(wire, out var parsed));
        Assert.Equal(guid, parsed);
        Assert.False(EntityVersionCodec.TryParse("not-a-version", out _));
        Assert.False(EntityVersionCodec.TryParse(null, out _));
    }

    [Fact]
    public void StringGuidConverter_MapsBothDirections()
    {
        var converter = new StringGuidConverter();
        var guid = Guid.NewGuid();

        Assert.Equal(guid, converter.ConvertToProviderTyped(guid.ToString("D").ToUpperInvariant()));
        Assert.Equal(Guid.Empty, converter.ConvertToProviderTyped(null));
        Assert.Equal(guid.ToString("D"), converter.ConvertFromProviderTyped(guid));
    }
}
