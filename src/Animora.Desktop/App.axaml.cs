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
using Animora.Contracts.Visits;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Mocks;
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

            if (login.DataContext is LoginViewModel loginViewModel)
            {
                loginViewModel.LoginSucceeded += (_, _) =>
                {
                    var shell = Services.GetRequiredService<MainWindow>();
                    desktop.MainWindow = shell;
                    shell.Show();
                    login.Close();
                };
            }

            desktop.MainWindow = login;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

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

        // Phase 05: identity seam + navigation + shell composition.
        services.AddSingleton<ICurrentUserContext, MockCurrentUserContext>();
        services.AddSingleton<INavigationService, NavigationService>();

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

        // Phase 09: Patients & Owners drill-in detail screens. The reference
        // renders both records as centered "sheets" over the directory (not
        // pages), so the directory asks IRecordSheetPresenter to resolve +
        // load + present them. Transient ViewModels so each opened record is a
        // fresh, fully-loaded instance; the presenter itself is stateless.
        services.AddSingleton<IRecordSheetPresenter, RecordSheetPresenter>();
        services.AddTransient<OwnerSheetViewModel>();
        services.AddTransient<PetFileViewModel>();

        // Phase 10: Visits triage gate (doctor rail + entry-mode workspace).
        // Stateless deterministic mock, so a singleton suffices.
        services.AddSingleton<IVisitsTriageService, MockVisitsTriageService>();
        services.AddSingleton<VisitsTriagePageViewModel>();

        // Phase 12: Vaccination screen (due/overdue list + catalog + SMS
        // reminders). Stateful in-memory mock — its send/mark-done mutations
        // persist while navigating, so a singleton.
        services.AddSingleton<IVaccinationService, MockVaccinationService>();
        services.AddSingleton<VaccinationPageViewModel>();

        // Phase 13/14: Appointments calendar + booking panel. One shared
        // in-memory event store backs both mocks (the calendar sees events the
        // panel creates/edits), so it is a singleton — as are both services.
        services.AddSingleton<AppointmentStore>();
        services.AddSingleton<IAppointmentsQueryService, MockAppointmentsQueryService>();
        services.AddSingleton<IAppointmentBookingService, MockAppointmentBookingService>();
        services.AddSingleton<AppointmentsCalendarViewModel>();
        services.AddTransient<BookingPanelViewModel>();

        // Phase 11: clinical-encounter wizard. One shared encounter store backs
        // both Visits mocks (triage creates the draft, the wizard mutates it);
        // the wizard ViewModel is transient so each started visit loads fresh.
        services.AddSingleton<VisitEncounterStore>();
        services.AddSingleton<IVisitEncounterService, MockVisitEncounterService>();
        services.AddTransient<VisitEncounterViewModel>();

        // Phase 15: Boarding board/list screen. Stateless deterministic mock,
        // so a singleton suffices.
        services.AddSingleton<IBoardingQueryService, MockBoardingQueryService>();
        services.AddSingleton<BoardingPageViewModel>();

        // Phase 17: Inventory product list & settings. Stateless deterministic
        // mock, so a singleton suffices.
        services.AddSingleton<IInventoryQueryService, MockInventoryQueryService>();
        services.AddSingleton<InventoryPageViewModel>();

        // Phase 19: Purchase & suppliers screen. Stateless deterministic mock,
        // so a singleton suffices.
        services.AddSingleton<IPurchasingQueryService, MockPurchasingQueryService>();
        services.AddSingleton<PurchasingPageViewModel>();

        // Phase 20: Sale/POS invoice workspace. Stateful in-memory mock (build/
        // blank create invoices), so a singleton preserves mutations.
        services.AddSingleton<ISalesQueryService, MockSalesQueryService>();
        services.AddSingleton<SalePageViewModel>();

        // Phase 22: Cashier (invoice & payment management). Stateful in-memory
        // mock (pay/void/refund/edit mutate invoices), so a singleton preserves
        // mutations while navigating.
        services.AddSingleton<ICashierService, MockCashierService>();
        services.AddSingleton<CashierPageViewModel>();

        // Phase 23: Cheques (treasury) list/board screen. Stateful in-memory mock
        // (row-menu status changes / deletes mutate the list), so a singleton.
        services.AddSingleton<IChequesQueryService, MockChequesQueryService>();
        services.AddSingleton<ChequesPageViewModel>();

        // Phase 25: shared Finance workspace (Overview + Income tabs). Stateless
        // deterministic mock, so a singleton. One ViewModel backs both the
        // «هزینه‌ها» and «آمار و گزارش فروش» sidebar entries — the sidebar passes
        // a per-entry initial tab/range (mirrors the reference's two initFinancePage calls).
        services.AddSingleton<IFinanceQueryService, MockFinanceQueryService>();
        services.AddSingleton<FinancePageViewModel>();

        // Phase 27: History — unified event ledger. Self-contained synthetic
        // mock (a singleton is sufficient; its data is static).
        services.AddSingleton<IHistoryQueryService, MockHistoryQueryService>();
        services.AddSingleton<HistoryPageViewModel>();

        // Phase 28: Profile & Settings screen. The mock persists edits
        // in-memory only for the session (mirroring the reference's explicit
        // "settings live in memory only" design), so a singleton preserves
        // edits while navigating away and back.
        services.AddSingleton<IUserProfileService, MockUserProfileService>();
        services.AddSingleton<ProfilePageViewModel>();

        // Startup sign-in gate (front-end only for now; admin/admin demo
        // credentials, mock-authenticated in LoginViewModel). Transient so a
        // fresh form is built each time the window opens.
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();

        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<TopbarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
