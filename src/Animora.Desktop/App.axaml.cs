using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Patients;
using Animora.Contracts.Reception;
using Animora.Desktop.Mocks;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Reception;
using Animora.Desktop.Services;
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

        // Phase 03: shared control-library services.
        services.AddSingleton<ToastService>();
        services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

        // Phase 05: identity seam + navigation + shell composition.
        services.AddSingleton<ICurrentUserContext, MockCurrentUserContext>();
        services.AddSingleton<INavigationService, NavigationService>();

        // A single shared placeholder instance backs every not-yet-built nav
        // item; the sidebar retargets it per click. Screen phases (06+)
        // register their own page ViewModels here.
        services.AddSingleton<PlaceholderPageViewModel>();

        // Phase 06: Dashboard screen — query service is stateless/pure mock
        // data, so a singleton is sufficient (no per-navigation reload needed
        // until a real backend adds freshness semantics).
        services.AddSingleton<IDashboardQueryService, MockDashboardQueryService>();
        services.AddSingleton<DashboardPageViewModel>();

        // Phase 07: stateful in-memory live queue. Singleton lifetime preserves
        // mutations while navigating between Dashboard and Reception.
        services.AddSingleton<IReceptionQueueService, MockReceptionQueueService>();
        services.AddSingleton<ReceptionPageViewModel>();

        // Phase 08: Patients & Owners directory. Stateful mock (Phase 09 adds
        // owner/pet create + edit persistence), so a singleton preserves
        // mutations while navigating between the list and the detail screens.
        services.AddSingleton<IPatientsQueryService, MockPatientsQueryService>();
        services.AddSingleton<PatientsPageViewModel>();

        // Phase 09: Patients & Owners drill-in detail screens. Transient so each
        // navigation resolves a fresh page whose state is loaded via the id
        // passed by INavigationService.NavigateTo<T>(configure).
        services.AddTransient<OwnerSheetViewModel>();
        services.AddTransient<PetFileViewModel>();

        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<TopbarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
