using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Finance;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Finance;

/// <summary>
/// Backs the «ثبت هزینه جدید» modal (mirrors the reference's
/// <c>openExpenseModal</c> / <c>.modal</c> in finance-page.js): the description
/// field, the دسته‌بندی / روش پرداخت selects, the amount input with its
/// Persian-grouped live formatting + quick-amount presets, the date field and
/// the ثبت هزینه / انصراف footer. Submits through
/// <see cref="IFinanceQueryService.AddExpenseAsync"/>.
/// </summary>
public sealed partial class AddExpenseModalViewModel : ObservableObject
{
    private readonly IFinanceQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action<ExpenseEntry>? _onAdded;

    private static readonly long[] QuickAmounts =
        [1_000_000, 2_500_000, 5_000_000, 10_000_000];

    public AddExpenseModalViewModel(
        IFinanceQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string today,
        Action<ExpenseEntry>? onAdded = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onAdded = onAdded;

        Date = PersianNumberFormatter.ToPersianDigits(today);

        foreach (var value in QuickAmounts)
        {
            Quick.Add(new FinanceQuickAmountViewModel(value));
        }

        _ = LoadOptionsAsync();
    }

    /// <summary>«شرح هزینه» input (finance.js <c>#fx-desc</c>).</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>«مبلغ (تومان)» — Persian-grouped text (finance.js <c>#fx-amount</c>).</summary>
    [ObservableProperty]
    private string _amountText = string.Empty;

    /// <summary>«تاریخ» — Persian digits of today (finance.js <c>#fx-date</c>).</summary>
    [ObservableProperty]
    private string _date = string.Empty;

    [ObservableProperty]
    private FinanceOptionViewModel? _selectedCategory;

    [ObservableProperty]
    private FinanceOptionViewModel? _selectedMethod;

    public ObservableCollection<FinanceOptionViewModel> Categories { get; } = [];

    public ObservableCollection<FinanceOptionViewModel> Methods { get; } = [];

    public ObservableCollection<FinanceQuickAmountViewModel> Quick { get; } = [];

    private long _rawAmount;

    private async Task LoadOptionsAsync()
    {
        var cats = await _service.GetExpenseCategoriesAsync();
        var methods = await _service.GetPaymentMethodsAsync();

        Categories.Clear();
        foreach (var c in cats)
        {
            Categories.Add(new FinanceOptionViewModel(c.Id, c.Label));
        }

        Methods.Clear();
        foreach (var m in methods)
        {
            Methods.Add(new FinanceOptionViewModel(m.Id, m.Label));
        }

        SelectedCategory = Categories.FirstOrDefault();
        SelectedMethod = Methods.FirstOrDefault();
    }

    partial void OnAmountTextChanged(string value)
    {
        var raw = new string(PersianNumberFormatter.ToAsciiDigits(value).Where(char.IsDigit).ToArray());
        _rawAmount = long.TryParse(raw, out var v) ? v : 0;
        var formatted = _rawAmount > 0 ? FinancePresentation.Money(_rawAmount) : string.Empty;
        if (formatted != value)
        {
            AmountText = formatted;
        }

        foreach (var q in Quick)
        {
            q.IsActive = false;
        }
    }

    [RelayCommand]
    private void PickQuick(FinanceQuickAmountViewModel? preset)
    {
        if (preset is null)
        {
            return;
        }

        _rawAmount = preset.Value;
        AmountText = FinancePresentation.Money(preset.Value);
        foreach (var q in Quick)
        {
            q.IsActive = q == preset;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_rawAmount <= 0)
        {
            _toast.Show("مبلغ هزینه را وارد کنید", Tone.Danger);
            return;
        }

        var request = new NewExpenseRequest(
            Description.Trim(),
            SelectedCategory?.Id ?? "other",
            SelectedMethod?.Id ?? "cash",
            _rawAmount,
            PersianNumberFormatter.ToAsciiDigits(Date));

        var entry = await _service.AddExpenseAsync(request);
        _dialogs.Close();
        _onAdded?.Invoke(entry);
        _toast.Show("هزینه ثبت شد (نمونه طراحی)", Tone.Ok);
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
