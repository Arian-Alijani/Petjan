using Animora.Contracts.Cheques;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Sales;

namespace Animora.Desktop.Services;

/// <summary>Default <see cref="IChequePrompt"/>: shows <see cref="ChequePromptViewModel"/> as a modal and awaits its outcome.</summary>
public sealed class ChequePrompt(
    IChequesService cheques,
    IToastService toast,
    IDialogService dialogs,
    IDataChangeNotifier changes) : IChequePrompt
{
    public Task<bool> PromptAsync(InvoiceDetail invoice, long amount)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // A cheque can only be registered against a real owner; walk-in (guest) invoices have none.
        if (string.IsNullOrWhiteSpace(invoice.OwnerId))
        {
            toast.Show("چک برای مشتری گذری در بخش چک‌ها ثبت نمی‌شود؛ ابتدا مالک فاکتور را انتخاب کنید", Tone.Warn);
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reference = string.IsNullOrWhiteSpace(invoice.ReceiptNo) ? invoice.Id : invoice.ReceiptNo;
        dialogs.ShowModal(
            new ChequePromptViewModel(cheques, toast, changes, invoice.OwnerId, invoice.OwnerName, reference, amount,
                registered => completion.TrySetResult(registered)),
            640);
        return completion.Task;
    }
}
