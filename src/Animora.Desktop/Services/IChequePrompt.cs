using Animora.Contracts.Sales;

namespace Animora.Desktop.Services;

/// <summary>
/// The «ثبت چک در بخش چک‌ها؟» step of a cheque tender: asks whether the cheque taken for an invoice must
/// be registered in the Cheques module and, if so, collects the cheque data and registers an inbound
/// cheque for the invoice's owner (note-linked to the invoice). Resolves once the user has finished
/// (<c>true</c> = a cheque was registered, <c>false</c> = skipped / not applicable).
/// </summary>
public interface IChequePrompt
{
    Task<bool> PromptAsync(InvoiceDetail invoice, long amount);
}
