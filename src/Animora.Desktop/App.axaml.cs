using Animora.Application;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Finance.Cheques;
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
using Animora.Application.UseCases.History;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Search;
using Animora.Application.UseCases.Profile;
using Animora.Client.Services.Boarding;
using Animora.Client.Services.Cashier;
using Animora.Client.Services.Inventory;
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
using Animora.Contracts.History;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Inventory;
using Animora.Contracts.Patients;
using Animora.Contracts.Profile;
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
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.History;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Login;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Profile;
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
using Animora.Sync.Abstractions;
using Animora.Sync.Custom;
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

    public override void Initialize()
    {
        Diagnostics.AppTrace.Milestone("App.Initialize (load XAML)");
        // AvaloniaXamlLoader.Load(this) throws XamlLoadException ("No precompiled XAML found …") when
        // the build did not embed the compiled XAML resource. That is a broken-build condition; the
        // exception propagates to Program.Main, which turns it into a clear, actionable message. We
        // only annotate the trace here so the log shows exactly where startup stopped.
        AvaloniaXamlLoader.Load(this);
        Diagnostics.AppTrace.Milestone("App.Initialize done");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Diagnostics.AppTrace.Milestone("framework init completed");
        Services = ConfigureServices();
        Diagnostics.AppTrace.Milestone("DI container built", Program.StartupTimer.Elapsed);

        // B05c: process and dispatcher exception hooks are the first runtime behavior installed.
        var exceptionHandler = Services.GetRequiredService<GlobalExceptionHandler>();
        exceptionHandler.RegisterProcessHooks();
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = Services.GetRequiredService<GlobalExceptionHandler>().HandleUiException(args.Exception);
        };

        // Convention-based ViewModel → View resolution is a single app-wide
        // IDataTemplate. It is resolved from DI (it owns the ViewCache) and
        // added here rather than declared in App.axaml, because a
        // XAML-instantiated template cannot receive the injected dependency.
        // It must be in place before the shell window builds.
        DataTemplates.Add(Services.GetRequiredService<ViewLocator>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray model: the app must outlive its windows (a hidden shell / Windows auto-start boot has no visible
            // window at all), so it only exits through desktop.Shutdown() — the tray «خروج» item, closing the
            // sign-in window, or the OS shutting down. See App.Tray.cs.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _hiddenStartPending = Program.IsStartupLaunch;

            // Startup gate: show the sign-in window first. Once credentials are
            // accepted, swap in the main shell (resolved lazily so no page
            // is built until the user is in). The shell then owns the app
            // lifetime and the login window is closed.
            var login = CreateLoginWindow(desktop);
            desktop.MainWindow = login;
            Diagnostics.AppTrace.Milestone("login window set", Program.StartupTimer.Elapsed);

            // Local store startup runs only after the login window is up so the first frame is never
            // blocked (PERFORMANCE §4); the form stays disabled until it completes.

            // F-02: on application exit dispose the root provider so every IDisposable/IAsyncDisposable
            // singleton (FinanceProjectionWorker, MaintenanceScheduler, …) shuts down cleanly — background
            // loops stop and the final SQLite/WAL checkpoint runs instead of being cut off by process kill.
            // ShutdownRequested is a synchronous event; blocking on DisposeAsync here is safe (the app is
            // already tearing down and the work being drained is background, not the UI thread's own work).
            desktop.ShutdownRequested += (_, _) =>
            {
                _exiting = true;
                switch (Services)
                {
                    case IAsyncDisposable asyncDisposable:
                        asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool _returningToLogin;
    // Tray/startup lifecycle state (see App.Tray.cs).
    private static bool _hiddenStartPending;
    private static bool _exiting;

    private LoginWindow CreateLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var login = Services.GetRequiredService<LoginWindow>();
        if (Program.TestProfile is { } profile) login.Title += profile.WindowSuffix;

        // Windows auto-start boot (--startup): keep the sign-in window invisible while the store opens and a
        // remembered session is resumed; it is revealed only if the user actually has to sign in (OnLoginOpened).
        // Consumed once so a later sign-out returns to a normal, visible sign-in window.
        var startHidden = _hiddenStartPending;
        if (startHidden) HideForStartup(login);

        // Closing the sign-in window (X) with nobody signed in quits the app — with OnExplicitShutdown nothing
        // else would. The success / sign-out transitions swap desktop.MainWindow BEFORE closing this window.
        login.Closed += (_, _) =>
        {
            if (!_exiting && ReferenceEquals(desktop.MainWindow, login)) desktop.Shutdown();
        };

        if (login.DataContext is LoginViewModel vm)
        {
            async void OnSucceeded(object? sender, EventArgs args)
            {
                try
                {
                    var shell = Services.GetRequiredService<MainWindow>();
                    if (Program.TestProfile is { } shellProfile) shell.Title += shellProfile.WindowSuffix;
                    Services.GetRequiredService<ProfilePageViewModel>().SignedOut += OnProfileSignedOut;
                    desktop.MainWindow = shell;
                    // Auto-started with Windows and signed in via «مرا به خاطر بسپار»: stay in the tray (shell is
                    // built and syncing, just not shown). The tray icon / second launch opens it.
                    // Only while the hidden boot is still pending: once the sign-in form was revealed the user
                    // signed in by hand and expects to see the shell.
                    if (startHidden && _hiddenStartPending) _hiddenStartPending = false;
                    else shell.Show();
                    vm.LoginSucceeded -= OnSucceeded;
                    login.Close();
                    await Services.GetRequiredService<LocalSyncStatusService>().RefreshAsync();
                    Diagnostics.AppTrace.Milestone("shell shown");
                }
                catch (Exception ex)
                {
                    Diagnostics.AppTrace.Error("shell transition failed", ex, "auth");
                    vm.ErrorMessage = "باز کردن محیط کار انجام نشد؛ دوباره تلاش کنید.";
                }
            }
            vm.LoginSucceeded += OnSucceeded;
            login.Closed += (_, _) => vm.LoginSucceeded -= OnSucceeded;
        }
        login.Opened += OnLoginOpened;
        return login;
    }

    // A fresh composition also discards every singleton page, view cache, navigation history and dialog host.
    // Reusing only MainWindow would leak the previous clinic's in-memory rows into the next user's shell.
    private async void OnProfileSignedOut(object? sender, EventArgs args)
    {
        if (_returningToLogin || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        _returningToLogin = true;
        if (sender is ProfilePageViewModel profile) profile.SignedOut -= OnProfileSignedOut;
        var shell = desktop.MainWindow;
        try
        {
            // An explicit sign-out must cancel «مرا به خاطر بسپار»: the next launch should land on the sign-in
            // form, not silently resume. Clear the flag on the still-live provider before it is disposed below.
            try { await Services.GetRequiredService<Animora.Contracts.Device.IDevicePreferencesService>().SetRememberMeAsync(false); }
            catch (Exception ex) { Diagnostics.AppTrace.Warn($"clearing 'remember me' on sign-out failed: {ex.Message}", "auth"); }

            shell?.Hide();
            if (Services.GetService<INavigationService>()?.CurrentViewModel is { } leaving)
            {
                leaving.IsCurrentPage = false;
                leaving.OnNavigatedFrom();
            }
            Services.GetRequiredService<IDialogService>().Close();
            var previous = Services;
            DataTemplates.Remove(previous.GetRequiredService<ViewLocator>());
            // Drain old workers and dispose page subscriptions before building any new tenant-bound service.
            await Task.Run(async () =>
            {
                if (previous is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                else if (previous is IDisposable disposable) disposable.Dispose();
            });
            Services = ConfigureServices();
            // UI-03: the previous provider's GlobalExceptionHandler was disposed above (which unsubscribed its process-global
            // hooks), so this registers exactly one live set; the Dispatcher.UnhandledException lambda from startup is
            // subscribed once and always resolves the CURRENT handler, so it must not be re-added here.
            Services.GetRequiredService<GlobalExceptionHandler>().RegisterProcessHooks();
            DataTemplates.Add(Services.GetRequiredService<ViewLocator>());
            var login = CreateLoginWindow(desktop);
            desktop.MainWindow = login;
            login.Show();
            // The shell normally hides instead of closing (close-to-tray); sign-out really disposes it.
            if (shell is MainWindow mainShell) mainShell.CloseForReal();
            else shell?.Close();
        }
        catch (Exception ex)
        {
            Diagnostics.AppTrace.Error("return to login failed; no signed-out shell is retained", ex, "auth");
            desktop.Shutdown(GlobalExceptionHandler.FatalExitCode);
        }
        finally { _returningToLogin = false; }
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
            Diagnostics.AppTrace.Milestone($"local store ready={ready}", Program.StartupTimer.Elapsed);
            if (ready) await HydrateClinicTimeZoneAsync(); // INF-05: pre-hydrate so the sync zone getter never queries on the UI thread
            Services.GetRequiredService<MaintenanceScheduler>().Start();
            Services.GetRequiredService<Animora.Client.Services.Notifications.NotificationService>().Start(); // publishes due scheduled messages
            Services.GetRequiredService<SyncSchedulerHostedService>().Start(); // dormant unless a server is configured
            Services.GetService<LiveAccessRefreshService>()?.Start(); // active-session access changes, independent of state-sync/backoff
            Services.GetRequiredService<SyncChangeBridge>().Start(); // D14: a completed pass refreshes the open page
            Services.GetRequiredService<NotificationBridge>().Start(); // messages arrive without sync: refresh badges + toast
            await Dispatcher.UIThread.InvokeAsync(() => viewModel.InitializeAsync(ready));
            // Hidden --startup boot that did NOT auto-sign-in: the user must authenticate, so show the form now.
            await Dispatcher.UIThread.InvokeAsync(() => RevealPendingStartupLogin(login));
        }
        catch (Exception ex)
        {
            // Never let a startup failure escape the event boundary; keep sign-in disabled.
            Diagnostics.AppTrace.Error("local store initialization failed; sign-in stays disabled", ex, "boot");
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => viewModel.InitializeAsync(storeReady: false));
                await Dispatcher.UIThread.InvokeAsync(() => RevealPendingStartupLogin(login));
            }
            catch (Exception)
            {
                // The window is gone; nothing left to report to.
            }
        }
    }

    private static async Task HydrateClinicTimeZoneAsync()
    {
        try
        {
            await Services.GetRequiredService<Animora.Application.Ports.IClinicTimeZoneProvider>()
                .GetClinicTimeZoneAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Non-fatal: the provider serves the default zone and re-hydrates in the background on first use.
            Diagnostics.AppTrace.Milestone($"clinic time zone pre-hydrate skipped: {ex.GetType().Name}", Program.StartupTimer.Elapsed);
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
        // SQL-01: the shipped composition resolves at-rest encryption (on whenever the cipher bundle is loaded,
        // required for an already-encrypted store, loud warning otherwise); test profiles stay plaintext on purpose.
        services.AddAnimoraLocalInfrastructure(testProfile is null
            ? Animora.Persistence.Sqlite.Options.LocalStoreEncryptionDefaults.ApplyShipped
            : testProfile.ConfigureStore);
        if (testProfile is not null)
        {
            services.AddSingleton(testProfile);
            // OS secrets must be isolated too: sessions/signers otherwise share the clinic's keys.
            services.AddSingleton<Animora.Infrastructure.Local.Security.ISecretStore>(sp =>
                testProfile.CreateSecretStore(
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Animora.Persistence.Sqlite.Options.LocalStoreOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Animora.Infrastructure.Local.Security.FileFallbackSecretStore>>()));
        }
        services.AddAnimoraApplication(builder => builder.AddIdentityUseCases().AddPatientsUseCases().AddReceptionUseCases().AddSchedulingUseCases().AddBoardingUseCases().AddVisitsUseCases().AddVisitsLocalUseCases().AddLabTestCatalogUseCases().AddVaccinationUseCases().AddInventoryUseCases().AddPurchasingUseCases().AddSalesUseCases().AddCashierUseCases().AddHistoryUseCases().AddDashboardUseCases().AddFinanceUseCases().AddChequeUseCases().AddSearchUseCases().AddProfileUseCases());

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
        services.AddSingleton<IPrintService, PrintService>();
        services.AddSingleton<IFileLauncher, FileLauncher>();

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
        // Keeps the local session's idle-timeout alive from real user input, so the shell never drops to the
        // fail-closed read-only banner (empty sidebar) while the operator is actively working — the session used
        // to lock 30 min after sign-in because ISessionService.TouchAsync was never called on activity.
        services.AddSingleton<SessionActivityService>();
        services.AddSingleton<IDataChangeNotifier, DataChangeNotifier>();

        // Device-level (pre-login) preferences: «مرا بخاطر بسپار» (auto-restore the last session on next launch)
        // and «اجرای خودکار هنگام روشن شدن ویندوز». Both are stored in device-prefs.json beside the DB so they
        // can be read BEFORE any user signs in, and the Windows-startup toggle is the OS Run-key seam.
        services.AddSingleton<Animora.Contracts.Device.IDevicePreferencesService, Animora.Client.Services.Device.DevicePreferencesFileStore>();
        services.AddSingleton<Animora.Contracts.Device.IStartupRegistration, Animora.Client.Services.Device.WindowsStartupRegistration>();

        // D14: a background sync pass writes straight into the local store, so the open page must be told — this
        // bridge re-announces a completed pass on the same DataChangeNotifier channel the local write path uses.
        services.AddSingleton<SyncChangeBridge>();

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
        // C01 use cases (PatientsService); no mock remains. Singleton: it resolves the scoped
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
        services.AddTransient<VisitDetailViewModel>();

        // Unified History: one seam that turns any history-feed row (main page + the
        // Vaccination/Boarding/Sales tabs) into the same record screen the rest of the
        // app opens — invoice sheet, pet/owner sheet, or the destination page.
        services.AddSingleton<IHistoryRecordOpener, HistoryRecordOpener>();

        // Cheque tender → «ثبت چک در بخش چک‌ها؟» prompt used by the payment modal.
        services.AddSingleton<IChequePrompt, ChequePrompt>();

        // C02: real triage gate, backed by the local store.
        services.AddSingleton<IVisitsTriageService, VisitsTriageService>();
        services.AddSingleton<VisitsTriagePageViewModel>();

        // C03: real vaccination queries and journaled commands; scope-per-call.
        services.AddSingleton<IVaccinationService, VaccinationService>();
        services.AddSingleton<VaccinationPageViewModel>();

        // Phase 13/14: Appointments calendar + booking panel. Real query/booking services over the local backend
        // (scope-per-call, stateless); the calendar re-reads after the panel's writes via the data-change notifier.
        // Clinic-editable appointment preferences (custom types, calendar mode, SMS) — local JSON, singleton cache.
        // A17: settings are read/written through authorized Application use cases over this durable store port.
        services.AddSingleton<Animora.Application.Ports.IAppointmentSettingsStore, AppointmentSettingsFileStore>();
        services.AddSingleton<IAppointmentSettingsService, AppointmentSettingsService>();
        // Visits → تنظیمات: clinic-editable laboratory test types — local JSON beside the DB, same pattern as the appointment settings.
        services.AddSingleton<Animora.Application.Ports.ILabTestCatalogStore, LabTestCatalogFileStore>();
        services.AddSingleton<ILabTestSettingsService, LabTestSettingsService>();
        // A24: clinic-local today/now for the appointments UI (clock + clinic timezone), never device time.
        services.AddSingleton<IClinicClock, ClinicClockService>();
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

        // Phase 17: Inventory product list & settings. Real InventoryQueryService over the local backend
        // (scope-per-call, stateless — the singleton holds no data).
        services.AddSingleton<IInventoryQueryService, InventoryQueryService>();
        services.AddSingleton<Animora.Contracts.Purchasing.IPurchasingService, Animora.Client.Services.Purchasing.PurchasingService>();
        services.AddSingleton<InventoryPageViewModel>();

        // Cheques: real service over the cheque use cases (scope-per-call dispatcher; state machine stays in the domain).
        services.AddSingleton<Animora.Contracts.Cheques.IChequesService, Animora.Client.Services.Cheques.ChequesService>();
        services.AddSingleton<Animora.Desktop.Pages.Cheques.ChequesPageViewModel>();

        // C07: scope-per-call Sales service over the local journaled dispatcher.
        services.AddSingleton<ISalesQueryService, SalesQueryService>();
        services.AddSingleton<IServiceRateService, ServiceRateService>();
        services.AddSingleton<SalePageViewModel>();

        // Phase 22: Cashier (invoice & payment management). Real CashierService over the local backend
        // (scope-per-call, stateless — pay/void/refund/edit persist through the dispatcher; the singleton holds no data).
        services.AddSingleton<ICashierService, CashierService>();
        services.AddSingleton<CashierPageViewModel>();

        // Phase 27: History — unified event ledger. Real HistoryQueryService over the local backend
        // (read-only, scope-per-call, stateless).
        services.AddSingleton<IHistoryQueryService, Animora.Client.Services.History.HistoryQueryService>();
        services.AddSingleton<HistoryPageViewModel>();

        // «آمار»: stateless dispatcher adapter (scope-per-call) + the singleton page and its four section view-models.
        services.AddSingleton<Animora.Contracts.Statistics.IStatisticsQueryService, Animora.Client.Services.Statistics.StatisticsQueryService>();
        services.AddSingleton<Animora.Desktop.Pages.Statistics.StatisticsOverviewViewModel>();
        services.AddSingleton<Animora.Desktop.Pages.Statistics.StatisticsClientsViewModel>();
        services.AddSingleton<Animora.Desktop.Pages.Statistics.StatisticsSalesViewModel>();
        services.AddSingleton<Animora.Desktop.Pages.Statistics.StatisticsExpensesViewModel>();
        services.AddSingleton<Animora.Desktop.Pages.Statistics.StatisticsPageViewModel>();

        // Phase 28: Profile & Settings screen. Real UserProfileService over the local backend
        // (scope-per-call, stateless — edits persist through the use cases; the singleton holds no data).
        services.AddSingleton<IUserProfileService, Animora.Client.Services.Profile.UserProfileService>();
        services.AddSingleton<ProfilePageViewModel>();

        // Notifications & messages: local, offline-first feed (no sync needed). One singleton is both the concrete
        // type (the composition root starts its minute due-sweep, DI disposes it) and the Contracts interface.
        services.AddSingleton<Animora.Client.Services.Notifications.NotificationService>();
        services.AddSingleton<Animora.Contracts.Notifications.INotificationService>(sp =>
            sp.GetRequiredService<Animora.Client.Services.Notifications.NotificationService>());
        services.AddSingleton<Animora.Desktop.Pages.Notifications.NotificationsPageViewModel>();
        services.AddSingleton<NotificationBridge>(); // feed change -> DataDomain.Notifications + toast for a new message

        // Local offline sync status. E04 swaps engine internals behind the same contract.
        services.AddAnimoraSyncEngine();
        // Real-server wiring: when a sync server base address is provided the desktop talks to the
        // live ASP.NET Core server (online auth/entitlement/device + command/state sync). The address
        // is read from the ANIMORA_SERVER_URL environment variable so the same build can target any
        // server; the publish/build script bakes the production default (http://188.212.99.234:5000).
        // A blank/invalid value leaves the engine offline-only (unchanged behavior) — see ServerEndpointOptions.
        services.AddAnimoraHttpSyncEngine(ConfigureServerEndpoint);
        services.AddAnimoraSyncOrchestration();
        services.AddAnimoraOnlineIdentity(); // G06: online auth/entitlement/device — no-op unless the server endpoint is configured
        services.AddSingleton<LocalSyncStatusService>();
        services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<LocalSyncStatusService>());
        services.AddSingleton<ISyncPendingLookup, LocalSyncPendingLookup>(); // SYN-02: «در صف سرور» marks in histories

        // Settings → "پاک‌سازی کامل و همگام‌سازی دوباره": wipe the whole local replica and re-pull a full server snapshot.
        services.AddSingleton<ILocalDataResetService, LocalDataResetService>();

        // Login-screen server reachability indicator: a cheap GET /health/live probe so the user can
        // tell a server/internet outage apart from a wrong password.
        services.AddSingleton<IServerConnectivityService, ServerConnectivityService>();

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

    /// <summary>
    /// Default sync-server base address baked into release/portable builds so the app connects to the
    /// real server out of the box. Overridable per run with the <c>ANIMORA_SERVER_URL</c> environment
    /// variable (the publish script sets it in the launcher). Set this to an empty string to ship an
    /// offline-only build.
    /// </summary>
    private const string DefaultServerBaseAddress = "http://188.212.99.234:5000";

    /// <summary>
    /// Default tenant (clinic) code for the online <c>/auth/login</c> that establishes the server session.
    /// Deployment-specific (each clinic is provisioned on the server with its own code), so this is blank by
    /// default and supplied per run via the <c>ANIMORA_TENANT_CODE</c> environment variable (the publish script
    /// sets it in the launcher). Blank ⇒ the desktop runs on the local session only — writes that reach the
    /// server are rejected (<c>SESSION_EXPIRED</c> / <c>ENTITLEMENT_REQUIRED</c>) until a tenant code is set.
    /// </summary>
    private const string DefaultTenantCode = "";

    /// <summary>
    /// Binds <see cref="ServerEndpointOptions"/> for the live server. Resolution order:
    /// <list type="number">
    ///   <item>the <c>ANIMORA_SERVER_URL</c> environment variable (per-run override), else</item>
    ///   <item>the compiled-in <see cref="DefaultServerBaseAddress"/> (production default).</item>
    /// </list>
    /// A blank result leaves <see cref="ServerEndpointOptions.BaseAddress"/> null, which keeps the
    /// desktop offline-only (the HTTP sync engine is not wired). Any explicit timeout override can be
    /// supplied through <c>ANIMORA_SERVER_TIMEOUT_SECONDS</c>.
    /// </summary>
    private static void ConfigureServerEndpoint(ServerEndpointOptions options)
    {
        var fromEnv = Environment.GetEnvironmentVariable("ANIMORA_SERVER_URL");
        var baseAddress = !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv.Trim() : DefaultServerBaseAddress;
        if (!string.IsNullOrWhiteSpace(baseAddress))
            options.BaseAddress = baseAddress.Trim();

        // Tenant (clinic) code for the online /auth/login that establishes the SERVER session. Without it the desktop
        // only holds a LOCAL session, so every server write returns 401 SESSION_EXPIRED / is rejected with ENTITLEMENT_REQUIRED.
        var tenantEnv = Environment.GetEnvironmentVariable("ANIMORA_TENANT_CODE");
        var tenantCode = !string.IsNullOrWhiteSpace(tenantEnv) ? tenantEnv.Trim() : DefaultTenantCode;
        if (!string.IsNullOrWhiteSpace(tenantCode))
            options.TenantCode = tenantCode.Trim();

        var timeoutRaw = Environment.GetEnvironmentVariable("ANIMORA_SERVER_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(timeoutRaw)
            && int.TryParse(timeoutRaw, out var seconds)
            && seconds is > 0 and <= 600)
        {
            options.RequestTimeout = TimeSpan.FromSeconds(seconds);
        }
    }
}
