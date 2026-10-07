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

        IsLow = entry.Stock <= entry.MinStock && entry.MinStock > 0 || entry.Stock == 0;
        // A custom item may have MinStock 0 → no division; the bar then simply shows full when anything is in stock.
        var pct = entry.MinStock > 0
            ? (int)Math.Min(100, Math.Round(entry.Stock / (entry.MinStock * 3d) * 100))
            : entry.Stock > 0 ? 100 : 0;
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

    /// <summary>Stock fill percentage (0..100), mirroring the reference's inline width% on the bar.</summary>
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
