using Avalonia;

namespace Animora.Desktop;

internal static class Program
{
    // Avalonia entry point. Keep initialization-only code here; do not use
    // any Avalonia types before AppMain (visual tree not yet built).
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
