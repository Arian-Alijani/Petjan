using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// Backs the add/edit cheque drawer (mirrors the reference's
/// <c>openChequeModal</c> / <c>paintForm</c> / <c>readForm</c> / <c>saveCheque</c>
/// in Animora-UI/assets/cheques-ui.js). Opened via <see cref="IDialogService.ShowDrawer"/>
/// from Phase 23's «ثبت چک جدید» header button and the row-menu's «ویرایش اطلاعات»
/// entry. All persistence flows through <see cref="IChequesQueryService"/> (the
/// single backend seam, R-18) — no mock branch leaks into this shape.
/// </summary>
public sealed partial class ChequeDrawerViewModel : ObservableObject
{
    private readonly IChequesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onSaved;
    private readonly Cheque? _editing;

    public ChequeDrawerViewModel(
        IChequesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        IReadOnlyList<ChequeBank> banks,
        Cheque? editing = null,
        Action? onSaved = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onSaved = onSaved;
        _editing = editing;

        foreach (var bank in banks)
        {
            var vm = new ChequeDrawerBankViewModel(bank);
            vm.PropertyChanged += (_, _) => OnBankOptionChanged(vm);
            Banks.Add(vm);
        }

        var today = JalaliCalendar.Today;
        if (editing is not null)
        {
            Direction = editing.Direction;
            BankKey = editing.BankKey;
            NumberText = ChequesPresentation.FaText(editing.Number);
            SayadText = ChequesPresentation.FaText(editing.Sayad);
            Party = editing.Party;
            SelectedRole = RoleOptions.Contains(editing.Role) ? editing.Role : RoleOptions[0];
            Phone = editing.Phone;
            AmountText = PersianNumberFormatter.GroupAscii(editing.Amount);
            IssueDate = editing.IssueDate;
            DueDate = editing.DueDate;
            Branch = editing.Branch;
            Subject = editing.Subject == "—" ? string.Empty : editing.Subject;
            Reference = editing.Reference == "—" ? string.Empty : editing.Reference;
            Note = editing.Note;
            Account = editing.Account;
        }
        else
        {
            Direction = ChequeDirection.In;
            BankKey = banks.Count > 0 ? banks[0].Key : "melli";
            NumberText = string.Empty;
            SayadText = string.Empty;
            Party = string.Empty;
            SelectedRole = RoleOptions[0];
            Phone = string.Empty;
            AmountText = string.Empty;
            IssueDate = today;
            DueDate = JalaliCalendar.AddDays(today, 30);
            Branch = string.Empty;
            Subject = string.Empty;
            Reference = string.Empty;
            Note = string.Empty;
            Account = "0106-5521-04";
        }

        SyncBankSelection();
        UpdateWords();
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public bool IsEdit => _editing is not null;

    /// <summary>«خزانه‌داری · فرم ثبت» / «اصلاح اطلاعات» (the <c>ch-mh .e</c>).</summary>
    public string Eyebrow => IsEdit ? "اصلاح اطلاعات" : "خزانه‌داری · فرم ثبت";

    /// <summary>«ثبت چک جدید» / «ویرایش چک {no}» (the <c>ch-mh h3</c>).</summary>
    public string Title => IsEdit ? $"ویرایش چک {ChequesPresentation.FaText(_editing!.Number)}" : "ثبت چک جدید";

    /// <summary>«ثبت چک» / «ذخیره تغییرات» (the primary footer button).</summary>
    public string SaveText => IsEdit ? "ذخیره تغییرات" : "ثبت چک";

    /// <summary>Whether the «ثبت و جدید» footer button shows (create only).</summary>
    public bool ShowSaveAndNew => !IsEdit;

    // ── Direction picker (.dir-pick) ─────────────────────────────────────

    [ObservableProperty]
    private ChequeDirection _direction;

    /// <summary>Whether the «چک دریافتی» option is the active one (the <c>.dir-opt.on</c>).</summary>
    public bool IsInSelected => Direction == ChequeDirection.In;

    /// <summary>Whether the «چک پرداختی» option is the active one.</summary>
    public bool IsOutSelected => Direction == ChequeDirection.Out;

    public IBrush InAccent => ChequesPresentation.Fill("#027a48");

    public IBrush OutAccent => ChequesPresentation.Fill("#d98032");

    public IBrush InIconBackground => ChequesPresentation.Fill("#ecfdf3");

    public IBrush OutIconBackground => ChequesPresentation.Fill("#fdf1e7");

    public IBrush InIconForeground => ChequesPresentation.Fill("#027a48");

    public IBrush OutIconForeground => ChequesPresentation.Fill("#c06a29");

    [RelayCommand]
    private void SetDirection(string? key)
    {
        Direction = key == "out" ? ChequeDirection.Out : ChequeDirection.In;
        OnPropertyChanged(nameof(IsInSelected));
        OnPropertyChanged(nameof(IsOutSelected));
    }

    // ── Bank picker (.bank-pick) ─────────────────────────────────────────

    [ObservableProperty]
    private string _bankKey = "melli";

    public ObservableCollection<ChequeDrawerBankViewModel> Banks { get; } = [];

    private void OnBankOptionChanged(ChequeDrawerBankViewModel bank)
    {
        if (bank.IsSelected)
        {
            foreach (var other in Banks)
            {
                if (!ReferenceEquals(other, bank))
                {
                    other.SetSelectedQuiet();
                }
            }

            BankKey = bank.Key;
        }
    }

    [RelayCommand]
    private void SetBank(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        BankKey = key;
        SyncBankSelection();
    }

    private void SyncBankSelection()
    {
        foreach (var bank in Banks)
        {
            bank.SetSelectedQuiet(bank.Key == BankKey);
        }
    }

    // ── Form fields (.frm) ───────────────────────────────────────────────

    public IReadOnlyList<string> RoleOptions { get; } =
    [
        "مالک بیمار", "تأمین‌کننده", "مشتری سازمانی", "پیمانکار", "موجر", "سایر",
    ];

    [ObservableProperty]
    private string _party = string.Empty;

    [ObservableProperty]
    private string _selectedRole = "مالک بیمار";

    [ObservableProperty]
    private string _numberText = string.Empty;

    [ObservableProperty]
    private string _sayadText = string.Empty;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private string _issueDate = string.Empty;

    [ObservableProperty]
    private string _dueDate = string.Empty;

    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _reference = string.Empty;

    [ObservableProperty]
    private string _subject = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>The cheque's account (not user-editable in the reference form).</summary>
    public string Account { get; private set; } = "0106-5521-04";

    private string Phone { get; set; } = string.Empty;

    /// <summary>«… تومان» amount-in-words hint (the <c>f-hint.words</c>).</summary>
    [ObservableProperty]
    private string _wordsHint = string.Empty;

    // Validation flags (red border on the required fields).
    [ObservableProperty]
    private bool _partyInvalid;

    [ObservableProperty]
    private bool _numberInvalid;

    [ObservableProperty]
    private bool _amountInvalid;

    partial void OnAmountTextChanged(string value)
    {
        // Mirror the reference's fAmount input handler: keep digits, regroup.
        var ascii = PersianNumberFormatter.ToAsciiDigits(value);
        var raw = new string(ascii.Where(char.IsDigit).ToArray());
        var grouped = raw.Length == 0 ? string.Empty : PersianNumberFormatter.GroupAscii(long.Parse(raw));
        if (grouped != value)
        {
            AmountText = grouped;
            return;
        }

        UpdateWords();
        AmountInvalid = false;
    }

    partial void OnNumberTextChanged(string value)
    {
        NumberInvalid = false;
    }

    partial void OnSayadTextChanged(string value)
    {
        // Mirror the fSayad handler: digits only, max 16, grouped by 4 with '-'.
        var ascii = PersianNumberFormatter.ToAsciiDigits(value);
        var raw = new string(ascii.Where(char.IsDigit).ToArray());
        if (raw.Length > 16)
        {
            raw = raw[..16];
        }

        var grouped = string.Join('-', Enumerable.Range(0, (raw.Length + 3) / 4)
            .Select(i => raw.Substring(i * 4, Math.Min(4, raw.Length - i * 4))));
        if (grouped != value)
        {
            SayadText = grouped;
        }
    }

    partial void OnPartyChanged(string value) => PartyInvalid = false;

    private void UpdateWords()
    {
        var ascii = new string(PersianNumberFormatter.ToAsciiDigits(AmountText).Where(char.IsDigit).ToArray());
        var amount = ascii.Length == 0 ? 0 : long.Parse(ascii);
        WordsHint = amount > 0
            ? ChequesPresentation.NumberToWords(amount) + " تومان"
            : "مبلغ را وارد کنید تا به حروف نمایش داده شود";
    }

    /// <summary>Quick due-date presets (۱ هفته / ۱ ماه / ۲ ماه / ۳ ماه).</summary>
    [RelayCommand]
    private void QuickDate(string? days)
    {
        if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(days ?? string.Empty), out var n))
        {
            return;
        }

        var basis = string.IsNullOrWhiteSpace(IssueDate) ? JalaliCalendar.Today : IssueDate;
        DueDate = JalaliCalendar.AddDays(basis, n);
    }

    // ── Save / cancel (saveCheque) ───────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync() => await SubmitAsync(false);

    [RelayCommand]
    private async Task SaveAndNewAsync() => await SubmitAsync(true);

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    private async Task SubmitAsync(bool again)
    {
        var party = Party.Trim();
        var number = PersianNumberFormatter.ToAsciiDigits(NumberText).Trim();
        var amount = ParseAmount();

        PartyInvalid = party.Length == 0;
        NumberInvalid = number.Length == 0;
        AmountInvalid = amount <= 0;
        if (PartyInvalid || NumberInvalid || AmountInvalid)
        {
            _toast.Show("لطفاً فیلدهای الزامی را کامل کنید", Tone.Danger);
            return;
        }

        if (IsEdit)
        {
            await _service.UpdateAsync(_editing!.Id, new UpdateChequeRequest(
                Direction, BankKey, number, SayadText, party, SelectedRole, Phone,
                amount, IssueDate, DueDate, Branch, Subject, Reference, Note, Account));
            _toast.Show($"تغییرات چک {ChequesPresentation.FaText(number)} ذخیره شد", Tone.Ok);
        }
        else
        {
            await _service.CreateAsync(new NewChequeRequest(
                Direction, BankKey, number, SayadText, party, SelectedRole, Phone,
                amount, IssueDate, DueDate, Branch, Subject, Reference, Note, Account));
            _toast.Show(
                $"چک {ChequesPresentation.FaText(number)} به مبلغ {ChequesPresentation.MoneyShort(amount)} تومان ثبت شد",
                Tone.Ok);
        }

        _onSaved?.Invoke();

        if (again)
        {
            ResetForNew();
            return;
        }

        _dialogs.Close();
    }

    private void ResetForNew()
    {
        var today = JalaliCalendar.Today;
        Party = string.Empty;
        NumberText = string.Empty;
        SayadText = string.Empty;
        AmountText = string.Empty;
        Branch = string.Empty;
        Subject = string.Empty;
        Reference = string.Empty;
        Note = string.Empty;
        IssueDate = today;
        DueDate = JalaliCalendar.AddDays(today, 30);
        PartyInvalid = NumberInvalid = AmountInvalid = false;
        UpdateWords();
    }

    private long ParseAmount()
    {
        var ascii = new string(PersianNumberFormatter.ToAsciiDigits(AmountText).Where(char.IsDigit).ToArray());
        return ascii.Length == 0 ? 0 : long.Parse(ascii);
    }
}

/// <summary>One bank tile of the drawer's <.bank-pick> (selectable, gradient mark).</summary>
public sealed partial class ChequeDrawerBankViewModel : ObservableObject
{
    public ChequeDrawerBankViewModel(ChequeBank bank)
    {
        Key = bank.Key;
        Name = bank.Name;
        Short = bank.Short;
        MarkLabel = bank.Short.Length > 3 ? bank.Short[..3] : bank.Short;
        Brush = ChequesPresentation.BankGradient(bank.ColorFrom, bank.ColorTo);
    }

    public string Key { get; }

    public string Name { get; }

    public string Short { get; }

    public string MarkLabel { get; }

    public IBrush Brush { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Sets the selection from the group sync (idempotent; safe to call repeatedly).</summary>
    public void SetSelectedQuiet(bool value = false) => IsSelected = value;
}