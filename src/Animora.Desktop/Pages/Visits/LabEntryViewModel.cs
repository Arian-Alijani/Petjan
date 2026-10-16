using Animora.Contracts.Visits;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Visits;

/// <summary>One measured value of a recorded lab sheet («گلبول سفید — ۷٫۵ ×10³/µL»).</summary>
public sealed class LabItemViewModel
{
    public LabItemViewModel(LabItem item) => Item = item;

    public LabItem Item { get; }

    public string TypeName => Item.TypeName;

    public string ValueText => PersianNumberFormatter.ToPersianDigits(Item.Value);

    public string UnitText => Item.Unit ?? string.Empty;

    public bool HasUnit => !string.IsNullOrWhiteSpace(Item.Unit);
}

/// <summary>One laboratory result sheet row of the encounter («آزمایشات» list): date, description and per-test values.</summary>
public sealed class LabEntryViewModel
{
    public LabEntryViewModel(LabEntry entry)
    {
        Entry = entry;
        Items = entry.Items.Select(i => new LabItemViewModel(i)).ToArray();
    }

    public LabEntry Entry { get; }

    public string Id => Entry.Id;

    public DateTime TakenAt => Entry.TakenAt;

    public string DateText => ParaclinicPresentation.DisplayDate(Entry.TakenAt);

    public string Description => Entry.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);

    public IReadOnlyList<LabItemViewModel> Items { get; }

    public string CountText => $"{PersianNumberFormatter.ToPersianDigits(Items.Count)} آزمایش";
}
