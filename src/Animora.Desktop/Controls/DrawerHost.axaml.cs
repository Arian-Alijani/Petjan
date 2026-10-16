using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Controls;

/// <summary>
/// Right-anchored sliding overlay host for drawer content
/// (Animora-UI's side-drawer pattern, e.g. cheques add/edit drawer).
/// Managed by DialogService.
///
/// Open/close motion matches the app's overlay language: on show the host is
/// made visible and the <c>open</c> class is added on the next frame so the
/// backdrop fades in and the panel slides in from the trailing edge; on close
/// the class is removed so the slide-out plays before the host collapses
/// (transitions defined in DrawerHost.axaml, run on the compositor).
/// </summary>
public partial class DrawerHost : UserControl
{
    // Slightly longer than the longest exit transition (0.24s panel slide) so
    // the reverse tween is fully played before the host collapses.
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(260);

    private DispatcherTimer? _exitTimer;

    public event EventHandler? BackdropRequestedClose;

    public DrawerHost()
    {
        InitializeComponent();
    }

    public void SetContent(object? content, double width = 420)
    {
        if (content is not null)
        {
            StopExitTimer();
            Panel.Width = width;
            ContentHost.Content = content;
            IsVisible = true;

            Backdrop.Classes.Remove("open");
            Panel.Classes.Remove("open");
            Dispatcher.UIThread.Post(PlayOpen, DispatcherPriority.Render);
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        Backdrop.Classes.Remove("open");
        Panel.Classes.Remove("open");
        StartExitTimer();
    }

    private void PlayOpen()
    {
        if (!IsVisible)
        {
            return;
        }

        Backdrop.Classes.Add("open");
        Panel.Classes.Add("open");
    }

    private void StartExitTimer()
    {
        StopExitTimer();
        _exitTimer = new DispatcherTimer { Interval = ExitDuration };
        _exitTimer.Tick += OnExitTick;
        _exitTimer.Start();
    }

    private void StopExitTimer()
    {
        if (_exitTimer is null)
        {
            return;
        }

        _exitTimer.Stop();
        _exitTimer.Tick -= OnExitTick;
        _exitTimer = null;
    }

    private void OnExitTick(object? sender, EventArgs e)
    {
        StopExitTimer();
        IsVisible = false;
        ContentHost.Content = null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        BackdropRequestedClose?.Invoke(this, EventArgs.Empty);
}
