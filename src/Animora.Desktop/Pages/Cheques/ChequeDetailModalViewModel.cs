using System.Collections.ObjectModel;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cheques;

public sealed class ChequeActionViewModel(ChequeActionDto action)
{
    public ChequeActionDto Action { get; } = action;
    public string Label { get; } = ChequesPresentation.ActionLabel(action);
    public bool NeedsReason { get; } = ChequesPresentation.NeedsReason(action);
    public bool IsDestructive { get; } = ChequesPresentation.IsDestructive(action);
}

public sealed class ChequeHistoryRowViewModel(ChequeHistoryEntry entry)
{
    public string Transition { get; } = ChequesPresentation.StateLabel(entry.From) + " ← " + ChequesPresentation.StateLabel(entry.To);
    public string When { get; } = PersianNumberFormatter.ToPersianDigits(entry.Date + "  " + entry.Time);
    public string Actor { get; } = string.IsNullOrWhiteSpace(entry.Actor) ? "—" : entry.Actor;
    public string Reason { get; } = entry.Reason ?? string.Empty;
    public bool HasReason { get; } = !string.IsNullOrWhiteSpace(entry.Reason);
}

/// <summary>Cheque detail sheet: facts, only the lifecycle actions the domain allows, mandatory reason prompt, and the append-only history timeline.</summary>
public sealed partial class ChequeDetailModalViewModel : ObservableObject
{
    private readonly IChequesService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _id;
    private readonly Action _onChanged;

    public ChequeDetailModalViewModel(IChequesService service, IToastService toast, IDialogService dialogs, string id, Action onChanged)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _id = id;
        _onChanged = onChanged;
        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری چک ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public ObservableCollection<ChequeActionViewModel> Actions { get; } = [];
    public ObservableCollection<ChequeHistoryRowViewModel> History { get; } = [];

    [ObservableProperty] private string _titleText = string.Empty;
    [ObservableProperty] private string _directionText = string.Empty;
    [ObservableProperty] private string _partyText = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _bankText = string.Empty;
    [ObservableProperty] private string _sayadText = string.Empty;
    [ObservableProperty] private string _issueText = string.Empty;
    [ObservableProperty] private string _dueText = string.Empty;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private Tone _stateTone = Tone.Neutral;
    [ObservableProperty] private bool _canDelete;
    [ObservableProperty] private bool _hasActions;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAwaitingReason))] private ChequeActionViewModel? _pending;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string _error = string.Empty;

    public bool IsAwaitingReason => Pending is not null;

    private async Task LoadAsync()
    {
        var detail = await _service.GetDetailAsync(_id);
        if (detail is null)
        {
            _toast.Show("چک پیدا نشد؛ فهرست را بازخوانی کنید", Tone.Danger);
            _dialogs.Close();
            return;
        }

        var c = detail.Cheque;
        TitleText = "چک " + PersianNumberFormatter.ToPersianDigits(c.Number);
        DirectionText = c.Direction == ChequeDirectionDto.Inbound ? "دریافتی" : "پرداختی";
        PartyText = string.IsNullOrWhiteSpace(c.Party) ? "—" : c.Party;
        AmountText = ChequesPresentation.Money(c.AmountMinor);
        BankText = string.Join(" · ", new[] { c.Bank, c.Branch }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (BankText.Length == 0) BankText = "—";
        SayadText = string.IsNullOrWhiteSpace(c.SayadId) ? "—" : PersianNumberFormatter.ToPersianDigits(c.SayadId);
        IssueText = PersianNumberFormatter.ToPersianDigits(c.IssueDate);
        DueText = PersianNumberFormatter.ToPersianDigits(c.DueDate);
        NoteText = c.Note ?? string.Empty;
        StateText = ChequesPresentation.StateLabel(c.State);
        StateTone = ChequesPresentation.StateTone(c.State);
        CanDelete = c.CanEdit;
        Actions.Clear();
        foreach (var a in c.Actions) Actions.Add(new ChequeActionViewModel(a));
        HasActions = Actions.Count > 0;
        History.Clear();
        foreach (var h in detail.History) History.Add(new ChequeHistoryRowViewModel(h));
        Pending = null;
        Reason = string.Empty;
    }

    [RelayCommand]
    private async Task ApplyAsync(ChequeActionViewModel? action)
    {
        if (action is null) return;
        Error = string.Empty;
        if (action.NeedsReason)
        {
            Pending = action; // the reason box appears; confirmed through ConfirmAsync
            return;
        }

        await RunAsync(action, null);
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Pending is null) return;
        if (string.IsNullOrWhiteSpace(Reason))
        {
            Error = "ثبت دلیل برای این عملیات الزامی است.";
            return;
        }

        await RunAsync(Pending, Reason.Trim());
    }

    [RelayCommand]
    private void CancelPending()
    {
        Pending = null;
        Reason = string.Empty;
        Error = string.Empty;
    }

    private async Task RunAsync(ChequeActionViewModel action, string? reason)
    {
        try
        {
            await _service.ApplyActionAsync(_id, action.Action, reason);
        }
        catch (InvalidOperationException ex)
        {
            Error = ChequesPresentation.Message(ex, "انجام عملیات ناموفق بود؛ دوباره تلاش کنید.");
            return;
        }

        _toast.Show(action.Label + " ثبت شد", action.IsDestructive ? Tone.Warn : Tone.Ok);
        _onChanged();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        try
        {
            await _service.DeleteAsync(_id);
        }
        catch (InvalidOperationException ex)
        {
            Error = ChequesPresentation.Message(ex, "حذف چک ناموفق بود؛ دوباره تلاش کنید.");
            return;
        }

        _toast.Show("چک حذف شد", Tone.Warn);
        _dialogs.Close();
        _onChanged();
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();
}
