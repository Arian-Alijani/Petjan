using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Controls;

/// <summary>
/// Centered dim-backdrop overlay host for modal content
/// (Animora-UI's .modal-backdrop/.modal). Managed by DialogService.
///
/// The open/close motion is a minimal, modern "pop": on show the host becomes
/// visible and the <c>open</c> class is added on the next frame so the backdrop
/// fades in and the panel scales/slides to its natural pose (transitions defined
/// in ModalHost.axaml, run on the compositor — no UI-thread cost). On close the
/// class is removed so the same transitions play in reverse, and the host is
/// only collapsed (IsVisible=false, content cleared) once that exit tween has
/// finished — so dialogs never disappear with a hard cut.
/// </summary>
public partial class ModalHost : UserControl
{
    // Slightly longer than the longest exit transition (0.22s panel) so the
    // reverse tween is fully played before the host collapses.
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(240);

    private DispatcherTimer? _exitTimer;

    public event EventHandler? BackdropRequestedClose;

    public ModalHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows <paramref name="content"/> in the centered modal panel, capped at
    /// <paramref name="maxWidth"/> (reference <c>.modal</c> = 520;
    /// <c>.modal.wide</c> = 760 — used by the owner/pet wizard + add-pet modal).
    /// Passing null content animates the host closed and then hides it.
    /// </summary>
    public void SetContent(object? content, double maxWidth = 520)
    {
        if (content is not null)
        {
            StopExitTimer();
            Panel.MaxWidth = maxWidth;
            ContentHost.Content = content;
            IsVisible = true;

            // Start from the closed pose, then flip to open on the next frame so
            // the transitions have a from→to delta to animate.
            Backdrop.Classes.Remove("open");
            Panel.Classes.Remove("open");
            Dispatcher.UIThread.Post(PlayOpen, DispatcherPriority.Render);
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        // Animate out: drop the open class so the reverse transitions run, then
        // collapse the host (and release the content) once they have finished.
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
