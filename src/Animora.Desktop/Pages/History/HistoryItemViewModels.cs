using System.Collections.Generic;
using Animora.Contracts.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.History;

/// <summary>One period chip of the toolbar's period rail (the reference's <c>renderPeriodChips</c>).</summary>
public sealed partial class HistoryPeriodChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public HistoryPeriodChipViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>today | 7 | 30 | all — the reference's period ids.</summary>
    public string Key { get; }

    public string Label { get; }
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
}

/// <summary>
/// One event row of the ledger (the reference's row renderer): the kind icon
/// badge, time, description lines, entity, signed amount and status chip.
/// </summary>
public sealed class HistoryEventRowViewModel
{
    public HistoryEventRowViewModel(HistoryEvent ev)
    {
        Title = ev.Title;
        Detail = ev.Detail;
        HasDetail = !string.IsNullOrEmpty(ev.Detail);
        Time = string.IsNullOrEmpty(ev.Time) ? "—" : HistoryPresentation.FaText(ev.Time);
        Entity = string.IsNullOrEmpty(ev.Entity) ? "—" : ev.Entity;

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
