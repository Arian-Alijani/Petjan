using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Adapts a <see cref="DashboardListItem"/> (Contracts DTO, tone-agnostic of
/// the Desktop control library) to the Desktop <see cref="Tone"/> enum that
/// <see cref="Badge"/> binds to, so Views can bind <c>Tone</c> directly
/// without a converter.
/// </summary>
public sealed class DashboardRowViewModel
{
    public DashboardRowViewModel(DashboardListItem item)
    {
        AvatarText = item.AvatarText;
        // Typed translation: the key is separate from the safe fallback title. A technical key or an
        // internal id is never shown, even for an unknown future event type.
        Title = HistoryText.TryTitle(item.TitleKey, out var translated) ? translated
            : string.IsNullOrWhiteSpace(item.Title) || item.Title.StartsWith("history.", System.StringComparison.Ordinal)
                ? "رویداد" : item.Title;
        Subtitle = PersianNumberFormatter.ToPersianDigits(item.Subtitle ?? string.Empty);
        TagText = item.TagText ?? string.Empty;
        Tone = ToneMap.From(item.TagTone);
        IconKey = item.IconKey ?? ResolveIconKey(item.Kind);
    }

    /// <summary>Semantic dashboard glyph, selected by the stable row kind (never by localized text).</summary>
    public string IconKey { get; }

    public string AvatarText { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string TagText { get; }

    public Tone Tone { get; }

    /// <summary>Total mapping over stable kinds; an unknown kind gets the generic paw glyph.</summary>
    public static string ResolveIconKey(string? kind) => kind switch
    {
        "Appointment" => "IconCalendar",
        "Visit" => "IconClipboard",
        "Vaccine" => "IconSyringe",
        "Boarding" => "IconBriefcase",
        "Purchase" => "IconCart",
        "Inventory" => "IconBox",
        "Sale" or "Expense" or "Invoice" => "IconReceipt",
        "Cheque" => "IconCheque",
        _ => "IconPaw",
    };
}
