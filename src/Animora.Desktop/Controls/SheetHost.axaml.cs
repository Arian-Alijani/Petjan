using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Controls;

/// <summary>
/// Centered dim-backdrop overlay host for the Patients records "sheet" pattern
/// (Animora-UI's <c>.sheet-root</c> / <c>.sheet.owner-sheet</c> /
/// <c>.sheet.pet-sheet</c>). Unlike <see cref="ModalHost"/> (a fixed-width
/// dialog) and <see cref="DrawerHost"/> (a right-anchored panel), a sheet is a
/// large, centered, elevated surface whose size differs per record — the owner
/// file is 680×760, the patient file 1120×880. Managed by DialogService via
/// <see cref="SetContent(object?, double, double)"/>.
///
/// Open/close motion mirrors <see cref="ModalHost"/>: the <c>open</c> class is
/// added one frame after the host is shown (backdrop fades in, panel scales up)
/// and removed on close so the reverse tween plays before the host collapses —
/// a single, consistent overlay language across the app, driven entirely by the
/// compositor transitions declared in SheetHost.axaml.
/// </summary>
public partial class SheetHost : UserControl
{
    // Slightly longer than the longest exit transition (0.24s panel) so the
    // reverse tween is fully played before the host collapses.
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(260);

    private DispatcherTimer? _exitTimer;

    public event EventHandler? BackdropRequestedClose;

    public SheetHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows <paramref name="content"/> inside a centered sheet of the given
    /// width/height (transcribed from the reference's <c>.owner-sheet</c> /
    /// <c>.pet-sheet</c> rules) and animates the host open. Passing null content
    /// animates the host closed and then hides it.
    /// </summary>
    public void SetContent(object? content, double width = 680, double height = 760)
    {
        if (content is not null)
        {
            StopExitTimer();
            Panel.Width = width;
            Panel.Height = height;
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
