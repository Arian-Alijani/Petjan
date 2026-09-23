using Animora.Domain.Results;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The Result contract application boundaries rely on: expected failures carry an
/// <see cref="ErrorCode"/>, and Map/Bind short-circuit without losing it.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Ok_And_Fail_CarryTheExpectedState()
    {
        var ok = Result.Ok();
        var fail = Result.Fail(ErrorCodes.NotFound, "pet 01H");

        Assert.True(ok.IsSuccess);
        Assert.True(ok.Error.IsNone);
        Assert.True(fail.IsFailure);
        Assert.Equal("NOT_FOUND", fail.Error.Code);
        Assert.Equal(ErrorCode.NotFound, fail.Error.Category);
        Assert.Equal("pet 01H", fail.Detail);
    }

    [Fact]
    public void ImplicitConversion_LiftsValueIntoSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(42, Result<int>.FromValue(42).Value);
        Assert.Equal(7, Result<int>.Fail(ErrorCodes.Unexpected).OrElse(7));
    }

    [Fact]
    public void Map_TransformsSuccess_AndPropagatesFailure()
    {
        Assert.Equal(10, Result<int>.Ok(5).Map(v => v * 2).Value);

        var failed = Result<int>.Fail(ErrorCodes.ValidationFailed, "why").Map(v => v * 2);

        Assert.True(failed.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, failed.Error);
        Assert.Equal("why", failed.Detail);
    }

    [Fact]
    public void Bind_ChainsSuccess_AndShortCircuitsFailure()
    {
        var chained = Result<int>.Ok(3).Bind(v => Result<string>.Ok($"#{v}"));
        Assert.Equal("#3", chained.Value);

        var shortCircuited = Result<int>
            .Fail(ErrorCodes.PermissionDenied)
            .Bind(v => Result<string>.Ok($"#{v}"));
        Assert.Equal(ErrorCodes.PermissionDenied, shortCircuited.Error);

        var dropped = Result<int>.Fail(ErrorCodes.StoreUnavailable).ToResult();
        Assert.True(dropped.IsFailure);

        var fromVoid = Result.Fail(ErrorCodes.TenantMismatch).Bind(() => Result.Ok());
        Assert.Equal(ErrorCodes.TenantMismatch, fromVoid.Error);
        Assert.True(Result.Ok().Bind(() => Result.Ok()).IsSuccess);
    }
}
