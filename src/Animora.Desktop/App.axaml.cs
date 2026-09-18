using Animora.Contracts.Appointments;
using Animora.Contracts.Boarding;
using Animora.Contracts.Cashier;
using Animora.Contracts.Cheques;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Inventory;
using Animora.Contracts.Patients;
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
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Patients;
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

        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<TopbarViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
