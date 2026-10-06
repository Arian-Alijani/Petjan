using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Backs the Boarding «تخلیه» modal (Animora-UI/assets/boarding.js
/// <c>openCheckoutModal</c>): a hint line naming the room that will be freed,
/// an info banner explaining that checkout hands the stay to the cashier, and a
/// confirm that ends the stay («تخلیه‌شده»). Checkout is the only action that
/// queues the stay (room + every consumed line) for invoicing.
/// </summary>
public sealed partial class CheckoutModalViewModel : ObservableObject
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onCheckedOut;

    private readonly BoardingStay _stay;

    public CheckoutModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        BoardingStay stay,
        Action? onCheckedOut = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _stay = stay;
        _onCheckedOut = onCheckedOut;

        RoomText = PersianNumberFormatter.ToPersianDigits(stay.Room);
        Title = $"پایان اقامت «{stay.PetName}»";
    }

    public string Title { get; }
    public string RoomText { get; }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        try
        {
            await _service.CheckoutAsync(_stay.Id);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, "تخلیه ناموفق بود؛ دوباره تلاش کنید"), Tone.Danger);
            return;
        }

        _dialogs.Close();
        // Truthful wording: a prepaid stay already paid its room, so it is closed directly unless extra items or
        // nights remain; only then does a second invoice wait at the cashier. A postpaid stay always goes there.
        var message = _stay.PaymentMode == BoardingPaymentMode.Prepaid
            ? $"«{_stay.PetName}» تخلیه شد — اتاق {RoomText} آزاد شد؛ فقط در صورت وجود اقلام یا شبِ اضافه، فاکتور آن در صندوق صادر می‌شود"
            : $"«{_stay.PetName}» تخلیه شد — اتاق {RoomText} آزاد و اقامت برای صدور فاکتور به صندوق منتقل شد";
        _toast.Show(message, Tone.Ok);
        _onCheckedOut?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
