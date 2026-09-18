using System.Collections.ObjectModel;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// A tone swatch in a category row's tone picker, mirroring the reference's
/// <c>.tone-dot</c> buttons rendered by <c>renderSettings</c> (inventory.js).
/// The active swatch carries the reference's <c>.tone-dot.on</c> ink ring.
/// </summary>
public sealed partial class InventoryToneOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;

    public InventoryToneOptionViewModel(string tone)
    {
        Tone = tone;
        Brush = InventoryPresentation.ToneDot(tone);
    }

    /// <summary>teal | peach | lilac | amber | blue | green.</summary>
    public string Tone { get; }

    public IBrush Brush { get; }
}

/// <summary>
/// One category row of the «دسته‌بندی کالاها» settings list, mirroring the
/// reference's <c>.cat-setting-row</c> rendered by <c>renderSettings</c>: the
/// tone icon tile, the name input, the tone picker, the product count and the
/// delete action.
/// </summary>
public sealed partial class InventoryCategoryRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private IBrush _iconBackground;

    [ObservableProperty]
    private IBrush _iconForeground;

    public InventoryCategoryRowViewModel(string id, string label, string tone, string iconKey, int productCount)
    {
        Id = id;
        _label = label;
        Tone = tone;
        IconKey = iconKey;
        CountText = $"{PersianNumberFormatter.ToPersianDigits(productCount)} کالا";

        var (bg, fg) = InventoryPresentation.CategoryIconTile(tone);
        _iconBackground = bg;
        _iconForeground = fg;

        foreach (var optionTone in new[] { "teal", "peach", "lilac", "amber", "blue", "green" })
        {
            ToneOptions.Add(new InventoryToneOptionViewModel(optionTone) { IsOn = optionTone == tone });
        }
    }

    public string Id { get; }

    public string Tone { get; }

    public string IconKey { get; }

    public string CountText { get; }

    public ObservableCollection<InventoryToneOptionViewModel> ToneOptions { get; } = [];

    /// <summary>
    /// Applies a newly chosen tone to the row's icon tile + picker selection,
    /// mirroring the reference's tone-dot click handler.
    /// </summary>
    public void ApplyTone(string tone)
    {
        var (bg, fg) = InventoryPresentation.CategoryIconTile(tone);
        IconBackground = bg;
        IconForeground = fg;
        foreach (var option in ToneOptions)
        {
            option.IsOn = option.Tone == tone;
        }
    }
}

/// <summary>
/// A row of the «تنظیمات موجودی» switch list, mirroring the reference's
/// <c>.switch-row</c> rendered by <c>renderSettings</c>: a bold title, a muted
/// sub-line and a toggle switch.
/// </summary>
public sealed partial class InventorySettingToggleViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;

    public InventorySettingToggleViewModel(string title, string sub, bool isOn)
    {
        Title = title;
        Sub = sub;
        _isOn = isOn;
    }

    public string Title { get; }

    public string Sub { get; }
}
