using Animora.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>B7 (BUG-CORR-01): the correlation id is memoized per request, so repeated resolves agree.</summary>
public sealed class CorrelationHeaderTests
{
    [Fact]
    public void Resolve_WithoutInboundHeader_ReturnsSameMintedIdWithinRequest()
    {
        var context = new DefaultHttpContext();

        string first = CorrelationHeader.Resolve(context);
        string second = CorrelationHeader.Resolve(context);

        Assert.Equal(first, second);
        CorrelationHeader.Echo(context, CorrelationHeader.Resolve(context));
        Assert.Equal(first, context.Response.Headers[CorrelationHeader.Name].ToString());
    }

    [Fact]
    public void Resolve_ReusesValidInbound_AndRejectsInvalid()
    {
        var ok = new DefaultHttpContext();
        ok.Request.Headers[CorrelationHeader.Name] = "abc-123_X";
        Assert.Equal("abc-123_X", CorrelationHeader.Resolve(ok));

        var bad = new DefaultHttpContext();
        bad.Request.Headers[CorrelationHeader.Name] = "has space!";
        string minted = CorrelationHeader.Resolve(bad);
        Assert.NotEqual("has space!", minted);
        Assert.Equal(minted, CorrelationHeader.Resolve(bad));
    }

    [Fact]
    public void DistinctRequests_GetDistinctIds()
    {
        Assert.NotEqual(CorrelationHeader.Resolve(new DefaultHttpContext()), CorrelationHeader.Resolve(new DefaultHttpContext()));
    }
}
