using System;
using System.Globalization;
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
/// Backs the «بیمهٔ جدید» / «ویرایش بیمه» modal of the «پوشش بیمه‌ها» section: the insurer's name, the percentage of
/// the invoice it covers and an optional ceiling amount (<c>0</c> / empty = no ceiling).
/// </summary>
public sealed partial class InsurerEditModalViewModel : ObservableObject
{
    private readonly IServiceRateService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly InsurerCoverage? _existing;
    private readonly Action _onSaved;

    public InsurerEditModalViewModel(IServiceRateService service, IToastService toast, IDialogService dialogs,
        InsurerCoverage? existing, Action onSaved)
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

        _name = existing.Name;
        _percentText = PersianNumberFormatter.ToPersianDigits(
            existing.Percent.ToString("0.##", CultureInfo.InvariantCulture));
        _ceilingText = existing.Ceiling > 0
            ? PersianNumberFormatter.ToPersianDigits(existing.Ceiling.ToString(CultureInfo.InvariantCulture))
            : string.Empty;
    }

    public bool IsCreate => _existing is null;

    public string HeadTitle => IsCreate ? "بیمهٔ جدید" : "ویرایش بیمه";

    public string HeadSubtitle => IsCreate
        ? "نام بیمه و سهمی از فاکتور که پوشش می‌دهد را ثبت کنید؛ بلافاصله در فاکتورها قابل انتخاب می‌شود."
        : "تغییر درصد یا سقف پوشش فقط روی فاکتورهایی که بعداً بیمه می‌شوند اثر می‌گذارد.";

    public string SubmitText => IsCreate ? "افزودن بیمه" : "ذخیرهٔ تغییرات";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _percentText = string.Empty;

    [ObservableProperty]
    private string _ceilingText = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving)
        {
            return;
        }

        var name = Name.Trim();
        if (name.Length == 0)
        {
            _toast.Show("نام بیمه را وارد کنید", Tone.Danger);
            return;
        }

        if (!TryParsePercent(PercentText, out var percent))
        {
            _toast.Show("درصد پوشش باید عددی بین ۰ تا ۱۰۰ باشد", Tone.Danger);
            return;
        }

        if (!TryParseCeiling(CeilingText, out var ceiling))
        {
            _toast.Show("سقف پوشش را به‌صورت عدد معتبر وارد کنید (یا خالی بگذارید)", Tone.Danger);
            return;
        }

        IsSaving = true;
        try
        {
            if (_existing is null)
            {
                await _service.CreateInsurerAsync(new NewInsurerCoverage(name, percent, ceiling));
            }
            else
            {
                await _service.UpdateInsurerAsync(_existing with { Name = name, Percent = percent, Ceiling = ceiling });
            }

            _dialogs.Close();
            _toast.Show(IsCreate ? $"بیمهٔ «{name}» افزوده شد" : $"بیمهٔ «{name}» ذخیره شد", Tone.Ok);
            _onSaved();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "ذخیرهٔ بیمه ناموفق بود"), Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private static string Normalize(string? text) =>
        PersianNumberFormatter.ToAsciiDigits(text).Replace("٫", ".").Replace("٬", string.Empty)
            .Replace(",", string.Empty).Trim();

    private static bool TryParsePercent(string? text, out decimal percent) =>
        decimal.TryParse(Normalize(text), NumberStyles.Number, CultureInfo.InvariantCulture, out percent)
        && percent is >= 0m and <= 100m;

    private static bool TryParseCeiling(string? text, out long ceiling)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            ceiling = 0;
            return true;
        }

        return long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out ceiling);
    }
}
