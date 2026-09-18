using Animora.Contracts.Vaccination;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Adapts a <see cref="VaccineCatalogEntry"/> to one card of the «انبار واکسن»
/// catalog grid, mirroring the reference's <c>renderCatalog</c>: the tinted
/// species-icon tile, the name/short/species line, the protects blurb and the
/// stock bar (low-stock turns danger-colored).
/// </summary>
public sealed partial class VaccineCatalogCardViewModel : ObservableObject
{
    public VaccineCatalogCardViewModel(VaccineCatalogEntry entry)
    {
        Entry = entry;
        Name = entry.Name;
        ShortLabel = $"{entry.Short} · {VaccinationPresentation.SpeciesLabel(entry.Species)}";

        var color = VaccinationPresentation.CatalogToneColor(entry.Tone);
        IconBackground = VaccinationPresentation.FillAlpha(color, 0x22);
        IconForeground = VaccinationPresentation.Fill(color);
        Protects = entry.Protects;
        SpeciesIconKey = VaccinationPresentation.SpeciesIconKey(entry.Species);

        IsLow = entry.Stock <= entry.MinStock;
        var pct = (int)Math.Min(100, Math.Round(entry.Stock / (entry.MinStock * 3d) * 100));
        StockPercent = pct;
        StockFill = IsLow ? VaccinationPresentation.Fill("#d92d20") : VaccinationPresentation.Fill(color);
        StockText = $"{VaccinationPresentation.Fa(entry.Stock)} دُز";
    }

    public VaccineCatalogEntry Entry { get; }
    public string Name { get; }
    public string ShortLabel { get; }
    public string SpeciesIconKey { get; }
    public IBrush IconBackground { get; }
    public IBrush IconForeground { get; }
    public string Protects { get; }
    public bool IsLow { get; }
    public int StockPercent { get; }
    public IBrush StockFill { get; }
    public string StockText { get; }
}

/// <summary>
/// Adapts an <see cref="SmsTemplate"/> to one row of the «قالب‌های پیامک»
/// panel, mirroring the reference's <c>renderTemplates</c> (name + chevron,
/// and the two-line clamped body preview).
/// </summary>
public sealed partial class SmsTemplateRowViewModel : ObservableObject
{
    public SmsTemplateRowViewModel(SmsTemplate template)
    {
        Template = template;
        Name = template.Name;
        Body = template.Body;
    }

    public SmsTemplate Template { get; }
    public string Name { get; }
    public string Body { get; }
}

/// <summary>
/// One status-filter chip in the toolbar's segmented rail, mirroring the
/// reference's <c>renderSeg</c>: a Persian label plus its live record count,
/// with the active state driven by the page's current segment.
/// </summary>
public sealed partial class VaccinationSegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public VaccinationSegmentViewModel(string key, string label, int count)
    {
        Key = key;
        Label = label;
        Count = PersianNumberFormatter.ToPersianDigits(count);
    }

    /// <summary>all | due | overdue | scheduled | done — the reference's <c>S.seg</c>.</summary>
    public string Key { get; }
    public string Label { get; }
    public string Count { get; }
}

/// <summary>
/// One pickable SMS template chip inside the compose drawer, mirroring the
/// reference's <c>vx-tmpl-chips</c> (the selected chip takes the violet wash).
/// </summary>
public sealed partial class SmsTemplateChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public SmsTemplateChipViewModel(SmsTemplate template)
    {
        Template = template;
        Name = template.Name;
    }

    public SmsTemplate Template { get; }
    public string Name { get; }
}

/// <summary>
/// A species-filter option for the toolbar's «فیلتر بر اساس گونه» select,
/// mirroring the reference's <c>#vxSpecies</c> options.
/// </summary>
public sealed partial class SpeciesFilterOption : ObservableObject
{
    public SpeciesFilterOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>"" (all) | cat | dog | bird | rabbit | rodent.</summary>
    public string Value { get; }
    public string Label { get; }
}

/// <summary>
/// One page button of the records pager, mirroring the reference's
/// <c>#vxPager .pages button</c> (the active page takes the brand fill).
/// </summary>
public sealed partial class PagerButtonViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public PagerButtonViewModel(int number, string label)
    {
        Number = number;
        Label = label;
    }

    public int Number { get; }
    public string Label { get; }
}
