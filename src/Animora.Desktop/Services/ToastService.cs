using System.Collections.ObjectModel;
using Animora.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Animora.Desktop.Services;

/// <summary>
/// In-memory implementation of <see cref="IToastService"/>. Holds the live
/// collection a <see cref="ToastHost"/> binds to. Each message resolves its
/// tone-specific visual tokens (icon glyph + accent/soft colours) up front, then
/// runs a short enter → dwell → exit lifecycle: it is added closed (so it starts
/// off-screen and transparent), flipped open a frame later to glide in, left on
/// screen for the dwell window, then flipped closed so it fades + slides out
/// before it is finally removed from the collection.
/// </summary>
public sealed class ToastService : IToastService
{
    // Time the toast stays fully visible before it starts animating out.
    private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(2800);

    // Must match the enter/exit transition duration declared in ToastHost.axaml
    // so the card has fully faded + slid away before it leaves the collection.
    private static readonly TimeSpan ExitAnimation = TimeSpan.FromMilliseconds(260);

    public ObservableCollection<ToastMessage> Toasts { get; } = [];

    public void Show(string text, Tone tone = Tone.Ok)
    {
        var (iconKey, accentKey, iconBgKey) = tone switch
        {
            Tone.Danger => ("IconAlert", "DangerBrush", "DangerBgBrush"),
            Tone.Warn => ("IconClock", "WarnBrush", "WarnBgBrush"),
            Tone.Info => ("IconInfo", "InfoBrush", "InfoBgBrush"),
            Tone.Brand => ("IconBell", "Brand600Brush", "Brand50Brush"),
            _ => ("IconCheckCircle", "OkBrush", "OkBgBrush"),
        };

        var message = new ToastMessage
        {
            Text = text,
            Tone = tone,
            Icon = FindResource<Geometry>(iconKey),
            AccentBrush = FindResource<IBrush>(accentKey) ?? Brushes.Transparent,
            IconBackground = FindResource<IBrush>(iconBgKey) ?? Brushes.Transparent,
        };

        Toasts.Add(message);

        // Flip to the open state on the next dispatcher turn so the control is
        // already laid out in its "closed" visual state when the transition
        // starts — otherwise the enter tween has nothing to animate from.
        Dispatcher.UIThread.Post(() => message.IsOpen = true, DispatcherPriority.Render);

        var dwell = new DispatcherTimer { Interval = Dwell };
        dwell.Tick += (_, _) =>
        {
            dwell.Stop();

            // Begin the exit transition, then remove once it has played out.
            message.IsOpen = false;

            var exit = new DispatcherTimer { Interval = ExitAnimation };
            exit.Tick += (_, _) =>
            {
                exit.Stop();
                Toasts.Remove(message);
            };
            exit.Start();
        };
        dwell.Start();
    }

    // Icon geometries and tone brushes live in Application.Resources (merged from
    // Resources/Theme/*). TryFindResource is an IResourceHost extension from
    // Avalonia.Controls; Application implements IResourceHost. Returns false
    // (never UnsetValue) on a miss, so we never cast the unset sentinel.
    private static T? FindResource<T>(string key) where T : class
    {
        if (global::Avalonia.Application.Current is { } app &&
            app.TryFindResource(key, out var value) &&
            value is T typed)
        {
            return typed;
        }

        return null;
    }
}
