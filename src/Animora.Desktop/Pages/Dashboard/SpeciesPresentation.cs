using System;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Presentation-only mapping from the stable species key to its Persian name, glyph and palette colour.
/// Backend code supplies only the key; unknown keys fall back to a neutral "other" look instead of
/// inheriting another species' colour. Pure C# (no Avalonia types) so it is unit-testable.
/// </summary>
public static class SpeciesPresentation
{
    public readonly record struct Look(string Name, string IconKey, string ColorHex);

    private static readonly Look Other = new("سایر", "IconPaw", "#64748B");

    public static Look For(string? key, string? fallbackName = null) => key switch
    {
        "Dog" => new("سگ", "IconDog", "#6366F1"),
        "Cat" => new("گربه", "IconCat", "#06B6D4"),
        "Bird" => new("پرنده", "IconBird", "#F59E0B"),
        "Rabbit" => new("خرگوش", "IconRabbit", "#EC4899"),
        "Rodent" => new("جوندگان", "IconRabbit", "#8B5CF6"),
        "Reptile" => new("خزندگان", "IconPaw", "#16A34A"),
        "Other" => Other,
        _ => string.IsNullOrWhiteSpace(fallbackName) ? Other : Other with { Name = fallbackName },
    };
}
