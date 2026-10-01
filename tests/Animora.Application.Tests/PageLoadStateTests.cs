using Animora.Desktop.Shell;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Shared page load states: loading / empty / stale / error are distinct, a zero never precedes first success.</summary>
public sealed class PageLoadStateTests
{
    [Theory]
    [InlineData(false, true, false, false, PageLoadState.InitialLoading)]
    [InlineData(false, false, false, false, PageLoadState.InitialLoading)]
    [InlineData(false, true, true, false, PageLoadState.InitialLoading)]
    [InlineData(false, false, true, false, PageLoadState.Error)]
    [InlineData(true, false, false, true, PageLoadState.Empty)]
    [InlineData(true, false, false, false, PageLoadState.Ready)]
    [InlineData(true, false, true, false, PageLoadState.Stale)]
    [InlineData(true, true, false, false, PageLoadState.Refreshing)]
    [InlineData(true, true, true, false, PageLoadState.Refreshing)]
    public void Resolve_MapsInputsToState(bool hasData, bool isBusy, bool hasError, bool isEmpty, PageLoadState expected) =>
        Assert.Equal(expected, PageLoadStateResolver.Resolve(hasData, isBusy, hasError, isEmpty));

    [Theory]
    [InlineData(PageLoadState.InitialLoading)]
    [InlineData(PageLoadState.Refreshing)]
    [InlineData(PageLoadState.Empty)]
    [InlineData(PageLoadState.Stale)]
    [InlineData(PageLoadState.Error)]
    public void Describe_NonReadyStatesHaveText(PageLoadState state) =>
        Assert.False(string.IsNullOrWhiteSpace(PageLoadStateResolver.Describe(state)));

    [Fact]
    public void Describe_ReadyIsEmpty() =>
        Assert.Equal(string.Empty, PageLoadStateResolver.Describe(PageLoadState.Ready));
}
