using System.Collections.ObjectModel;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>«ثبت چک جدید»: direction toggle, number, amount, Jalali issue/due dates and the mandatory counterparty (owner when received, supplier when issued).</summary>
public sealed partial class ChequeRegisterModalViewModel : ObservableObject
{
    private readonly IChequesService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action _onCreated;
    private readonly Debouncer _partyDebounce = new(TimeSpan.FromMilliseconds(250));
    private int _partyGeneration;

    public ChequeRegisterModalViewModel(IChequesService service, IToastService toast, IDialogService dialogs, Action onCreated)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onCreated = onCreated;
        _ = LoadPartiesAsync();
    }

    public ObservableCollection<ChequeParty> Parties { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOutbound), nameof(PartyLabel))]
    private bool _isInbound = true;

    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _issueDate = string.Empty;
    [ObservableProperty] private string _dueDate = string.Empty;
    [ObservableProperty] private string _partySearch = string.Empty;
    [ObservableProperty] private ChequeParty? _selectedParty;
    [ObservableProperty] private string _bank = string.Empty;
    [ObservableProperty] private string _branch = string.Empty;
    [ObservableProperty] private string _sayadId = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private bool _isSaving;

    public bool IsOutbound => !IsInbound;
    public string PartyLabel => IsInbound ? "دریافت از (مشتری)" : "پرداخت به (تأمین‌کننده)";

    partial void OnIsInboundChanged(bool value)
    {
        SelectedParty = null;
        _ = LoadPartiesAsync();
    }

    partial void OnPartySearchChanged(string value) => _partyDebounce.Debounce(() => _ = LoadPartiesAsync());

    [RelayCommand]
    private void ChooseDirection(string? key) => IsInbound = key != "out";

    private async Task LoadPartiesAsync()
    {
        var token = ++_partyGeneration;
        try
        {
            var list = IsInbound ? await _service.FindOwnersAsync(PartySearch) : await _service.FindSuppliersAsync(PartySearch);
            if (token != _partyGeneration) return;
            var keep = SelectedParty?.Id;
            Parties.Clear();
            foreach (var p in list) Parties.Add(p);
            SelectedParty = keep is null ? null : Parties.FirstOrDefault(p => p.Id == keep);
        }
        catch (InvalidOperationException)
        {
            Parties.Clear();
        }
    }

    private static long ParseAmount(string text)
    {
        var digits = Animora.Application.Text.PersianText.NormalizeDigits(text ?? string.Empty);
        var clean = new string(digits.Where(char.IsDigit).ToArray());
        return long.TryParse(clean, out var v) ? v : 0;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Error = string.Empty;
        var amount = ParseAmount(AmountText);
        if (string.IsNullOrWhiteSpace(Number)) Error = "شمارهٔ چک را وارد کنید.";
        else if (amount <= 0) Error = "مبلغ چک باید بیشتر از صفر باشد.";
        else if (string.IsNullOrWhiteSpace(IssueDate) || string.IsNullOrWhiteSpace(DueDate)) Error = "تاریخ صدور و سررسید را انتخاب کنید.";
        else if (SelectedParty is null)
            Error = IsInbound ? "برای چک دریافتی انتخاب مشتری الزامی است." : "برای چک پرداختی انتخاب تأمین‌کننده الزامی است.";
        if (Error.Length > 0) return;

        IsSaving = true;
        try
        {
            await _service.RegisterAsync(new NewChequeRequest(
                IsInbound ? ChequeDirectionDto.Inbound : ChequeDirectionDto.Outbound,
                Animora.Application.Text.PersianText.NormalizeDigits(Number.Trim()), amount, IssueDate, DueDate, SelectedParty!.Id,
                Bank, Branch, null, Animora.Application.Text.PersianText.NormalizeDigits(SayadId), Note));
        }
        catch (InvalidOperationException ex)
        {
            Error = ChequesPresentation.Message(ex, "ثبت چک ناموفق بود؛ دوباره تلاش کنید.");
            IsSaving = false;
            return;
        }

        IsSaving = false;
        _toast.Show("چک ثبت شد", Tone.Ok);
        _dialogs.Close();
        _onCreated();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
