using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Animora.Desktop.Shell;

/// <summary>
/// Reflection-free ViewModel → View construction for the shell's
/// convention-based view resolution (<see cref="ViewLocator"/>).
///
/// The locator used to derive the view's name by string concatenation
/// (<c>fullName - "ViewModel" + "View"</c>) and instantiate it with
/// <c>Type.GetType</c> + <c>Activator.CreateInstance</c>. Both run on the
/// navigation hot path and both are reflection. This registry replaces them
/// with a compile-time <see cref="Type"/> → <c>Func&lt;Control&gt;</c> map
/// built once (via <see cref="BuildRegistry"/> in the static field initializer)
/// from generic registrations: a plain dictionary lookup plus a direct
/// constructor call — no runtime type resolution, no activator, no string
/// building.
///
/// A registration is also a compile-time-verified pair: renaming or moving a
/// View (or its ViewModel) breaks the build here instead of silently
/// rendering "View not found" at runtime. Adding a screen means adding one
/// <see cref="Register{TViewModel, TView}"/> line.
/// </summary>
public static class ViewFactory
{
    // Built exactly once, at type-load. Never mutated afterwards, so reads are
    // lock-free on the UI thread.
    private static readonly Dictionary<Type, Func<Control>> Factories = BuildRegistry();

    /// <summary>
    /// Creates the View registered for <paramref name="viewModelType"/>, or
    /// <see langword="null"/> when no View is registered for it (the locator
    /// then renders its "View not found" placeholder).
    /// </summary>
    public static Control? Create(Type viewModelType) =>
        Factories.TryGetValue(viewModelType, out var factory) ? factory() : null;

    // Generic registration keeps the map key/constructing both statically
    // typed: the ViewModel type is the key and the View type is new()'d
    // directly. The factory is a static lambda so it captures nothing.
    private static void Register<TViewModel, TView>(Dictionary<Type, Func<Control>> factories)
        where TView : Control, new() =>
        factories[typeof(TViewModel)] = static () => new TView();

    private static Dictionary<Type, Func<Control>> BuildRegistry()
    {
        // One entry per View/ViewModel pair (CONVENTIONS.md §2/§3). Grouped by
        // area to mirror the Pages/ folder layout.
        var factories = new Dictionary<Type, Func<Control>>(64);

        // Shell chrome.
        Register<global::Animora.Desktop.Shell.SidebarViewModel, global::Animora.Desktop.Shell.SidebarView>(factories);
        Register<global::Animora.Desktop.Shell.TopbarViewModel, global::Animora.Desktop.Shell.TopbarView>(factories);
        Register<global::Animora.Desktop.Shell.Search.CommandPaletteViewModel, global::Animora.Desktop.Shell.Search.CommandPaletteView>(factories);

        // Appointments.
        Register<global::Animora.Desktop.Pages.Appointments.AppointmentsCalendarViewModel, global::Animora.Desktop.Pages.Appointments.AppointmentsCalendarView>(factories);
        Register<global::Animora.Desktop.Pages.Appointments.BookingDetailViewModel, global::Animora.Desktop.Pages.Appointments.BookingDetailView>(factories);
        Register<global::Animora.Desktop.Pages.Appointments.BookingPanelViewModel, global::Animora.Desktop.Pages.Appointments.BookingPanelView>(factories);

        // Boarding.
        Register<global::Animora.Desktop.Pages.Boarding.BoardingPageViewModel, global::Animora.Desktop.Pages.Boarding.BoardingPageView>(factories);
        Register<global::Animora.Desktop.Pages.Boarding.CheckoutModalViewModel, global::Animora.Desktop.Pages.Boarding.CheckoutModalView>(factories);
        Register<global::Animora.Desktop.Pages.Boarding.NewStayModalViewModel, global::Animora.Desktop.Pages.Boarding.NewStayModalView>(factories);
        Register<global::Animora.Desktop.Pages.Boarding.StayDetailModalViewModel, global::Animora.Desktop.Pages.Boarding.StayDetailModalView>(factories);

        // Cashier.
        Register<global::Animora.Desktop.Pages.Cashier.CashierPageViewModel, global::Animora.Desktop.Pages.Cashier.CashierPageView>(factories);

        // Cheques.
        Register<global::Animora.Desktop.Pages.Cheques.ChequeDetailViewModel, global::Animora.Desktop.Pages.Cheques.ChequeDetailView>(factories);
        Register<global::Animora.Desktop.Pages.Cheques.ChequeDrawerViewModel, global::Animora.Desktop.Pages.Cheques.ChequeDrawerView>(factories);
        Register<global::Animora.Desktop.Pages.Cheques.ChequesPageViewModel, global::Animora.Desktop.Pages.Cheques.ChequesPageView>(factories);

        // Dashboard.
        Register<global::Animora.Desktop.Pages.Dashboard.DashboardPageViewModel, global::Animora.Desktop.Pages.Dashboard.DashboardPageView>(factories);

        // Finance.
        Register<global::Animora.Desktop.Pages.Finance.AddExpenseModalViewModel, global::Animora.Desktop.Pages.Finance.AddExpenseModalView>(factories);
        Register<global::Animora.Desktop.Pages.Finance.FinancePageViewModel, global::Animora.Desktop.Pages.Finance.FinancePageView>(factories);

        // History.
        Register<global::Animora.Desktop.Pages.History.HistoryPageViewModel, global::Animora.Desktop.Pages.History.HistoryPageView>(factories);

        // Inventory.
        Register<global::Animora.Desktop.Pages.Inventory.InventoryPageViewModel, global::Animora.Desktop.Pages.Inventory.InventoryPageView>(factories);
        Register<global::Animora.Desktop.Pages.Inventory.NewProductModalViewModel, global::Animora.Desktop.Pages.Inventory.NewProductModalView>(factories);
        Register<global::Animora.Desktop.Pages.Inventory.ProductModalViewModel, global::Animora.Desktop.Pages.Inventory.ProductModalView>(factories);
        Register<global::Animora.Desktop.Pages.Inventory.StockAdjustModalViewModel, global::Animora.Desktop.Pages.Inventory.StockAdjustModalView>(factories);

        // Login (first-run owner setup, shown pre-shell).
        Register<global::Animora.Desktop.Pages.Login.FirstRunViewModel, global::Animora.Desktop.Pages.Login.FirstRunView>(factories);

        // Patients & Owners (directory + record sheets + wizard/modals).
        Register<global::Animora.Desktop.Pages.Patients.AddPatientWizardViewModel, global::Animora.Desktop.Pages.Patients.AddPatientWizardView>(factories);
        Register<global::Animora.Desktop.Pages.Patients.AddPetModalViewModel, global::Animora.Desktop.Pages.Patients.AddPetModalView>(factories);
        Register<global::Animora.Desktop.Pages.Patients.OwnerSheetViewModel, global::Animora.Desktop.Pages.Patients.OwnerSheetView>(factories);
        Register<global::Animora.Desktop.Pages.Patients.PatientsPageViewModel, global::Animora.Desktop.Pages.Patients.PatientsPageView>(factories);
        Register<global::Animora.Desktop.Pages.Patients.PetFileViewModel, global::Animora.Desktop.Pages.Patients.PetFileView>(factories);
        Register<global::Animora.Desktop.Pages.Patients.PetFormViewModel, global::Animora.Desktop.Pages.Patients.PetFormView>(factories);

        // Profile.
        Register<global::Animora.Desktop.Pages.Profile.ProfilePageViewModel, global::Animora.Desktop.Pages.Profile.ProfilePageView>(factories);

        // Purchasing.
        Register<global::Animora.Desktop.Pages.Purchasing.PurchasingPageViewModel, global::Animora.Desktop.Pages.Purchasing.PurchasingPageView>(factories);

        // Reception.
        Register<global::Animora.Desktop.Pages.Reception.ReceptionPageViewModel, global::Animora.Desktop.Pages.Reception.ReceptionPageView>(factories);
        Register<global::Animora.Desktop.Pages.Reception.WalkInModalViewModel, global::Animora.Desktop.Pages.Reception.WalkInModalView>(factories);

        // Sales (POS) + its shared dialogs.
        Register<global::Animora.Desktop.Pages.Sales.CatalogModalViewModel, global::Animora.Desktop.Pages.Sales.CatalogModalView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.CustomItemModalViewModel, global::Animora.Desktop.Pages.Sales.CustomItemModalView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.InvoiceViewerViewModel, global::Animora.Desktop.Pages.Sales.InvoiceViewerView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.OwnerPickerModalViewModel, global::Animora.Desktop.Pages.Sales.OwnerPickerModalView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.PaymentModalViewModel, global::Animora.Desktop.Pages.Sales.PaymentModalView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.RefundModalViewModel, global::Animora.Desktop.Pages.Sales.RefundModalView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.SalePageViewModel, global::Animora.Desktop.Pages.Sales.SalePageView>(factories);
        Register<global::Animora.Desktop.Pages.Sales.Shared.ConfirmDialogViewModel, global::Animora.Desktop.Pages.Sales.Shared.ConfirmDialogView>(factories);

        // Vaccination.
        Register<global::Animora.Desktop.Pages.Vaccination.AddVaccineModalViewModel, global::Animora.Desktop.Pages.Vaccination.AddVaccineModalView>(factories);
        Register<global::Animora.Desktop.Pages.Vaccination.SmsComposeViewModel, global::Animora.Desktop.Pages.Vaccination.SmsComposeView>(factories);
        Register<global::Animora.Desktop.Pages.Vaccination.VaccinationPageViewModel, global::Animora.Desktop.Pages.Vaccination.VaccinationPageView>(factories);
        Register<global::Animora.Desktop.Pages.Vaccination.VaccinationRecordDetailViewModel, global::Animora.Desktop.Pages.Vaccination.VaccinationRecordDetailView>(factories);

        // Visits (triage gate + clinical-encounter wizard).
        Register<global::Animora.Desktop.Pages.Visits.VisitEncounterViewModel, global::Animora.Desktop.Pages.Visits.VisitEncounterView>(factories);
        Register<global::Animora.Desktop.Pages.Visits.VisitsTriagePageViewModel, global::Animora.Desktop.Pages.Visits.VisitsTriagePageView>(factories);

        return factories;
    }
}
