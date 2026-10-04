using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// One category row of the «دسته‌بندی کالاها» settings list: a tone dot, the name and the live product count.
/// Tone is derived from the immutable category id, so it is display-only. Categories carry no icon.
/// </summary>
public sealed partial class InventoryCategoryRowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;

    public InventoryCategoryRowViewModel(string id, string label, string tone, int productCount)
    {
        Id = id;
        Label = label;
        Tone = tone;
        ProductCount = productCount;
        CountText = $"{PersianNumberFormatter.ToPersianDigits(productCount)} کالا";
        ToneBrush = InventoryPresentation.ChipDot(tone);
    }

    public string Id { get; }

    public string Label { get; }

    public string Tone { get; }

    public int ProductCount { get; }

    public string CountText { get; }

    public IBrush ToneBrush { get; }

    /// <summary>Only an empty category may be removed (the backend enforces the same rule).</summary>
    public bool CanDelete => ProductCount == 0;
}
