using Animora.Contracts.Sync;
using Animora.Desktop.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// How one record's server-queue state reads in a history/list row (SYN-02): the chip label, its explanatory tooltip
/// and its palette. Built once per row from the O(1) <see cref="SyncQueueSnapshot"/> lookup — a record with no mark
/// (already applied on the server) carries <see cref="None"/> and renders nothing.
/// </summary>
public sealed record SyncMarkView(bool Has, string Label, string Tip, IBrush Foreground, IBrush Background)
{
    /// <summary>The "nothing to show" mark: applied on the server, or never queued on this device.</summary>
    public static readonly SyncMarkView None = new(false, string.Empty, string.Empty, Brushes.Transparent, Brushes.Transparent);

    private static readonly SyncMarkView QueuedView = new(
        true,
        "در صف سرور",
        "این مورد فقط به‌صورت محلی ثبت شده و پس از اتصال به سرور اعمال می‌شود.",
        HistoryPresentation.ToneBrush(Tone.Warn),
        HistoryPresentation.ToneBgBrush(Tone.Warn));

    private static readonly SyncMarkView AttentionView = new(
        true,
        "ناهماهنگ",
        "اعمال روی سرور با خطا مواجه شد؛ نیاز به بررسی.",
        HistoryPresentation.ToneBrush(Tone.Danger),
        HistoryPresentation.ToneBgBrush(Tone.Danger));

    /// <summary>The view of a queue mark.</summary>
    public static SyncMarkView Of(SyncQueueMark mark) => mark switch
    {
        SyncQueueMark.Queued => QueuedView,
        SyncQueueMark.NeedsAttention => AttentionView,
        _ => None,
    };

    /// <summary>The view of the record <paramref name="aggregateType"/>/<paramref name="aggregateId"/> in <paramref name="queue"/>.</summary>
    public static SyncMarkView For(SyncQueueSnapshot? queue, string? aggregateType, string? aggregateId) =>
        queue is null ? None : Of(queue.MarkFor(aggregateType, aggregateId));
}
