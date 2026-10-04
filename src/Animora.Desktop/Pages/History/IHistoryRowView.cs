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
}
