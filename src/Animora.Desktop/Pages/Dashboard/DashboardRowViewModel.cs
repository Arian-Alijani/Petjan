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
        IsAlert = item.Kind.StartsWith("Alert", System.StringComparison.Ordinal);
        IsDanger = IsAlert && item.TagTone == DashboardTone.Danger;
        IsWarn = IsAlert && item.TagTone == DashboardTone.Warn;
        Kind = item.Kind;
        SectionLabel = item.Kind switch
        {
            "AlertCheque" => "چک",
            "AlertRecurring" => "هزینه",
            "AlertBoardingVaccine" => "پانسیون",
            _ => string.Empty,
        };
    }

    /// <summary>The stable row kind (<c>AlertCheque</c> | <c>AlertRecurring</c> | <c>AlertBoardingVaccine</c> | …); drives alert navigation.</summary>
    public string Kind { get; }

    /// <summary>Short owning-section tag of an alert («چک» / «هزینه»); empty for plain activity rows.</summary>
    public string SectionLabel { get; }

    /// <summary>A management alert (fixed expense / cheque) rather than a plain activity row: gets the tinted treatment.</summary>
    public bool IsAlert { get; }

    /// <summary>Alert at its most urgent (overdue / bounced).</summary>
    public bool IsDanger { get; }

    /// <summary>Alert that is due soon or today.</summary>
    public bool IsWarn { get; }

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
