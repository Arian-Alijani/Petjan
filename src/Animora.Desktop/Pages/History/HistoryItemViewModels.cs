using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Animora.Contracts.History;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// One preset of the ledger toolbar's date range: a key, the Persian label and
/// the resolved inclusive Jalali <see cref="From"/>/<see cref="To"/> bounds
/// (empty = unbounded). Selecting one swaps the free date pickers.
/// </summary>
public sealed partial class HistoryRangeViewModel : ObservableObject
{
    public HistoryRangeViewModel(string key, string label, string from, string to)
    {
        Key = key;
        Label = label;
        From = from;
        To = to;
    }

    /// <summary>today | 7 | 30 | all — the range preset keys.</summary>
    public string Key { get; }

    public string Label { get; }

    /// <summary>Inclusive lower bound (ASCII Jalali), or empty for unbounded.</summary>
    public string From { get; }

    /// <summary>Inclusive upper bound (ASCII Jalali), or empty for unbounded.</summary>
    public string To { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// One module-kind category chip of the filter row (the reference's
/// <c>renderCatChips</c>): its key, label, icon and per-kind accent tones.
/// </summary>
public sealed partial class HistoryCategoryChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public HistoryCategoryChipViewModel(HistoryEventKind kind)
    {
        Kind = kind;
        Label = HistoryPresentation.KindLabel(kind);
        IconKey = HistoryPresentation.KindIcon(kind);
        Color = HistoryPresentation.KindColor(kind);
        Background = HistoryPresentation.KindBg(kind);
        ColorBrush = HistoryPresentation.Fill(Color);
        BackgroundBrush = HistoryPresentation.Fill(Background);
    }

    public HistoryEventKind Kind { get; }

    public string Label { get; }

    public string IconKey { get; }

    public string Color { get; }

    public string Background { get; }

    public IBrush ColorBrush { get; }

    public IBrush BackgroundBrush { get; }

    /// <summary>Chip fill: the kind's tinted surface when active, transparent otherwise.</summary>
    public IBrush ChipBackground => IsActive ? BackgroundBrush : Brushes.Transparent;

    /// <summary>Chip border: the kind's accent colour when active, a muted line otherwise.</summary>
    public IBrush ChipBorder => IsActive ? ColorBrush : HistoryPresentation.Fill("#e4e7ec");

    /// <summary>Label colour: the kind's accent when active, a muted ink otherwise.</summary>
    public IBrush LabelBrush => IsActive ? ColorBrush : HistoryPresentation.Fill("#475467");
}

/// <summary>
/// One event row of the ledger (the reference's row renderer): the kind icon
/// badge, time, description lines, party entity, the audit «کاربر» (actor),
/// signed amount and status chip.
/// </summary>
public sealed class HistoryEventRowViewModel : IHistoryRowView
{
    /// <param name="ev">The ledger event (its <c>Entity</c> is a <c>Type:Id</c> pair).</param>
    /// <param name="open">
    /// Opens the unified record screen for this row's entity (injected from the page VM, routed through
    /// <see cref="Services.IHistoryRecordOpener"/>). Null for inert rows.
    /// </param>
    public HistoryEventRowViewModel(HistoryEvent ev, Func<string, string, Task>? open = null)
    {
        Title = ev.Title;
        Detail = ev.Detail;
        HasDetail = !string.IsNullOrEmpty(ev.Detail);
        Time = string.IsNullOrEmpty(ev.Time) ? "—" : HistoryPresentation.FaText(ev.Time);

        // ev.Entity arrives as "Type:Id" (e.g. "Invoice:01J…"). Split it: the type drives navigation and
        // a readable Persian label, the id is passed to the opener. Never show the raw ULID to the user.
        var raw = ev.Entity ?? string.Empty;
        var colon = raw.IndexOf(':');
        if (colon > 0)
        {
            EntityType = raw[..colon];
            EntityId = raw[(colon + 1)..];
        }
        else
        {
            EntityType = string.Empty;
            EntityId = raw;
        }

        Entity = string.IsNullOrEmpty(EntityType) ? "—" : HistoryText.EntityLabel(EntityType);
        CanOpen = !string.IsNullOrEmpty(EntityType) && !string.IsNullOrEmpty(EntityId);
        OpenCommand = new AsyncRelayCommand(
            () => open is not null && CanOpen ? open(EntityType, EntityId) : Task.CompletedTask,
            () => CanOpen && open is not null);

        // The audit "who" (R1).
        ActorName = ev.Actor.Name;
        ActorInitials = ev.Actor.Initials;
        ActorRole = ev.Actor.RoleLabel;

        IconKey = HistoryPresentation.KindIcon(ev.Kind);
        BadgeBrush = HistoryPresentation.Fill(HistoryPresentation.KindBg(ev.Kind));
        BadgeIconBrush = HistoryPresentation.Fill(HistoryPresentation.KindColor(ev.Kind));

        CategoryLabel = HistoryPresentation.KindLabel(ev.Kind);
        CategoryColorBrush = HistoryPresentation.Fill(HistoryPresentation.KindColor(ev.Kind));
        CategoryBgBrush = HistoryPresentation.Fill(HistoryPresentation.KindBg(ev.Kind));

        var hasAmount = ev.Amount != 0 || ev.AmountSign != 0;
        HasAmount = hasAmount;
        if (!hasAmount)
        {
            AmountText = "—";
            AmountBrush = HistoryPresentation.Fill("#667085");
        }
        else
        {
            var sign = ev.AmountSign < 0 ? "−" : "+";
            AmountText = sign + HistoryPresentation.Money(ev.Amount);
            AmountBrush = HistoryPresentation.Fill(ev.AmountSign < 0 ? "#d92d20" : "#027a48");
        }

        StatusLabel = HistoryPresentation.StatusLabel(ev.Status);
        StatusBrush = HistoryPresentation.Fill(HistoryPresentation.StatusColor(ev.Status));
        StatusBgBrush = HistoryPresentation.Fill(HistoryPresentation.StatusBg(ev.Status));
    }

    public string Title { get; }

    public string Detail { get; }

    public bool HasDetail { get; }

    public string Time { get; }

    public string Entity { get; }

    /// <summary>The feed entity's source-aggregate tag (e.g. "Invoice", "Pet"); drives navigation.</summary>
    public string EntityType { get; }

    /// <summary>The feed entity's id (never shown raw to the user).</summary>
    public string EntityId { get; }

    /// <summary>True when this row maps to an openable record (non-empty type + id).</summary>
    public bool CanOpen { get; }

    /// <summary>Opens the unified record screen for this row (invoice sheet / pet sheet / page).</summary>
    public ICommand OpenCommand { get; }

    /// <summary>The audit "who" — the user who performed the recorded action (R1).</summary>
    public string ActorName { get; }

    public string ActorInitials { get; }

    public string ActorRole { get; }

    public string IconKey { get; }

    public IBrush BadgeBrush { get; }

    public IBrush BadgeIconBrush { get; }

    public string CategoryLabel { get; }

    public IBrush CategoryColorBrush { get; }

    public IBrush CategoryBgBrush { get; }

    public bool HasAmount { get; }

    public string AmountText { get; }

    public IBrush AmountBrush { get; }

    public string StatusLabel { get; }

    public IBrush StatusBrush { get; }

    public IBrush StatusBgBrush { get; }
}

/// <summary>
/// A day group of the ledger (the reference's <c>groupByDay</c> output): the
/// sticky date header (label + date + count badge + net amount) plus its rows.
/// </summary>
public sealed class HistoryDayGroupViewModel
{
    public HistoryDayGroupViewModel(string jdate, string label, int count, long net, IReadOnlyList<HistoryEventRowViewModel> rows)
    {
        DateText = HistoryPresentation.FaText(jdate);
        Label = label;
        CountText = HistoryPresentation.Fa(count) + " رویداد";
        HeaderText = label + " – " + DateText;
        Rows = rows;

        HasNet = net != 0;
        if (HasNet)
        {
            NetText = (net > 0 ? "+" : "−") + HistoryPresentation.Money(System.Math.Abs(net)) + " ت";
            NetBrush = HistoryPresentation.Fill(net >= 0 ? "#027a48" : "#d92d20");
        }
        else
        {
            NetText = string.Empty;
            NetBrush = HistoryPresentation.Fill("#667085");
        }
    }

    public string Label { get; }

    public string DateText { get; }

    /// <summary>The sticky day header's single composed line («امروز – ۱۴۰۵/...»).</summary>
    public string HeaderText { get; }

    public string CountText { get; }

    public bool HasNet { get; }

    public string NetText { get; }

    public IBrush NetBrush { get; }

    public IReadOnlyList<HistoryEventRowViewModel> Rows { get; }
}
