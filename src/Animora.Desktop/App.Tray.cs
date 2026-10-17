using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Animora.Desktop.Pages.Login;
using Animora.Desktop.Shell;

namespace Animora.Desktop;

/// <summary>
/// System-tray behaviour: tray menu handlers, show/restore of the main window, real exit, and the hidden
/// Windows auto-start (<c>--startup</c>) boot. The tray icon itself is declared in <c>App.axaml</c>.
/// </summary>
public partial class App
{
    /// <summary>True once a real shutdown has been requested (tray «خروج», OS shutdown, sign-in window closed).</summary>
    internal static bool IsExiting => _exiting;

    private static IClassicDesktopStyleApplicationLifetime? Desktop =>
        Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    // ---- tray handlers (wired from App.axaml) ------------------------------------------------------------------

    private void OnTrayClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayOpenClick(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayExitClick(object? sender, EventArgs e) => ExitApplication();

    // ---- show / exit -------------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the app's current window to the front: restores the hidden shell (or the sign-in window when
    /// nobody is signed in). Safe to call from any thread and from the tray, a second launch, or the OS.
    /// </summary>
    internal static void ShowMainWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowMainWindow);
            return;
        }

        if (_exiting || Desktop?.MainWindow is not { } window) return;

        // A user-initiated open also cancels a still-pending hidden sign-in reveal.
        if (window is LoginWindow) _hiddenStartPending = false;

        RestoreWindow(window);
    }

    private static void RestoreWindow(Window window)
    {
        window.Opacity = 1;
        window.ShowInTaskbar = true;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Fully quits the app (the only exit besides closing the sign-in window / OS shutdown).</summary>
    internal static void ExitApplication()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ExitApplication);
            return;
        }

        if (_exiting) return;
        Desktop?.Shutdown();
    }

    // ---- hidden --startup boot ---------------------------------------------------------------------------------

    /// <summary>
    /// Keeps the sign-in window invisible during a Windows auto-start boot. The lifetime shows it as soon as it
    /// is assigned as <c>MainWindow</c>, so it is made transparent + off the taskbar and hidden right after it
    /// opens; <see cref="RevealPendingStartupLogin"/> brings it back only if the user must actually sign in.
    /// </summary>
    private static void HideForStartup(LoginWindow login)
    {
        login.Opacity = 0;
        login.ShowInTaskbar = false;
        login.ShowActivated = false;

        void OnOpened(object? sender, EventArgs args)
        {
            login.Opened -= OnOpened;
            if (_hiddenStartPending) login.Hide();
        }
        login.Opened += OnOpened;
    }

    /// <summary>
    /// After a hidden boot's local-store init + remembered-session attempt: if nobody got signed in (no
    /// «مرا به خاطر بسپار», expired/locked session…) show the normal sign-in window — there is nothing to sync
    /// without a user, so staying invisible would just look like the app failed to start.
    /// </summary>
    private static void RevealPendingStartupLogin(LoginWindow login)
    {
        if (!_hiddenStartPending || _exiting) return;
        _hiddenStartPending = false;
        if (ReferenceEquals(Desktop?.MainWindow, login)) RestoreWindow(login);
    }
}
