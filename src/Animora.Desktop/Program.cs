using Avalonia;

namespace Animora.Desktop;

internal static class Program
{
    internal static readonly System.Diagnostics.Stopwatch StartupWatch = System.Diagnostics.Stopwatch.StartNew();

    // Avalonia entry point. Keep initialization-only code here; do not use
    // any Avalonia types before AppMain (visual tree not yet built).
    [STAThread]
    public static void Main(string[] args)
    {
        _ = StartupWatch;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
