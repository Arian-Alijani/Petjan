using Animora.Desktop.Diagnostics;
using Avalonia;

namespace Animora.Desktop;

internal static class Program
{
    // Avalonia entry point. Keep initialization-only code here; do not use
    // any Avalonia types before AppMain (visual tree not yet built).
    internal static readonly System.Diagnostics.Stopwatch StartupTimer = new();

    internal static LocalTestProfile? TestProfile { get; private set; }

    /// <summary>
    /// True when Windows launched the app at sign-in (<c>--startup</c>, registered by the «اجرای خودکار» setting).
    /// Such a boot comes up hidden in the system tray and only shows a window if the user has to sign in.
    /// </summary>
    internal static bool IsStartupLaunch { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Diagnostics FIRST: if ANIMORA_NET_LOG=1 the launcher wants a full server-traffic trace.
        // A WinExe has no console, so attach to the launcher's terminal (or allocate one) before any
        // Console output is produced; otherwise the NetworkLoggingHandler trace would go nowhere.
        Diagnostics.ConsoleTrace.EnsureAttachedIfRequested();

        // Open the app's own trace (lifecycle / clicks / navigation / errors) + its log file. This is
        // always on (its events are cheap and invaluable for support); it is independent of the
        // network trace above. Any throw from here on is captured to the log and surfaced clearly.
        AppTrace.Initialize();
        AppTrace.Milestone("process start");
        AppTrace.Info($"args: {(args.Length == 0 ? "(none)" : string.Join(' ', args))}", "boot");

        // Parse before building DI or opening storage. Invalid test flags must never fall back to
        // the normal clinic database.
        try
        {
            // Developer launches (including Visual Studio) reuse an isolated durable test profile.
            // Release keeps the clinic default; --clinic explicitly selects it in Debug too.
#if DEBUG
            TestProfile = LocalTestProfile.Parse(args, useDefaultTestProfile: true);
#else
            TestProfile = LocalTestProfile.Parse(args);
#endif
        }
        catch (ArgumentException ex)
        {
            AppTrace.Error("invalid command-line flags", ex, "boot");
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 2;
            return;
        }
        if (TestProfile is { } profile)
        {
            AppTrace.Info($"local test profile [{profile.Name}] at {profile.DataDirectory}", "boot");
            Console.WriteLine($"LOCAL TEST [{profile.Name}]: {profile.DataDirectory}");
            Console.WriteLine("Create your own Manager account on first run; normal clinic data is not used.");
        }
        IsStartupLaunch = args.Contains(Animora.Client.Services.Device.WindowsStartupRegistration.StartupFlag, StringComparer.Ordinal);

        // Tray-resident app: a second launch must not open a second process on the same store — it asks the
        // running instance to show its window and exits. One guard per storage profile.
        using var singleInstance = SingleInstanceGuard.TryAcquire(TestProfile?.Name ?? "clinic", out var isPrimary);
        if (!isPrimary)
        {
            AppTrace.Info("another instance is already running; asked it to show its window", "boot");
            return;
        }
        singleInstance?.StartListening(App.ShowMainWindow);
        StartupTimer.Start();

        try
        {
            AppTrace.Milestone("Avalonia startup");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppTrace.Milestone("Avalonia shutdown", StartupTimer.Elapsed);
        }
        catch (Avalonia.Markup.Xaml.XamlLoadException ex)
        {
            // The classic "No precompiled XAML found for Animora.Desktop.App" failure. This only
            // happens with a BROKEN BUILD (the Avalonia XAML compiler did not embed the compiled
            // resource into the assembly) — never a user/runtime fault. Turn the cryptic unhandled
            // stack trace into a clear, actionable message in the log, console AND a dialog.
            const string advice =
                "Animora could not start because its compiled UI resources are missing from the build.\n" +
                "This is a packaging/build problem, not a problem with your computer or the server.\n\n" +
                "Fix (on the machine that BUILDS the app):\n" +
                "  1. Delete every bin/ and obj/ folder (or run 'dotnet clean').\n" +
                "  2. Re-run the publish script (it now cleans automatically).\n" +
                "  3. Re-send the produced package.";
            AppTrace.Error("FATAL: compiled Avalonia XAML missing (broken build).\n" + advice, ex, "boot");
            FatalStartup.Report("Animora — build is incomplete", advice, ex);
            Environment.ExitCode = unchecked((int)0xE0434352); // CLR unhandled-exception code, for parity with before.
        }
        catch (Exception ex)
        {
            AppTrace.Error("FATAL: unhandled exception during startup", ex, "boot");
            FatalStartup.Report("Animora — startup failed",
                "Animora hit an unexpected error while starting. The details were written to the log file:\n" +
                (AppTrace.LogFilePath ?? "(console only)"), ex);
            Environment.ExitCode = unchecked((int)0xE0434352);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

        // Trace logging is a debug aid; enabling it in Release adds per-log
        // formatting/IO overhead to the startup and render hot paths
        // (PERFORMANCE.md §16: no heavy logging in hot paths). Keep it for
        // Debug only so Release startup stays minimal (§3).
#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}
