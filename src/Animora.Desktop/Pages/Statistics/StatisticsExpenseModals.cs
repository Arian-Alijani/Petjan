using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>Shared parsing / selection helpers of the three expense modals.</summary>
internal static class StatisticsModalInput
{
    /// <summary>Digits-only amount (Persian / Arabic digits and any separators accepted); <see langword="null"/> when empty or zero.</summary>
    public static long? ParseAmount(string? text)
    {
        var digits = new string(PersianNumberFormatter.ToAsciiDigits(text).Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 15 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : null;
    }

    public static IReadOnlyList<StatisticsChoice> CategoryChoices(string? selected)
    {
        var list = StatisticsFormat.CategoryCodes
            .Select(c => new StatisticsChoice(c, StatisticsFormat.CategoryName(c), StatisticsFormat.CategoryColor(c)))
            .ToArray();
        Select(list, selected ?? "OTHER");
        return list;
    }

    public static void Select(IEnumerable<StatisticsChoice> options, string key)
    {
        foreach (var o in options) o.IsSelected = o.Key == key;
    }

    public static string SelectedKey(IEnumerable<StatisticsChoice> options, string fallback) =>
        options.FirstOrDefault(o => o.IsSelected)?.Key ?? fallback;

    /// <summary>
    /// The instant an expense incurred on the Jalali <paramref name="jalali"/> day is booked at: the current time of day for
    /// today, noon for a past day (so the clinic-local day never shifts across a timezone offset).
    /// </summary>
    public static DateTimeOffset? ToIncurredUtc(string jalali, out bool isFuture)
    {
        isFuture = false;
        if (StatisticsFormat.ToGregorianKey(jalali) is not { } key) return null;
        var day = new DateTime(key / 10000, key / 100 % 100, key % 100, 0, 0, 0, DateTimeKind.Local);
        var today = DateTime.Today;
        if (day > today)
        {
            isFuture = true;
            return null;
        }

        var local = day == today ? DateTime.Now : day.AddHours(12);
        return new DateTimeOffset(local).ToUniversalTime();
    }
}

/// <summary>«ثبت هزینه»: records and posts a one-off expense.</summary>
public sealed partial class StatisticsExpenseModalViewModel : ObservableObject
{
    private readonly IStatisticsQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _onSaved;

    public StatisticsExpenseModalViewModel(IStatisticsQueryService service, IToastService toast, IDialogService dialogs,
        Action<string> onSaved)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onSaved = onSaved;
        Categories = StatisticsModalInput.CategoryChoices("OTHER");
        Settlements =
        [
            new StatisticsChoice(nameof(ExpenseSettlement.Cash), StatisticsFormat.SettlementName(ExpenseSettlement.Cash)) { IsSelected = true },
            new StatisticsChoice(nameof(ExpenseSettlement.Card), StatisticsFormat.SettlementName(ExpenseSettlement.Card)),
            new StatisticsChoice(nameof(ExpenseSettlement.Transfer), StatisticsFormat.SettlementName(ExpenseSettlement.Transfer)),
            new StatisticsChoice(nameof(ExpenseSettlement.Credit), StatisticsFormat.SettlementName(ExpenseSettlement.Credit)),
        ];
        _date = JalaliCalendar.Today;
    }

    public IReadOnlyList<StatisticsChoice> Categories { get; }

    public IReadOnlyList<StatisticsChoice> Settlements { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _date;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>Live Persian preview of the typed amount («۱٬۲۰۰٬۰۰۰ تومان»).</summary>
    public string AmountPreview => StatisticsModalInput.ParseAmount(AmountText) is { } v ? StatisticsFormat.Money(v) : string.Empty;

    partial void OnAmountTextChanged(string value) => OnPropertyChanged(nameof(AmountPreview));

    [RelayCommand]
    private void SelectCategory(StatisticsChoice? option)
    {
        if (option is not null) StatisticsModalInput.Select(Categories, option.Key);
    }

    [RelayCommand]
    private void SelectSettlement(StatisticsChoice? option)
    {
        if (option is not null) StatisticsModalInput.Select(Settlements, option.Key);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving) return;
        var title = Title.Trim();
        if (title.Length == 0) { Fail("عنوان هزینه را وارد کنید"); return; }
        if (StatisticsModalInput.ParseAmount(AmountText) is not { } amount)
        {
            Fail("مبلغ هزینه را به‌صورت عدد مثبت وارد کنید");
            return;
        }

        if (StatisticsModalInput.ToIncurredUtc(Date, out var future) is not { } incurred)
        {
            Fail(future ? "تاریخ هزینه نمی‌تواند در آینده باشد" : "تاریخ هزینه را انتخاب کنید");
            return;
        }

        var category = StatisticsModalInput.SelectedKey(Categories, "OTHER");
        var settlement = Enum.Parse<ExpenseSettlement>(StatisticsModalInput.SelectedKey(Settlements, nameof(ExpenseSettlement.Cash)));
        var note = Note.Trim();
        IsSaving = true;
        try
        {
            await _service.AddExpenseAsync(new NewExpense(category, title, amount, incurred, settlement, null, null,
                note.Length == 0 ? null : note));
            _dialogs.Close();
            _onSaved($"هزینهٔ «{title}» ثبت شد");
        }
        catch (StatisticsQueryException ex)
        {
            _toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private void Fail(string message) => _toast.Show(message, Tone.Danger);
}

/// <summary>«برگشت هزینه»: corrects a posted expense with a reversing expense (never an edit).</summary>
public sealed partial class StatisticsReverseModalViewModel(IStatisticsQueryService service, IToastService toast,
    IDialogService dialogs, StatisticsExpenseRowViewModel row, Action<string> onSaved) : ObservableObject
{
    public string Title => $"برگشت هزینهٔ «{row.Title}»";

    public string Summary => $"{row.CategoryName} · {row.DateText} · {row.Amount}";

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving) return;
        var reason = Reason.Trim();
        if (reason.Length == 0)
        {
            toast.Show("دلیل برگشت را وارد کنید", Tone.Danger);
            return;
        }

        IsSaving = true;
        try
        {
            await service.ReverseExpenseAsync(row.Row.Id, reason);
            dialogs.Close();
            onSaved($"هزینهٔ «{row.Title}» برگشت خورد");
        }
        catch (StatisticsQueryException ex)
        {
            toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => dialogs.Close();
}

/// <summary>«هزینهٔ ثابت»: adds or edits a fixed monthly expense template.</summary>
public sealed partial class StatisticsRecurringModalViewModel : ObservableObject
{
    private readonly IStatisticsQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly RecurringExpenseItem? _existing;
    private readonly Action<string> _onSaved;

    public StatisticsRecurringModalViewModel(IStatisticsQueryService service, IToastService toast, IDialogService dialogs,
        RecurringExpenseItem? existing, Action<string> onSaved)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _existing = existing;
        _onSaved = onSaved;
        Categories = StatisticsModalInput.CategoryChoices(existing?.CategoryCode ?? "RENT");
        _title = existing?.Title ?? string.Empty;
        _amountText = existing is null ? string.Empty : StatisticsFormat.Number(existing.AmountMinor);
        _dayText = StatisticsFormat.Count(existing?.DayOfMonth ?? 1);
        _note = existing?.Note ?? string.Empty;
    }

    public bool IsCreate => _existing is null;

    public string HeadTitle => IsCreate ? "هزینهٔ ثابت جدید" : "ویرایش هزینهٔ ثابت";

    public string HeadSubtitle => "هزینه‌ای که هر ماه تکرار می‌شود (مثل اجاره یا حقوق)؛ در سررسید یادآوری می‌شود و با دکمهٔ «پرداخت» در دفتر هزینه‌ها ثبت می‌گردد.";

    public string SubmitText => IsCreate ? "افزودن" : "ذخیرهٔ تغییرات";

    public IReadOnlyList<StatisticsChoice> Categories { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _amountText;

    [ObservableProperty]
    private string _dayText;

    [ObservableProperty]
    private string _note;

    [ObservableProperty]
    private bool _isSaving;

    public string AmountPreview => StatisticsModalInput.ParseAmount(AmountText) is { } v ? StatisticsFormat.Money(v) : string.Empty;

    partial void OnAmountTextChanged(string value) => OnPropertyChanged(nameof(AmountPreview));

    [RelayCommand]
    private void SelectCategory(StatisticsChoice? option)
    {
        if (option is not null) StatisticsModalInput.Select(Categories, option.Key);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving) return;
        var title = Title.Trim();
        if (title.Length == 0) { _toast.Show("عنوان هزینه را وارد کنید", Tone.Danger); return; }
        if (StatisticsModalInput.ParseAmount(AmountText) is not { } amount)
        {
            _toast.Show("مبلغ را به‌صورت عدد مثبت وارد کنید", Tone.Danger);
            return;
        }

        if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(DayText).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || day is < 1 or > 31)
        {
            _toast.Show("روز سررسید باید عددی بین ۱ تا ۳۱ باشد", Tone.Danger);
            return;
        }

        var note = Note.Trim();
        var input = new RecurringExpenseInput(StatisticsModalInput.SelectedKey(Categories, "OTHER"), title, amount, day,
            _existing?.SupplierId, note.Length == 0 ? null : note);
        IsSaving = true;
        try
        {
            if (_existing is null) await _service.AddRecurringExpenseAsync(input);
            else await _service.EditRecurringExpenseAsync(_existing.Id, input);
            _dialogs.Close();
            _onSaved(IsCreate ? $"هزینهٔ ثابت «{title}» افزوده شد" : $"هزینهٔ ثابت «{title}» ذخیره شد");
        }
        catch (StatisticsQueryException ex)
        {
            _toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
