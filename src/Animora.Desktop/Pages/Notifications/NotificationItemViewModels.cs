using System;
using Animora.Contracts.Notifications;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Notifications;

/// <summary>One message card: kind-driven icon/colours, a coloured tag chip, relative time and read/expanded state.</summary>
public sealed partial class NotificationItemViewModel : ObservableObject
{
    public NotificationItemViewModel(NotificationDto dto, DateTimeOffset nowUtc, TimeZoneInfo zone, bool isExpanded)
    {
        Id = dto.Id;
        Kind = dto.Kind;
        Title = dto.Title;
        Body = dto.Body;
        TagText = string.IsNullOrWhiteSpace(dto.Tag) ? NotificationsPresentation.KindLabel(dto.Kind) : dto.Tag!;
        RelativeTime = NotificationsPresentation.FormatWhen(dto.PublishedAtUtc ?? dto.CreatedAtUtc, nowUtc, zone);
        var (icon, accent, soft) = NotificationsPresentation.Palette(dto.Kind);
        IconKey = icon;
        AccentBrush = BrushCache.Solid(accent);
        SoftBrush = BrushCache.Solid(soft);
        _isRead = dto.IsRead;
        _isExpanded = isExpanded;
    }

    public string Id { get; }

    public NotificationKind Kind { get; }

    public string Title { get; }

    public string Body { get; }

    public string TagText { get; }

    public string RelativeTime { get; }

    public string IconKey { get; }

    public IBrush AccentBrush { get; }

    public IBrush SoftBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    private bool _isRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyMaxLines))]
    private bool _isExpanded;

    public bool IsUnread => !IsRead;

    /// <summary>Collapsed cards clamp the text to two lines; an opened card shows it all (0 = unlimited).</summary>
    public int BodyMaxLines => IsExpanded ? 0 : 2;
}

/// <summary>A filter chip (scope / kind).</summary>
public sealed partial class NotificationChipViewModel(string key, string label) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isActive;
}
