using System;
using Animora.Contracts.Search;
using Animora.Desktop.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Shell.Search;

/// <summary>
/// One row in the command palette, wrapping a <see cref="SearchResult"/> with the
/// presentation bits the row template binds (icon geometry, tone brushes and the
/// transient highlight flag).
/// </summary>
public sealed partial class CommandPaletteItemViewModel : ObservableObject
{
    public CommandPaletteItemViewModel(SearchResult result)
    {
        Title = result.Title;
        Subtitle = result.Subtitle;
        IconKey = result.IconKey;
        Target = result.Target;
        DrillId = result.DrillId;

        (IconBackground, IconForeground) = ToneBrushes(result.Tone);
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string IconKey { get; }

    public Type Target { get; }

    public string? DrillId { get; }

    /// <summary>Soft tone background of the icon chip.</summary>
    public Avalonia.Media.IBrush IconBackground { get; }

    /// <summary>Saturated tone foreground of the icon chip.</summary>
    public Avalonia.Media.IBrush IconForeground { get; }

    /// <summary>True for the currently highlighted row.</summary>
    [ObservableProperty]
    private bool _isSelected;

    // Reproduces the reference's .ci-ic.tone-* palette (soft bg + saturated icon).
    private static (Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Foreground) ToneBrushes(string tone) =>
        tone switch
        {
            "blue" => (BrushCache.Solid("#e6eefb"), BrushCache.Solid("#1570cd")),
            "violet" => (BrushCache.Solid("#efe8fb"), BrushCache.Solid("#7b5cc4")),
            "green" => (BrushCache.Solid("#e2f1e8"), BrushCache.Solid("#027a48")),
            "amber" => (BrushCache.Solid("#fbf0da"), BrushCache.Solid("#b54708")),
            _ => (BrushCache.Solid("#dbe4fe"), BrushCache.Solid("#4a6cf7")),
        };
}
