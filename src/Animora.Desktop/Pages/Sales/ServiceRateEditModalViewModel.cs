using System;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the «خدمت جدید» / «ویرایش خدمت» modal of the «نرخ خدمات» tab. Creating lets the user pick the
/// catalogue (clinic / boarding) and, for boarding, the billing basis; editing keeps both fixed and
/// additionally exposes the active flag.
/// </summary>
public sealed partial class ServiceRateEditModalViewModel : ObservableObject
{
    private readonly IServiceRateService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly ServiceRate? _existing;
    private readonly Action _onSaved;

    public ServiceRateEditModalViewModel(IServiceRateService service, IToastService toast, IDialogService dialogs,
        ServiceRate? existing, Action onSaved)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _existing = existing;
        _onSaved = onSaved;

        if (existing is null)
        {
            return;
        }

        _name = existing.Title;
        _priceText = PersianNumberFormatter.ToPersianDigits(existing.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _isBoarding = existing.Source == ServiceRateSource.Boarding;
        _billing = existing.Billing ?? ServiceRateBilling.PerUse;
        _isActive = existing.IsActive;
    }

    public bool IsCreate => _existing is null;

    public bool IsEdit => _existing is not null;

    public string HeadTitle => IsCreate ? "خدمت جدید" : "ویرایش خدمت";

    public string HeadSubtitle => IsCreate
        ? "نام و نرخ خدمتی را که در فروش، ویزیت یا پانسیون ارائه می‌شود ثبت کنید."
        : "تغییر نام و نرخ فقط روی فاکتورهای بعدی اثر می‌گذارد.";

    public string SubmitText => IsCreate ? "افزودن خدمت" : "ذخیرهٔ تغییرات";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClinic))]
    [NotifyPropertyChangedFor(nameof(ShowBilling))]
    private bool _isBoarding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerStay))]
    [NotifyPropertyChangedFor(nameof(IsPerDay))]
    [NotifyPropertyChangedFor(nameof(IsPerUse))]
    private ServiceRateBilling _billing = ServiceRateBilling.PerUse;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private bool _isSaving;

    public bool IsClinic => !IsBoarding;

    /// <summary>The billing basis is chosen only when creating a boarding service.</summary>
    public bool ShowBilling => IsCreate && IsBoarding;

    public bool IsPerStay => Billing == ServiceRateBilling.PerStay;

    public bool IsPerDay => Billing == ServiceRateBilling.PerDay;

    public bool IsPerUse => Billing == ServiceRateBilling.PerUse;

    [RelayCommand]
    private void SetSource(string? key)
    {
        if (IsEdit)
        {
            return;
        }

        IsBoarding = key == "boarding";
    }

    [RelayCommand]
    private void SetBilling(string? key) =>
        Billing = key switch
        {
            "stay" => ServiceRateBilling.PerStay,
            "day" => ServiceRateBilling.PerDay,
            _ => ServiceRateBilling.PerUse,
        };

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving)
        {
            return;
        }

        var title = Name.Trim();
        if (title.Length == 0)
        {
            _toast.Show("نام خدمت را وارد کنید", Tone.Danger);
            return;
        }

        if (!long.TryParse(PersianNumberFormatter.ToAsciiDigits(PriceText).Replace(",", string.Empty).Replace("٬", string.Empty),
                out var price) || price < 0)
        {
            _toast.Show("نرخ خدمت را به‌صورت عدد معتبر وارد کنید", Tone.Danger);
            return;
        }

        IsSaving = true;
        try
        {
            if (_existing is null)
            {
                await _service.CreateAsync(new NewServiceRate(
                    IsBoarding ? ServiceRateSource.Boarding : ServiceRateSource.Clinic, title, price, Billing));
            }
            else
            {
                await _service.UpdateAsync(_existing with { Title = title, Price = price, IsActive = IsActive });
            }

            _dialogs.Close();
            _toast.Show(IsCreate ? $"«{title}» افزوده شد" : $"«{title}» ذخیره شد", Tone.Ok);
            _onSaved();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "ذخیرهٔ خدمت ناموفق بود"), Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
