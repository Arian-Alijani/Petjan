using System;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Boarding;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Pages.Visits;

namespace Animora.Desktop.Services;

/// <summary>
/// The one implementation of <see cref="IHistoryRecordOpener"/>: maps a feed entity tag to the unified
/// screen the rest of the app already uses. Invoice/Payment rows open the exact same
/// <see cref="InvoiceViewerViewModel"/> sheet opened from the صندوق page and the Owner sheet; Pet/Owner
/// rows open the record sheets; every other tag lands on its page. This is the single place that knows
/// the invoice-sheet ctor and the destination map, so the four history surfaces never duplicate it.
/// </summary>
public sealed class HistoryRecordOpener(
    ISalesQueryService sales,
    IToastService toast,
    IDialogService dialogs,
    IPrintService print,
    IChequePrompt chequePrompt,
    IRecordSheetPresenter sheets,
    INavigationService navigation) : IHistoryRecordOpener
{
    public Task OpenAsync(string entityType, string entityId, string? invoiceId = null, Action? onChanged = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return Task.CompletedTask;
        }

        switch (entityType)
        {
            case "Invoice":
                return OpenInvoice(entityId, onChanged);

            case "Payment":
                // The feed's EntityId is the payment id; the sheet needs the owning invoice id.
                return OpenInvoice(string.IsNullOrWhiteSpace(invoiceId) ? entityId : invoiceId, onChanged);

            case "Pet":
                return string.IsNullOrWhiteSpace(entityId) ? Task.CompletedTask : sheets.OpenPetSheetAsync(entityId);

            case "Owner":
                return string.IsNullOrWhiteSpace(entityId) ? Task.CompletedTask : sheets.OpenOwnerSheetAsync(entityId);

            case "Stay":
                navigation.NavigateTo<BoardingPageViewModel>();
                return Task.CompletedTask;

            case "Vaccination":
                navigation.NavigateTo<VaccinationPageViewModel>();
                return Task.CompletedTask;

            case "Visit":
                navigation.NavigateTo<VisitsTriagePageViewModel>();
                return Task.CompletedTask;

            case "Appointment":
                navigation.NavigateTo<AppointmentsCalendarViewModel>();
                return Task.CompletedTask;

            case "Cheque":
                navigation.NavigateTo<Pages.Cheques.ChequesPageViewModel>();
                return Task.CompletedTask;

            // «Expense»: صفحهٔ اختصاصی‌شان حذف شده (بازسازی خواهد شد)؛ تا آن زمان no-op.
            case "GoodsReceipt":
            case "StockMovement":
            case "Product":
                navigation.NavigateTo<InventoryPageViewModel>();
                return Task.CompletedTask;

            default:
                return Task.CompletedTask; // unknown tag → safe no-op
        }
    }

    private Task OpenInvoice(string invoiceId, Action? onChanged)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return Task.CompletedTask;
        }

        dialogs.ShowSheet(new InvoiceViewerViewModel(sales, toast, dialogs, print, chequePrompt, invoiceId, onChanged), 1040, 720);
        return Task.CompletedTask;
    }
}
