using System.Collections.Generic;
using System.Globalization;
using Animora.Contracts.Boarding;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// One virtualized row of the board grid: holds up to three
/// <see cref="BoardingStayCardViewModel"/> cards. The stay grid stays a
/// single-column virtualized ListBox (VirtualizingStackPanel realizes only the
/// visible rows), but each realized item now lays its cards out three-per-row
/// via a UniformGrid — so the 3-column look is restored without giving up the
/// row-level virtualization the earlier perf pass introduced.
/// </summary>
public sealed class BoardingStayRowViewModel
{
    public BoardingStayRowViewModel(IReadOnlyList<BoardingStayCardViewModel> cards)
    {
        Cards = cards;
    }

    public IReadOnlyList<BoardingStayCardViewModel> Cards { get; }
}

/// <summary>
/// One entry of the page-chrome section switcher, mirroring the reference's
/// <c>#bd-main-seg</c> tabs (مدیریت اقامت / تاریخچه / تنظیمات): a Persian
/// label plus its icon key, with the active state driven by the page's
/// current section.
/// </summary>
public sealed partial class BoardingSectionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public BoardingSectionViewModel(string key, string label, string iconKey)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>board | history | settings — the reference's data-section keys.</summary>
    public string Key { get; }

    public string Label { get; }

    public string IconKey { get; }
}

/// <summary>
/// One status-filter tab in the toolbar's segmented rail, mirroring the
/// reference's <c>#bd-status-tabs</c>: a Persian label plus its live count
/// (rendered as Persian digits, reference <c>renderTabCounts</c>), with the
/// active state driven by the page's current tab.
/// </summary>
public sealed partial class BoardingTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = string.Empty;

    public BoardingTabViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>all | current | overdue | leaving | done — the reference's data-tab keys.</summary>
    public string Key { get; }

    public string Label { get; }
}

/// <summary>
/// One species option in the board's species dropdown, mirroring the
/// reference's <c>#bd-species-dropdown</c> (همه گونه‌ها + سگ/گربه/پرنده/خرگوش).
/// The «همه گونه‌ها» option has no glyph; the rest carry the species icon.
/// </summary>
public sealed partial class BoardingSpeciesOption : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public BoardingSpeciesOption(BoardingSpecies? value, string label, string? iconKey)
    {
        Value = value;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>null = «همه گونه‌ها»; otherwise the concrete species.</summary>
    public BoardingSpecies? Value { get; }

    public string Label { get; }

    /// <summary>Species glyph resource key, or null for the «همه گونه‌ها» option.</summary>
    public string? IconKey { get; }

    public bool HasIcon => IconKey is not null;
}

/// <summary>
/// Adapts a <see cref="BoardingStay"/> to one stay card of the board grid,
/// mirroring the reference's <c>renderCards</c>: the toned species-avatar head
/// with the status/room badges, the check-in/out strip, the stay-progress
/// timeline, the owner/cost footer and the «مشاهده جزئیات» CTA. Every value
/// (nights, elapsed, remaining, total cost, day label, tones) is derived the
/// same way the reference derives it.
/// </summary>
public sealed partial class BoardingStayCardViewModel : ObservableObject
{
    public BoardingStayCardViewModel(BoardingStay stay, DateOnly today)
    {
        Stay = stay;

        var cls = BoardingStayClassification.Of(stay, today);
        var nights = cls.Nights;
        var elapsed = cls.Elapsed;
        var remaining = cls.Remaining;
        var overdue = cls.IsOverdue;
        var leaving = cls.IsLeaving;

        IsOverdue = overdue;
        IsUrgent = leaving;
        // Head
        PetName = stay.PetName;
        BreedLine = $"{BoardingPresentation.SpeciesLabel(stay.Species)} · {stay.Breed}";
        SpeciesIconKey = BoardingPresentation.SpeciesIconKey(stay.Species);
        AvatarBrush = BoardingPresentation.AvatarBrush(stay.Tone);
        AvatarGlyphBrush = BoardingPresentation.AvatarGlyphBrush(stay.Tone);
        RoomText = $"اتاق {stay.Room}";

        var status = StatusMeta(stay, overdue, remaining);
        StatusLabel = status.Label;
        (StatusPillBackground, StatusPillForeground) = BoardingPresentation.PillBrushes(status.Tone);

        // Check-in / out strip
        CheckInText = Long(stay.CheckIn);
        CheckOutText = Long(stay.CheckOut);
        NightsText = $"{BoardingPresentation.Fa(nights)} شب";

        // Timeline
        ProgressPercent = Math.Min(100, (int)Math.Round(elapsed / (double)nights * 100));
        ProgressFill = BoardingPresentation.ProgressFill(overdue, leaving);
        ElapsedText = $"{BoardingPresentation.Fa(elapsed)} از {BoardingPresentation.Fa(nights)} شب گذشته";
        DayLabel = BuildDayLabel(stay, cls);
        DayLabelBrush = BoardingPresentation.DayLabelBrush(overdue, remaining);

        // Footer
        OwnerName = stay.OwnerName;
        TotalCostText = BoardingPresentation.Money(nights * stay.DailyRate + stay.ExtraItemsCost);
        PayLabel = stay.PaymentMode == BoardingPaymentMode.Prepaid ? "پیش‌پرداخت" : "پس‌پرداخت";
        (PayPillBackground, PayPillForeground) = BoardingPresentation.PillBrushes(
            stay.PaymentMode == BoardingPaymentMode.Prepaid ? "ok" : "info");

        // Card chrome
        CardBorderBrush = BoardingPresentation.CardBorderBrush(overdue, leaving);
        CardBackground = BoardingPresentation.CardBackground(overdue, leaving);
    }

    public BoardingStay Stay { get; }

    public bool IsOverdue { get; }
    public bool IsUrgent { get; }

    // Head
    public string PetName { get; }
    public string BreedLine { get; }
    public string SpeciesIconKey { get; }
    public IBrush AvatarBrush { get; }
    public IBrush AvatarGlyphBrush { get; }
    public string RoomText { get; }
    public string StatusLabel { get; }
    public IBrush StatusPillBackground { get; }
    public IBrush StatusPillForeground { get; }

    // Check-in / out strip
    public string CheckInText { get; }
    public string CheckOutText { get; }
    public string NightsText { get; }

    // Timeline
    public int ProgressPercent { get; }
    public IBrush ProgressFill { get; }
    public string ElapsedText { get; }
    public string DayLabel { get; }
    public IBrush DayLabelBrush { get; }

    // Footer
    public string OwnerName { get; }
    public string TotalCostText { get; }
    public string PayLabel { get; }
    public IBrush PayPillBackground { get; }
    public IBrush PayPillForeground { get; }

    // Card chrome
    public IBrush CardBorderBrush { get; }
    public IBrush CardBackground { get; }

    /// <summary>
    /// Reference <c>statusMeta</c>: done → تخلیه‌شده (mute); overdue →
    /// عقب‌افتاده (danger); remaining 0 → تخلیه امروز (warn); 1 → فردا تخلیه
    /// (info); otherwise اقامت فعلی (ok).
    /// </summary>
    public static (string Label, string Tone) StatusMeta(BoardingStay stay, bool overdue, int remaining)
    {
        if (stay.Status == BoardingStatus.Done)
        {
            return ("تخلیه‌شده", "mute");
        }

        if (overdue)
        {
            return ("عقب‌افتاده", "danger");
        }

        return remaining switch
        {
            0 => ("تخلیه امروز", "warn"),
            1 => ("فردا تخلیه", "info"),
            _ => ("اقامت فعلی", "ok"),
        };
    }

    /// <summary>Reference <c>renderCards</c> day-label text (the timeline's right caption).</summary>
    private static string BuildDayLabel(BoardingStay stay, BoardingStayClassification cls)
    {
        if (stay.Status == BoardingStatus.Done)
        {
            return "اقامت پایان یافت";
        }

        if (cls.IsOverdue)
        {
            return $"{BoardingPresentation.Fa(cls.OverdueDays)} روز عقب‌افتاده";
        }

        return cls.Remaining switch
        {
            0 => "امروز تخلیه می‌شود",
            1 => "فردا تخلیه می‌شود",
            _ => $"{BoardingPresentation.Fa(cls.Remaining)} روز مانده",
        };
    }

    // Reference J().long: the Jalali "long" shape (e.g. «۱۴ مهر ۱۴۰۴»), Persian digits.
    private static string Long(DateOnly date) =>
        JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).ToString("long");

    // Reference J().diff(a, b) → whole days from b to a.
}
