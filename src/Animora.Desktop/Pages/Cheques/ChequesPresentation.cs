using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>Persian labels / tones for cheque states and lifecycle actions (single source for list, detail and history).</summary>
internal static class ChequesPresentation
{
    public static string StateLabel(ChequeStateDto state) => state switch
    {
        ChequeStateDto.Registered => "ثبت‌شده",
        ChequeStateDto.InPortfolio => "در پرتفوی",
        ChequeStateDto.Deposited => "واگذار به بانک",
        ChequeStateDto.Cleared => "وصول‌شده",
        ChequeStateDto.Bounced => "برگشتی",
        ChequeStateDto.Returned => "عودت‌شده",
        ChequeStateDto.Cancelled => "ابطال‌شده",
        _ => "تسویه نقدی",
    };

    public static Tone StateTone(ChequeStateDto state) => state switch
    {
        ChequeStateDto.Cleared or ChequeStateDto.Settled => Tone.Ok,
        ChequeStateDto.Bounced => Tone.Danger,
        ChequeStateDto.Deposited => Tone.Info,
        ChequeStateDto.InPortfolio => Tone.Brand,
        ChequeStateDto.Registered => Tone.Warn,
        _ => Tone.Neutral,
    };

    public static string ActionLabel(ChequeActionDto action) => action switch
    {
        ChequeActionDto.MoveToPortfolio => "بایگانی در پرتفوی",
        ChequeActionDto.Deposit => "واگذاری به بانک",
        ChequeActionDto.MarkCleared => "وصول شد",
        ChequeActionDto.MarkBounced => "برگشت خورد",
        ChequeActionDto.Return => "عودت",
        ChequeActionDto.Cancel => "ابطال",
        _ => "تسویه نقدی",
    };

    public static bool NeedsReason(ChequeActionDto action) =>
        action is ChequeActionDto.MarkBounced or ChequeActionDto.Return or ChequeActionDto.Cancel;

    public static bool IsDestructive(ChequeActionDto action) =>
        action is ChequeActionDto.MarkBounced or ChequeActionDto.Cancel;

    public static string Money(long minor) => PersianNumberFormatter.FormatGrouped(minor) + " تومان";

    public static string DueHint(ChequeItem c)
    {
        if (c.State is ChequeStateDto.Cleared or ChequeStateDto.Returned or ChequeStateDto.Cancelled or ChequeStateDto.Settled)
            return string.Empty;
        var d = c.DaysUntilDue;
        var n = PersianNumberFormatter.ToPersianDigits(Math.Abs(d));
        return d < 0 ? $"{n} روز گذشته" : d == 0 ? "امروز" : $"{n} روز مانده";
    }

    public static string Message(InvalidOperationException ex, string fallback) =>
        ex is Animora.Client.Services.ServiceException se
            ? ErrorMessages.TryGet(se.Code) ?? fallback
            : fallback;
}

public sealed partial class ChequeChipViewModel(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isActive;
}

public sealed class ChequeRowViewModel
{
    public ChequeRowViewModel(ChequeItem item)
    {
        Item = item;
        Id = item.Id;
        IsInbound = item.Direction == ChequeDirectionDto.Inbound;
        DirectionLabel = IsInbound ? "دریافتی" : "پرداختی";
        Number = PersianNumberFormatter.ToPersianDigits(item.Number);
        Party = string.IsNullOrWhiteSpace(item.Party) ? "—" : item.Party;
        Bank = string.IsNullOrWhiteSpace(item.Bank) ? "—" : item.Bank!;
        AmountText = ChequesPresentation.Money(item.AmountMinor);
        DueDate = PersianNumberFormatter.ToPersianDigits(item.DueDate);
        DueHint = ChequesPresentation.DueHint(item);
        StateLabel = ChequesPresentation.StateLabel(item.State);
        StateTone = ChequesPresentation.StateTone(item.State);
        DirectionTone = IsInbound ? Tone.Ok : Tone.Info;
    }

    public ChequeItem Item { get; }
    public string Id { get; }
    public bool IsInbound { get; }
    public string DirectionLabel { get; }
    public Tone DirectionTone { get; }
    public string Number { get; }
    public string Party { get; }
    public string Bank { get; }
    public string AmountText { get; }
    public string DueDate { get; }
    public string DueHint { get; }
    public string StateLabel { get; }
    public Tone StateTone { get; }
}
