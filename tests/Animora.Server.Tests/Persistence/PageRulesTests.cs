using Animora.Persistence.PostgreSql.Repositories;
using Xunit;

namespace Animora.Server.Tests.Persistence;

public sealed class PageRulesTests
{
    [Theory]
    [InlineData(500, 200)]
    [InlineData(201, 200)]
    [InlineData(200, 200)]
    [InlineData(50, 50)]
    [InlineData(1, 1)]
    public void Clamps_To200(int requested, int expected) =>
        Assert.Equal(expected, PageRules.ClampPageSize(requested));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Page_Below1_Becomes1(int requested, int expected) =>
        Assert.Equal(expected, PageRules.NormalizePage(requested));

    [Fact]
    public void PageSize_Below1_BecomesMinimum()
    {
        Assert.Equal(PageRules.MinPageSize, PageRules.ClampPageSize(0));
        Assert.Equal(PageRules.MinPageSize, PageRules.ClampPageSize(-10));
    }

    [Fact]
    public void MaxPageSize_Is200() => Assert.Equal(200, PageRules.MaxPageSize);
}
