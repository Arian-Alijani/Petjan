using Animora.Desktop.Shell;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop;

public partial class App : Application
{
    // Composition root. Later phases resolve ViewModels/services from here.
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Phase 01: only what an empty MainWindow needs.
        // Screen ViewModels and mock services are registered in later phases.
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
