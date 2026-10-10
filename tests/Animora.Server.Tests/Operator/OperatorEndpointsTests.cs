using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Animora.Server.Tests.Operator;

public sealed class OperatorEndpointsTests
{
    [Fact]
    public void IfMatchMismatch_412_ConcurrencyConflict()
    {
        var http = new DefaultHttpContext(); http.Request.Headers.IfMatch = "\"stale\"";
        Assert.False(OperatorEndpoints.IfMatch(http, "current"));
        var response = Assert.IsType<JsonHttpResult<WireApiError>>(OperatorEndpoints.Failure(ErrorCodes.ConcurrencyConflict, http));
        Assert.Equal(412, response.StatusCode);
        http.Request.Headers.IfMatch = "\"current\""; Assert.True(OperatorEndpoints.IfMatch(http, "current"));
    }

    [Theory]
    [InlineData("500", 200)]
    [InlineData("-1", 1)]
    [InlineData("invalid", 50)]
    public void Paging_IsBounded(string input, int expected)
    {
        var http = new DefaultHttpContext(); http.Request.QueryString = new QueryString("?pageSize=" + input);
        Assert.Equal(expected, OperatorEndpoints.Number(http, "pageSize", 50, 200));
    }
}
