using Animora.Application.Text;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Cheques;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Shown inside the payment surface right after a payment (or a «ترکیبی» part) was taken by cheque:
/// step 1 asks whether the cheque must be registered in the Cheques module, step 2 collects the cheque
/// data (number, amount pre-filled, Jalali issue/due dates, bank details) and registers an
/// <b>inbound</b> cheque for the invoice's owner through <see cref="IChequesService"/>. The invoice
/// reference is written into the cheque note, so the cheque is traceable from the Cheques screen, the
/// History feed and the owner's cheque list. The payment itself is already recorded before this opens.
/// </summary>
public sealed partial class ChequePromptViewModel : ObservableObject
{
    private readonly IChequesService _cheques;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly string _ownerId;
    private readonly Action<bool> _done;

    public ChequePromptViewModel(
        IChequesService cheques,
        IToastService toast,
        IDataChangeNotifier changes,
        string ownerId,
        string ownerName,
        string invoiceReference,
        long amount,
        Action<bool> done)
    {
        _cheques = cheques;
        _toast = toast;
        _changes = changes;
        _ownerId = ownerId;
        _done = done;

        OwnerName = ownerName;
        AmountText = PersianNumberFormatter.ToPersianDigits(amount);
        IssueDate = JalaliCalendar.Today;
        DueDate = JalaliCalendar.Today;
        Note = string.IsNullOrWhiteSpace(ownerName)
            ? $"بابت فاکتور {invoiceReference}"
            : $"بابت فاکتور {invoiceReference} — {ownerName}";
    }

    public string OwnerName { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsForm))]
    private bool _isConfirmStep = true;

    public bool IsForm => !IsConfirmStep;

    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _amountText;
    [ObservableProperty] private string _issueDate;
    [ObservableProperty] private string _dueDate;
    [ObservableProperty] private string _bank = string.Empty;
    [ObservableProperty] private string _branch = string.Empty;
    [ObservableProperty] private string _account = string.Empty;
    [ObservableProperty] private string _sayadId = string.Empty;
    [ObservableProperty] private string _note;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string _error = string.Empty;
    [ObservableProperty] private bool _isSaving;

    public bool HasError => Error.Length > 0;

    private long Amount
    {
        get
        {
            var digits = new string(PersianText.NormalizeDigits(AmountText ?? string.Empty).Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out var v) ? v : 0;
        }
    }

    [RelayCommand]
    private void StartRegister() => IsConfirmStep = false;

    [RelayCommand]
    private void Skip() => _done(false);

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = string.Empty;
        var amount = Amount;
        if (string.IsNullOrWhiteSpace(Number)) Error = "شمارهٔ چک را وارد کنید.";
        else if (amount <= 0) Error = "مبلغ چک باید بیشتر از صفر باشد.";
        else if (string.IsNullOrWhiteSpace(IssueDate) || string.IsNullOrWhiteSpace(DueDate))
            Error = "تاریخ صدور و سررسید را انتخاب کنید.";
        if (Error.Length > 0) return;

        IsSaving = true;
        try
        {
            await _cheques.RegisterAsync(new NewChequeRequest(
                ChequeDirectionDto.Inbound,
                PersianText.NormalizeDigits(Number.Trim()),
                amount,
                IssueDate,
                DueDate,
                _ownerId,
                Bank,
                Branch,
                Account,
                PersianText.NormalizeDigits(SayadId ?? string.Empty),
                Note));
        }
        catch (InvalidOperationException ex)
        {
            Error = ChequesPresentation.Message(ex, "ثبت چک ناموفق بود؛ دوباره تلاش کنید.");
            IsSaving = false;
            return;
        }

        IsSaving = false;
        _toast.Show("چک در بخش چک‌ها ثبت شد", Tone.Ok);
        _changes.Notify(DataDomain.Cheques, DataDomain.Finance, DataDomain.History, DataDomain.Dashboard);
        _done(true);
    }
}
