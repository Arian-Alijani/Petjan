using System.Windows.Input;
using Avalonia.Media;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// The single surface every history-feed row exposes to the shared row visual (see
/// <c>Resources/Controls/HistoryRow.axaml</c>). The main History page's
/// <see cref="HistoryEventRowViewModel"/> and each in-page tab's row VM (Sales / Boarding /
/// Vaccination) implement this so all four history surfaces render pixel-identically and every row is
/// clickable → opens its unified record screen. Presentation (brushes, labels, Persian digits) is
/// resolved via the single <see cref="HistoryPresentation"/> palette authority before binding.
/// </summary>
public interface IHistoryRowView
{
    string Title { get; }

    string Detail { get; }

    bool HasDetail { get; }

    string Time { get; }

    /// <summary>The readable party/entity label (never a raw id).</summary>
    string Entity { get; }

    /// <summary>The audit "who" — who performed the recorded action.</summary>
    string ActorName { get; }

    string ActorInitials { get; }

    string ActorRole { get; }

    /// <summary>The kind icon key (into <c>Resources/Theme/Icons.axaml</c>).</summary>
    string IconKey { get; }

    IBrush BadgeBrush { get; }

    IBrush BadgeIconBrush { get; }

    string CategoryLabel { get; }

    IBrush CategoryColorBrush { get; }

    IBrush CategoryBgBrush { get; }

    bool HasAmount { get; }

    string AmountText { get; }

    IBrush AmountBrush { get; }

    string StatusLabel { get; }

    IBrush StatusBrush { get; }

    IBrush StatusBgBrush { get; }

    /// <summary>Opens the unified record screen for this row; a no-op for inert rows.</summary>
    ICommand OpenCommand { get; }

    /// <summary>
    /// True when this row's record is an invoice the operator may still change (an open draft). Drives the
    /// row's «ویرایش» / «حذف» actions; every other surface leaves it false, so the action column stays empty
    /// instead of offering a click that could only fail.
    /// </summary>
    bool CanManageInvoice => false;

    /// <summary>
    /// True when the row's record is not yet applied on the server (queued locally, or refused by it). Only the
    /// surfaces that know the record's aggregate fill it; every other row shows no chip.
    /// </summary>
    bool HasSyncMark => false;

    /// <summary>The sync-mark chip text («در صف سرور» / «ناهماهنگ»).</summary>
    string SyncMarkLabel => string.Empty;

    /// <summary>The sync-mark explanation shown as the chip's tooltip.</summary>
    string SyncMarkTip => string.Empty;

    IBrush SyncMarkBrush => Brushes.Transparent;

    IBrush SyncMarkBgBrush => Brushes.Transparent;

    /// <summary>Opens the invoice for editing (draft only); null when the row is not a manageable invoice.</summary>
    ICommand? EditInvoiceCommand => null;

    /// <summary>Deletes the invoice after confirmation (draft only); null when the row is not manageable.</summary>
    ICommand? DeleteInvoiceCommand => null;
}
