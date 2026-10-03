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
/// an optional warning banner when the invoice was never transferred, and a
/// confirm that ends the stay («تخلیه‌شده»).
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
        ShowTransferWarning = !stay.Transferred;
    }

    public string Title { get; }
    public string RoomText { get; }

    /// <summary>Reference shows the warning banner only while the invoice is not yet transferred.</summary>
    public bool ShowTransferWarning { get; }

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
        _toast.Show($"«{_stay.PetName}» تخلیه شد — اتاق {RoomText} آزاد است", Tone.Ok);
        _onCheckedOut?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
