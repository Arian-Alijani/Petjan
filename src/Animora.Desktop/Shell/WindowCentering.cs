using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Animora.Desktop.Shell;

/// <summary>
/// Keeps a window centered on the <b>active</b> monitor every time it is shown.
///
/// Avalonia's <see cref="WindowStartupLocation.CenterScreen"/> only runs on the
/// very first show and, on multi-monitor setups, can center against the primary
/// screen (or a stale size) instead of where the user is looking. It is also
/// skipped entirely when a window is re-revealed from a hidden/tray state via
/// <see cref="Window.Show()"/>. This helper fixes both: it re-centers on
/// <see cref="Window.Opened"/> and whenever the window transitions to visible,
/// using the <see cref="Screens"/> API to pick the screen the window overlaps
/// (then the one under the pointer, then the primary) and placing the window at
/// that screen's working-area center.
/// </summary>
internal static class WindowCentering
{
    /// <summary>
    /// Switches <paramref name="window"/> to manual positioning and wires the
    /// re-centering handlers. Call once from the window constructor, after
    /// <c>InitializeComponent()</c>.
    /// </summary>
    public static void Enable(Window window)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));

        // We own positioning now; the XAML CenterScreen hint is bypassed.
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        window.Opened += (_, _) => CenterOnActiveScreen(window);

        // Re-reveal from hidden/tray (Show() on an already-created window) does not
        // re-run startup location; catch the visibility flip and re-center.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && e.NewValue is true)
            {
                CenterOnActiveScreen(window);
            }
        };
    }

    private static void CenterOnActiveScreen(Window window)
    {
        // Only meaningful for a normal, visible, framed window — never fight a
        // maximized/minimized state, and never touch a window that isn't shown.
        if (!window.IsVisible || window.WindowState != WindowState.Normal)
        {
            return;
        }

        var screens = window.Screens;
        if (screens is null)
        {
            return;
        }

        // Prefer the screen the window already overlaps, then the one under the
        // pointer, then the primary — mirrors where the user is actually looking.
        // ScreenFromWindow throws if the platform window was already disposed
        // (a reveal/close race); treat that as "unknown screen" and bail out.
        Screen? screen;
        try
        {
            screen = screens.ScreenFromWindow(window);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        screen ??= screens.ScreenFromPoint(window.Position)
                   ?? screens.Primary
                   ?? (screens.ScreenCount > 0 ? screens.All[0] : null);

        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling;

        // Window.Width/Height are the requested logical (DIP) size; fall back to the
        // measured client size when they are unset (NaN). Convert to device pixels so
        // the geometry matches the screen's WorkingArea (which is in device pixels).
        var widthDip = double.IsNaN(window.Width) ? window.ClientSize.Width : window.Width;
        var heightDip = double.IsNaN(window.Height) ? window.ClientSize.Height : window.Height;
        var widthPx = (int)Math.Round(widthDip * scaling);
        var heightPx = (int)Math.Round(heightDip * scaling);

        var x = area.X + (area.Width - widthPx) / 2;
        var y = area.Y + (area.Height - heightPx) / 2;

        // Never let the title bar slip above/left of the working area on a small screen.
        if (x < area.X) x = area.X;
        if (y < area.Y) y = area.Y;

        window.Position = new PixelPoint(x, y);
    }
}
