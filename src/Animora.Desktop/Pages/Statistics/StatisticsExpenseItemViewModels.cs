using Animora.Contracts.Statistics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>One selectable chip (category / settlement / filter) of the expenses UI.</summary>
public sealed partial class StatisticsChoice(string key, string label, string? colorHex = null) : ObservableObject
{
    /// <summary>The code the chip stands for (category code, settlement name; empty = «همه»).</summary>
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>Accent swatch (hex) or <see langword="null"/> for a plain chip.</summary>
    public string? ColorHex { get; } = colorHex;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One ledger row of the expenses list.</summary>
public sealed class StatisticsExpenseRowViewModel
{
    public StatisticsExpenseRowViewModel(ExpenseRow row)
    {
        Row = row;
        Number = StatisticsFormat.Digits(row.Number);
        Title = row.Title;
        CategoryName = StatisticsFormat.CategoryName(row.CategoryCode);
        CategoryBrush = StatisticsFormat.Brush(StatisticsFormat.CategoryColor(row.CategoryCode));
        DateText = StatisticsFormat.JalaliShort(row.IncurredUtc);
        IsReversal = row.ReversesExpenseId is not null;
        IsReversed = row.Status == ExpenseStatus.Reversed;
        Amount = (IsReversal ? "−" : string.Empty) + StatisticsFormat.Money(row.AmountMinor);
        SettlementText = StatisticsFormat.SettlementName(row.Settlement);
        BadgeText = IsReversal ? "برگشتی" : IsReversed ? "برگشت‌خورده" : string.Empty;
        HasBadge = BadgeText.Length > 0;
        Subtitle = string.IsNullOrWhiteSpace(row.Note) ? SettlementText : $"{SettlementText} · {row.Note}";
    }

    public ExpenseRow Row { get; }

    public string Number { get; }

    public string Title { get; }

    public string CategoryName { get; }

    public IBrush CategoryBrush { get; }

    public string DateText { get; }

    public string Amount { get; }

    public string SettlementText { get; }

    public string Subtitle { get; }

    public string BadgeText { get; }

    public bool HasBadge { get; }

    public bool IsReversal { get; }

    public bool IsReversed { get; }

    public bool CanReverse => Row.CanReverse;
}

/// <summary>One fixed (monthly) expense of the «هزینه‌های ثابت» panel.</summary>
public sealed class StatisticsRecurringRowViewModel
{
    public StatisticsRecurringRowViewModel(RecurringExpenseItem item)
    {
        Item = item;
        Title = item.Title;
        CategoryName = StatisticsFormat.CategoryName(item.CategoryCode);
        CategoryBrush = StatisticsFormat.Brush(StatisticsFormat.CategoryColor(item.CategoryCode));
        Amount = StatisticsFormat.Money(item.AmountMinor);
        var (text, fg, bg) = Look(item.Status);
        StatusText = text;
        StatusForeground = StatisticsFormat.Brush(fg);
        StatusBackground = StatisticsFormat.Brush(bg);
        DueText = item.Status switch
        {
            RecurringStatus.Paid => item.PaidUtc is { } paid ? $"پرداخت: {StatisticsFormat.JalaliShort(paid)}" : "پرداخت‌شده",
            RecurringStatus.Inactive => $"روز {StatisticsFormat.Count(item.DayOfMonth)} هر ماه",
            RecurringStatus.Overdue => $"سررسید: {StatisticsFormat.JalaliMedium(item.DueDay)} · {StatisticsFormat.Count(-item.DaysUntilDue)} روز گذشته",
            RecurringStatus.Due => $"سررسید: امروز ({StatisticsFormat.JalaliMedium(item.DueDay)})",
            _ => $"سررسید: {StatisticsFormat.JalaliMedium(item.DueDay)} · {StatisticsFormat.Count(item.DaysUntilDue)} روز دیگر",
        };
        ToggleText = item.IsActive ? "غیرفعال‌سازی" : "فعال‌سازی";
    }

    public RecurringExpenseItem Item { get; }

    public string Title { get; }

    public string CategoryName { get; }

    public IBrush CategoryBrush { get; }

    public string Amount { get; }

    public string StatusText { get; }

    public IBrush StatusForeground { get; }

    public IBrush StatusBackground { get; }

    public string DueText { get; }

    public string ToggleText { get; }

    public bool CanPay => Item.CanPay;

    public bool IsActive => Item.IsActive;

    /// <summary>Inactive templates are dimmed.</summary>
    public double RowOpacity => Item.IsActive ? 1d : 0.55d;

    private static (string Text, string Fg, string Bg) Look(RecurringStatus status) => status switch
    {
        RecurringStatus.Paid => ("پرداخت‌شده", "#067647", "#ecfdf3"),
        RecurringStatus.Upcoming => ("در پیش", "#475467", "#f2f4f7"),
        RecurringStatus.DueSoon => ("سررسید نزدیک", "#b54708", "#fffaeb"),
        RecurringStatus.Due => ("سررسید امروز", "#b54708", "#fffaeb"),
        RecurringStatus.Overdue => ("معوق", "#b42318", "#fef3f2"),
        _ => ("غیرفعال", "#98a2b3", "#f2f4f7"),
    };
}

/// <summary>One legend row of the per-category expense breakdown.</summary>
public sealed record StatisticsCategoryRow(string Name, string Amount, string Share, string Delta, bool IsDeltaPositive,
    double Fraction, IBrush Color);
