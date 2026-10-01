using Animora.Desktop.Controls;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>D29: columns follow the real content width (after the sidebar), never overflow, and stack when narrow.</summary>
public sealed class DashboardResponsiveLayoutTests
{
    // Content widths for 1440 / 1100 / 960 windows with the sidebar open (≈280) and closed (≈80), minus page margins (52).
    [Theory]
    [InlineData(1440 - 280 - 52, 4)] // wide, sidebar open
    [InlineData(1100 - 280 - 52, 3)] // medium, sidebar open
    [InlineData(960 - 280 - 52, 2)]  // narrow (minimum window), sidebar open
    [InlineData(960 - 80 - 52, 3)]   // narrow, sidebar collapsed
    [InlineData(300, 1)]             // far below any minimum: single column, no overflow
    public void KpiColumns_FollowContentWidth(double width, int expected) =>
        Assert.Equal(expected, AdaptiveGridMath.Columns(width, 0, 210, 4, 4));

    [Theory]
    [InlineData(1000, 3)]
    [InlineData(700, 2)]
    [InlineData(400, 1)]
    public void ManagerRowTwo_StacksBeforeCardsGetCramped(double width, int expected) =>
        Assert.Equal(expected, AdaptiveGridMath.Columns(width, 16, 300, 3, 3));

    [Fact]
    public void Columns_AreClampedToItemCountAndMax_AndInfiniteWidthIsSafe()
    {
        Assert.Equal(2, AdaptiveGridMath.Columns(5000, 16, 100, 4, 2));
        Assert.Equal(4, AdaptiveGridMath.Columns(5000, 16, 100, 4, 8));
        Assert.Equal(3, AdaptiveGridMath.Columns(double.PositiveInfinity, 0, 100, 3, 9));
        Assert.Equal(1, AdaptiveGridMath.Columns(0, 16, 100, 4, 4));
        Assert.Equal(1, AdaptiveGridMath.Columns(500, 0, 100, 4, 0));
    }

    [Fact]
    public void CellWidths_NeverExceedAvailable_AndWeightsApplyOnlyToASingleRow()
    {
        var w = AdaptiveGridMath.CellWidths(1000, 16, 2, 2, AdaptiveGridMath.ParseWeights("1.3,1"));
        Assert.Equal(984, w.Sum(), 6);
        Assert.True(w[0] > w[1]);
        var stacked = AdaptiveGridMath.CellWidths(500, 16, 1, 2, AdaptiveGridMath.ParseWeights("1.3,1"));
        Assert.Equal([500d], stacked);
        var wrapped = AdaptiveGridMath.CellWidths(700, 0, 2, 3, AdaptiveGridMath.ParseWeights("2,1,1"));
        Assert.Equal(wrapped[0], wrapped[1]);
    }

    [Theory]
    [InlineData("1.3,1", true)]
    [InlineData("1,x", false)]
    [InlineData("0,1", false)]
    [InlineData("", false)]
    public void ParseWeights_RejectsInvalidInput(string text, bool ok) =>
        Assert.Equal(ok, AdaptiveGridMath.ParseWeights(text) is not null);

    [Fact]
    public void DashboardViews_UseAdaptiveGrid_AndNoFixedColumnRows()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Animora.Desktop/");
        foreach (var f in new[] { "Manager", "Doctor", "Cashier" })
        {
            var x = File.ReadAllText(Path.Combine(root, $"Pages/Dashboard/{f}DashboardView.axaml"));
            Assert.Contains("AdaptiveGrid", x);
            Assert.DoesNotContain("ColumnDefinitions=\"*,*\" ColumnSpacing=\"16\"", x);
            Assert.DoesNotContain("ColumnDefinitions=\"*,*,*\" ColumnSpacing=\"16\"", x);
        }
        var page = File.ReadAllText(Path.Combine(root, "Pages/Dashboard/DashboardPageView.axaml"));
        Assert.DoesNotContain("UniformGrid Columns=\"4\"", page);
        Assert.Contains("MinWidth=\"960\"", File.ReadAllText(Path.Combine(root, "Shell/MainWindow.axaml")));
    }
}
