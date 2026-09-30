using Avalonia;

namespace Animora.Desktop;

internal static class Program
{
    // Avalonia entry point. Keep initialization-only code here; do not use
    // any Avalonia types before AppMain (visual tree not yet built).
    internal static readonly System.Diagnostics.Stopwatch StartupTimer = new();

    internal static LocalTestProfile? TestProfile { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
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
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 2;
            return;
        }
        if (TestProfile is { } profile)
        {
            Console.WriteLine($"LOCAL TEST [{profile.Name}]: {profile.DataDirectory}");
            Console.WriteLine("Create your own Manager account on first run; normal clinic data is not used.");
        }
        StartupTimer.Start();
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
