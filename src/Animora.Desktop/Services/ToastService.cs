using System.Collections.ObjectModel;
using Animora.Desktop.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Animora.Desktop.Services;

/// <summary>
/// In-memory implementation of <see cref="IToastService"/>. Holds the live
/// collection a <see cref="ToastHost"/> binds to; each message auto-removes
/// itself after a fixed delay, matching the reference's ~2.6s toast timeout.
/// </summary>
public sealed class ToastService : IToastService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMilliseconds(2600);

    public ObservableCollection<ToastMessage> Toasts { get; } = [];

    public void Show(string text, Tone tone = Tone.Ok)
    {
        var iconKey = tone switch
        {
            Tone.Danger => "IconX",
            Tone.Warn => "IconClock",
            Tone.Info => "IconSearch",
            _ => "IconCheck",
        };
        Application.Current?.TryFindResource(iconKey, out var iconResource);
        var icon = iconResource as Geometry;

        var message = new ToastMessage { Text = text, Tone = tone, Icon = icon };
        Toasts.Add(message);

        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(message);
        };
        timer.Start();
    }
}
