using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

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
        // Per-icon accent (so the list is not a wall of one colour). Urgency wins for alert
        // rows (danger/warn tint is applied by the View's alert-ico style), so the semantic
        // per-icon colour here only drives non-urgent rows and plain activities.
        var hex = IconColorHex(IconKey);
        IconBrush = BrushCache.Solid(hex);
        IconBgBrush = BrushCache.Solid(hex, 0x22);
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

    /// <summary>Per-icon accent brush (stroke), so each row type reads with its own colour.</summary>
    public IBrush IconBrush { get; }

    /// <summary>Soft tinted tile background that matches <see cref="IconBrush"/>.</summary>
    public IBrush IconBgBrush { get; }

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

    /// <summary>
    /// Distinct accent per glyph so a dashboard list reads as a palette rather than a wall of one
    /// brand colour. Each hue is picked from the design-system family (indigo / teal / amber /
    /// violet / rose / green / sky / cyan) so the whole dashboard stays coherent.
    /// </summary>
    public static string IconColorHex(string? iconKey) => iconKey switch
    {
        "IconCalendar" => "#4a6cf7",   // appointments — brand indigo
        "IconClipboard" => "#4a6cf7",  // visits — blue
        "IconSyringe" => "#8b5cf6",    // vaccines — violet
        "IconBriefcase" => "#f59e0b",  // boarding — amber
        "IconCart" => "#0ea5e9",       // purchases — sky
        "IconBox" => "#d97706",        // inventory — deep amber
        "IconReceipt" => "#16a34a",    // sales / invoices — green
        "IconCheque" => "#ec4899",     // cheques — rose
        "IconBell" => "#f59e0b",       // alerts — amber
        "IconWallet" => "#3a56d4",     // money — blue
        "IconUsers" => "#2563eb",      // people — blue
        _ => "#64748b",                // other — slate
    };
}
