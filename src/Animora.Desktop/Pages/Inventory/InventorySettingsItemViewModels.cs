using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// One category row of the read-only «دسته‌بندی کالاها» overview: the tone icon tile, the name and the live
/// product count. Tone and icon are derived from the immutable category id, so they are display-only.
/// </summary>
public sealed class InventoryCategoryRowViewModel
{
    public InventoryCategoryRowViewModel(string id, string label, string tone, string iconKey, int productCount)
    {
        Id = id;
        Label = label;
        Tone = tone;
        IconKey = iconKey;
        CountText = $"{PersianNumberFormatter.ToPersianDigits(productCount)} کالا";

        var (bg, fg) = InventoryPresentation.CategoryIconTile(tone);
        IconBackground = bg;
        IconForeground = fg;
    }

    public string Id { get; }

    public string Label { get; }

    public string Tone { get; }

    public string IconKey { get; }

    public string CountText { get; }

    public IBrush IconBackground { get; }

    public IBrush IconForeground { get; }
}
