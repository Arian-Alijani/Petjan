using Animora.Application;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Vaccination;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Application.UseCases.History;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Search;
using Animora.Application.UseCases.Profile;
using Animora.Client.Services.Boarding;
using Animora.Client.Services.Cashier;
using Animora.Client.Services.Cheques;
using Animora.Client.Services.Finance;
using Animora.Client.Services.Inventory;
using Animora.Client.Services.Purchasing;
using Animora.Client.Services.Diagnostics;
using Animora.Client.Services.Identity;
using Animora.Client.Services.Patients;
using Animora.Client.Services.Reception;
using Animora.Client.Services.Sales;
using Animora.Client.Services.Appointments;
using Animora.Client.Services.Visits;
using Animora.Client.Services.Vaccination;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Appointments;
using Animora.Contracts.Boarding;
using Animora.Contracts.Cashier;
using Animora.Contracts.Cheques;
using Animora.Contracts.Finance;
using Animora.Contracts.History;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Inventory;
using Animora.Contracts.Patients;
using Animora.Contracts.Profile;
using Animora.Contracts.Purchasing;
using Animora.Contracts.Reception;
using Animora.Contracts.Sales;
using Animora.Contracts.Search;
using Animora.Contracts.Sync;
using Animora.Contracts.Visits;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Diagnostics;
using Animora.Client.Services.Sync;
using Animora.Desktop.Shell.Search;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Boarding;
using Animora.Desktop.Pages.Cashier;
using Animora.Desktop.Pages.Cheques;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.Finance;
using Animora.Desktop.Pages.History;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Login;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Profile;
using Animora.Desktop.Pages.Purchasing;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Pages.Reception;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Pages.Visits;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Animora.Infrastructure.Local;
using Animora.Infrastructure.Local.Startup;
using Animora.Infrastructure.Local.Workers;
using Animora.Persistence.Sqlite.Maintenance;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop;

public partial class App : global::Avalonia.Application
{
    // Composition root. Later phases resolve ViewModels/services from here.
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        // B05c: process and dispatcher exception hooks are the first runtime behavior installed.
        var exceptionHandler = Services.GetRequiredService<GlobalExceptionHandler>();
        exceptionHandler.RegisterProcessHooks();
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = exceptionHandler.HandleUiException(args.Exception);
        };

        // Convention-based ViewModel → View resolution is a single app-wide
        // IDataTemplate. It is resolved from DI (it owns the ViewCache) and
        // added here rather than declared in App.axaml, because a
        // XAML-instantiated template cannot receive the injected dependency.
        // It must be in place before the shell window builds.
        DataTemplates.Add(Services.GetRequiredService<ViewLocator>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Startup gate: show the sign-in window first. Once credentials are
            // accepted, swap in the main shell (resolved lazily so no page/mock
            // is built until the user is in). The shell then owns the app
            // lifetime and the login window is closed.
            var login = Services.GetRequiredService<LoginWindow>();
            if (Program.TestProfile is { } loginProfile)
                login.Title += loginProfile.WindowSuffix;

            if (login.DataContext is LoginViewModel loginViewModel)
            {
                loginViewModel.LoginSucceeded += async (_, _) =>
                {
                    var shell = Services.GetRequiredService<MainWindow>();
                    if (Program.TestProfile is { } shellProfile)
                        shell.Title += shellProfile.WindowSuffix;
                    desktop.MainWindow = shell;
                    shell.Show();
                    login.Close();
                    await Services.GetRequiredService<LocalSyncStatusService>().RefreshAsync();
                };
            }

            desktop.MainWindow = login;

            // Local store startup runs only after the login window is up so the first frame is never
            // blocked (PERFORMANCE §4); the form stays disabled until it completes.
            login.Opened += OnLoginOpened;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // async void only at the Avalonia lifecycle-event boundary; every exception is caught here.
    private static async void OnLoginOpened(object? sender, EventArgs e)
    {
        if (sender is not LoginWindow login)
        {
            return;
        }

        login.Opened -= OnLoginOpened;
        if (Environment.GetEnvironmentVariable("ANIMORA_BENCH") == "1")
            Console.WriteLine($"ANIMORA_BENCH first_frame_ms={Program.StartupTimer.Elapsed.TotalMilliseconds:F3}");
        if (login.DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        try
        {
            var ready = await InitializeLocalStoreAsync();
            Services.GetRequiredService<MaintenanceScheduler>().Start();
            await Dispatcher.UIThread.InvokeAsync(() => viewModel.InitializeAsync(ready));
        }
        catch (Exception)
        {
            // Never let a startup failure escape the event boundary; keep sign-in disabled.
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => viewModel.InitializeAsync(storeReady: false));
            }
            catch (Exception)
            {
                // The window is gone; nothing left to report to.
            }
        }
    }

    private static async Task<bool> InitializeLocalStoreAsync()
    {
        try
        {
            // ILocalStoreInitializer is scoped: keep the scope alive for the whole await. Run off the UI thread.
            return await Task.Run(async () =>
            {
                await using var scope = Services.CreateAsyncScope();
                var initializer = scope.ServiceProvider.GetRequiredService<ILocalStoreInitializer>();
                var result = await initializer.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                if (result.IsFailure) return false;

                // C01: the 8-step startup succeeded — release any ViewModel awaiting the readiness gate.
                Services.GetRequiredService<StoreReadinessGate>().MarkReady();
                Services.GetRequiredService<FinanceProjectionWorker>().Start();

                var handler = Services.GetRequiredService<GlobalExceptionHandler>();
                handler.Diagnostics = result.Value;
                var recovery = await Services.GetRequiredService<CrashRecoveryService>()
                    .RecoverIfRequiredAsync(CancellationToken.None).ConfigureAwait(false);
                return recovery.IsSuccess;
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        ConfigureServices(services, Program.TestProfile);
        return services.BuildServiceProvider();
    }

    /// <summary>The shared composition seam; registration only, with no store I/O.</summary>
    public static void ConfigureServices(IServiceCollection services) => ConfigureServices(services, null);

    /// <summary>Explicit test profile override; a null profile preserves the normal composition.</summary>
    public static void ConfigureServices(IServiceCollection services, LocalTestProfile? testProfile)
    {
        // B03c: real local store + application layer (identity use cases) behind the desktop seams.
        services.AddAnimoraLocalInfrastructure(testProfile is null ? null : testProfile.ConfigureStore);
        if (testProfile is not null)
        {
            services.AddSingleton(testProfile);
            // OS secrets must be isolated too: sessions/signers otherwise share the clinic's keys.
            services.AddSingleton<Animora.Infrastructure.Local.Security.ISecretStore>(sp =>
                testProfile.CreateSecretStore(
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Animora.Persistence.Sqlite.Options.LocalStoreOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Animora.Infrastructure.Local.Security.FileFallbackSecretStore>>()));
        }
        services.AddAnimoraApplication(builder => builder.AddIdentityUseCases().AddPatientsUseCases().AddReceptionUseCases().AddSchedulingUseCases().AddBoardingUseCases().AddVisitsUseCases().AddVaccinationUseCases().AddInventoryUseCases().AddPurchasingUseCases().AddSalesUseCases().AddCashierUseCases().AddChequeUseCases().AddFinanceUseCases().AddHistoryUseCases().AddDashboardUseCases().AddSearchUseCases().AddProfileUseCases());

        // View resolution: the reflection-free View-type registry is static (no
        // DI), but the per-ViewModel View cache and the locator that consumes it
        // are composed here and added to App.DataTemplates in
        // OnFrameworkInitializationCompleted. Singletons: one cache and one
        // locator for the whole app's lifetime.
        services.AddSingleton<ViewCache>();
        services.AddSingleton<ViewLocator>();

        // Phase 03: shared control-library services.
        services.AddSingleton<ToastService>();
        services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

        // B05c: presentation-neutral notifications are adapted to the existing toast/modal hosts.
        services.AddSingleton<IUserNotificationPort>(sp => new ToastNotificationAdapter(
            (text, severity) => sp.GetRequiredService<IToastService>().Show(text, ToTone(severity)),
            (text, action) => sp.GetRequiredService<IDialogService>()
                .ShowModal($"{text}\n{action.ActionCode}"),
            NotificationText.Resolve));
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddSingleton<CrashRecoveryService>();
        services.AddSingleton<DraftStateStore>();
        services.AddSingleton<MaintenanceScheduler>();
        // C01: the store readiness gate — one instance, both as the concrete type (marked ready by startup)
        // and behind the Contracts interface a ViewModel awaits before its first DB read.
        services.AddSingleton<StoreReadinessGate>();
        services.AddSingleton<IStoreReadinessGate>(sp => sp.GetRequiredService<StoreReadinessGate>());

        // Phase 05: identity seam + navigation + shell composition.
        services.AddSingleton<ICurrentUserContext, CurrentUserContextService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // B04c: the capability gate is ONE app-wide singleton — the concrete service (warmed by
        // AuthenticationService via DI) and the ICapabilityGate the shell ViewModels consume are the same
        // instance, so a sign-in/refresh/sign-out is seen by the sidebar/topbar immediately.
        services.AddSingleton<CapabilityGateService>();
        services.AddSingleton<ICapabilityGate>(sp => sp.GetRequiredService<CapabilityGateService>());

        // C10: Dashboard screen runs on the real local backend. The query service is a
        // stateless singleton that opens a fresh async scope per call over the authorized
        // query pipeline (SQLite); it carries its own tenant/user/role-scoped data cache
        // keyed by the source generation, so freshness is real (invalidated on any source
        // write, D19) rather than process-lifetime. The singleton ViewModel re-activates on
        // every navigation and refreshes on an activation-scoped timer (D04/D20/D37/D38),
        // so navigating back shows current state — no per-navigation reload is skipped.
        services.AddSingleton<IDashboardQueryService, Animora.Client.Services.Dashboard.DashboardQueryService>();
        services.AddSingleton<DashboardPageViewModel>();

        // C02: real reception queue; each call resolves a scoped dispatcher over the local store.
        services.AddSingleton<IReceptionQueueService, ReceptionQueueService>();
        services.AddSingleton<ReceptionPageViewModel>();

        // Phase 08 / C01: Patients & Owners directory now runs on the real local SQLite store through the
        // C01 use cases (PatientsService) instead of the in-memory mock. Singleton: it resolves the scoped
        // dispatcher + identity provider from a fresh async scope per operation, never capturing them.
        services.AddSingleton<IPatientsQueryService, PatientsService>();
        services.AddSingleton<PatientsPageViewModel>();

        // Phase 09: Patients & Owners drill-in detail screens. The reference
        // renders both records as centered "sheets" over the directory (not
        // pages), so the directory asks IRecordSheetPresenter to resolve +
        // load + present them. Transient ViewModels so each opened record is a
        // fresh, fully-loaded instance; the presenter itself is stateless.
        services.AddSingleton<IRecordSheetPresenter, RecordSheetPresenter>();
        services.AddTransient<OwnerSheetViewModel>();
        services.AddTransient<PetFileViewModel>();

        // C02: real triage gate, backed by the local store.
        services.AddSingleton<IVisitsTriageService, VisitsTriageService>();
        services.AddSingleton<VisitsTriagePageViewModel>();

        // C03: real vaccination queries and journaled commands; scope-per-call.
        services.AddSingleton<IVaccinationService, VaccinationService>();
        services.AddSingleton<VaccinationPageViewModel>();

        // Phase 13/14: Appointments calendar + booking panel. One shared
        // in-memory event store backs both mocks (the calendar sees events the
        // panel creates/edits), so it is a singleton — as are both services.
        services.AddSingleton<AppointmentsQueryService>();
        services.AddSingleton<IAppointmentsQueryService>(p => p.GetRequiredService<AppointmentsQueryService>());
        services.AddSingleton<IAppointmentBookingService, AppointmentBookingService>();
        services.AddSingleton<AppointmentsCalendarViewModel>();
        services.AddTransient<BookingPanelViewModel>();

        // C03: real encounter service over local dispatcher; keep the note buffer across navigations.
        services.AddSingleton<DebounceBuffer>();
        services.AddSingleton<IVisitEncounterService, VisitEncounterService>();
        services.AddTransient<VisitEncounterViewModel>();

        // C05: real boarding service creates a fresh scoped dispatcher for each call.
        services.AddSingleton<IBoardingQueryService, BoardingQueryService>();
        services.AddSingleton<BoardingPageViewModel>();

        // Phase 17: Inventory product list & settings. Stateless deterministic
        // mock, so a singleton suffices.
        services.AddSingleton<IInventoryQueryService, InventoryQueryService>();
        services.AddSingleton<InventoryPageViewModel>();

        // Phase 19: Purchase & suppliers screen. Stateless deterministic mock,
        // so a singleton suffices.
        services.AddSingleton<IPurchasingQueryService, PurchasingQueryService>();
        services.AddSingleton<PurchasingPageViewModel>();

        // C07: scope-per-call Sales service over the local journaled dispatcher.
        services.AddSingleton<ISalesQueryService, SalesQueryService>();
        services.AddSingleton<SalePageViewModel>();

        // Phase 22: Cashier (invoice & payment management). Stateful in-memory
        // mock (pay/void/refund/edit mutate invoices), so a singleton preserves
        // mutations while navigating.
        services.AddSingleton<ICashierService, CashierService>();
        services.AddSingleton<CashierPageViewModel>();

        // Phase 23: Cheques (treasury) list/board screen. Stateful in-memory mock
        // (row-menu status changes / deletes mutate the list), so a singleton.
        services.AddSingleton<IChequesQueryService, ChequesQueryService>();
        services.AddSingleton<ChequesPageViewModel>();

        // Phase 25: shared Finance workspace (Overview + Income tabs). Stateless
        // deterministic mock, so a singleton. One ViewModel backs both the
        // «هزینه‌ها» and «آمار و گزارش فروش» sidebar entries — the sidebar passes
        // a per-entry initial tab/range (mirrors the reference's two initFinancePage calls).
        services.AddSingleton<IFinanceQueryService, FinanceQueryService>();
        services.AddSingleton<FinancePageViewModel>();

        // Phase 27: History — unified event ledger. Self-contained synthetic
        // mock (a singleton is sufficient; its data is static).
        services.AddSingleton<IHistoryQueryService, Animora.Client.Services.History.HistoryQueryService>();
        services.AddSingleton<HistoryPageViewModel>();

        // Phase 28: Profile & Settings screen. The mock persists edits
        // in-memory only for the session (mirroring the reference's explicit
        // "settings live in memory only" design), so a singleton preserves
        // edits while navigating away and back.
        services.AddSingleton<IUserProfileService, Animora.Client.Services.Profile.UserProfileService>();
        services.AddSingleton<ProfilePageViewModel>();

        // Local offline sync status. E04 swaps engine internals behind the same contract.
        services.AddSingleton<LocalSyncStatusService>();
        services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<LocalSyncStatusService>());

        // Global command palette (Ctrl+K) search seam. Depends on the patients
        // directory so a query also returns live patient/owner matches. The
        // palette ViewModel is transient so each open starts fresh.
        services.AddSingleton<ISearchService, Animora.Client.Services.Search.SearchService>();
        services.AddTransient<CommandPaletteViewModel>();

        // Startup sign-in gate (B03c: real local authentication + first-run
        // owner setup). Transient so a fresh form is built each time the window
        // opens; the first-run VM is created on demand through a factory.
        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<Func<FirstRunViewModel>>(sp => () => sp.GetRequiredService<FirstRunViewModel>());
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();

        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<TopbarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

    }

    private static Tone ToTone(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Info => Tone.Info,
        NotificationSeverity.Warning => Tone.Warn,
        NotificationSeverity.Error or NotificationSeverity.NeedsAttention => Tone.Danger,
        _ => Tone.Info,
    };
}
