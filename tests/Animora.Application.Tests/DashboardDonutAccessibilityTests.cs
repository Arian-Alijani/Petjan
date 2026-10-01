using Animora.Desktop.Pages.Dashboard;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>D31: the donut drill-in is a real keyboard/automation control and the counts are available as text.</summary>
public sealed class DashboardDonutAccessibilityTests
{
    private static string Xaml(string rel) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "../../../../../src/Animora.Desktop/Pages/Dashboard/", rel));

    [Fact]
    public void Entry_AndSummary_ExposeNameCountAndPercentAsText()
    {
        Assert.Equal("گربه: ۱۲ بیمار، ۴۰٪", SpeciesAccessibility.Entry("گربه", "۱۲", "۴۰٪"));
        var text = SpeciesAccessibility.Summary("۳۰", ["گربه: ۱۲ بیمار، ۴۰٪", "سگ: ۱۸ بیمار، ۶۰٪"]);
        Assert.Contains("مجموع ۳۰", text);
        Assert.Contains("سگ: ۱۸", text);
        Assert.Contains("Enter", text);
    }

    [Fact]
    public void Summary_IsBounded_AndEmptyIsExplicit()
    {
        var many = Enumerable.Range(1, 10).Select(i => $"g{i}").ToArray();
        var text = SpeciesAccessibility.Summary("۱", many);
        Assert.Contains("g6", text);
        Assert.DoesNotContain("g7", text);
        Assert.Contains("و 4 گونه دیگر", text);
        Assert.Contains("داده‌ای وجود ندارد", SpeciesAccessibility.Summary("۰", []));
    }

    [Fact]
    public void DonutDrillIn_IsAButtonWithCommandAndAutomationName_NotAPointerPressedBorder()
    {
        var x = Xaml("ManagerDashboardView.axaml");
        Assert.Contains("Command=\"{Binding OpenSpeciesPatientsCommand}\"", x);
        Assert.Contains("AutomationProperties.Name=\"{Binding SpeciesAutomationName}\"", x);
        Assert.DoesNotContain("PointerPressed=", x);
        Assert.Contains("GotFocus=\"OnDonutGotFocus\"", x);
        Assert.Contains("LostFocus=\"OnDonutLostFocus\"", x);
        Assert.Contains("AutomationProperties.Name=\"{Binding AutomationName}\"", x); // legend rows
        Assert.Contains("OnLegendGotFocus", x);
        Assert.Contains("OnLegendLostFocus", x);
    }

    [Fact]
    public void CodeBehind_HasNoPointerPressedDrillIn_AndEscapeRestoresReadout()
    {
        var cs = Xaml("ManagerDashboardView.axaml.cs");
        Assert.DoesNotContain("OnDonutPointerPressed", cs);
        Assert.Contains("Key.Escape", cs);
        Assert.Contains("BlurSpeciesCommand", cs);
    }
}
