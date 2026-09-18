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
/// Backs the cheque details drawer (mirrors the reference's <c>openCheque</c> /
/// <c>closeCheque</c> in Animora-UI/assets/cheques-ui.js and the <c>.ch-drawer</c>
/// scaffold in cheques.css): the big leaf card, the status/due-meter section, the
/// info <c>kv</c> list, the lifecycle timeline, the attachments and the internal
/// note, plus the status-aware footer actions. Opened via
/// <see cref="IDialogService.ShowDrawer"/> from a table row / grid leaf /
/// timeline item click and the row menu's «مشاهده جزئیات».
/// </summary>
public sealed partial class ChequeDetailViewModel : ObservableObject
{
    private readonly IChequesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onChanged;
    private readonly Action<Cheque>? _onEdit;

    private Cheque _cheque;

    public ChequeDetailViewModel(
        IChequesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        Cheque cheque,
        ChequeBank bank,
        Action? onChanged = null,
        Action<Cheque>? onEdit = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _cheque = cheque;
        _onChanged = onChanged;
        _onEdit = onEdit;

        BankMarkLabel = bank.Short.Length > 3 ? bank.Short[..3] : bank.Short;
        HeaderBrush = ChequesPresentation.BankGradient(bank.ColorFrom, bank.ColorTo);
        LeafGradientFrom = Avalonia.Media.Color.Parse(bank.ColorFrom);
        LeafGradientTo = Avalonia.Media.Color.Parse(bank.ColorTo);
        Bank = bank;

        BuildTimeline();
        BuildActions();
        ApplyChequeFields();
    }

    public ChequeBank Bank { get; }

    public string BankMarkLabel { get; }

    public IBrush HeaderBrush { get; }

    /// <summary>The big-leaf gradient's start/end colors (the reference's <c>--b1</c>/<c>--b2</c>).</summary>
    public Avalonia.Media.Color LeafGradientFrom { get; }

    public Avalonia.Media.Color LeafGradientTo { get; }

    // ── Header (ch-dh) ───────────────────────────────────────────────────

    public string HeaderEyebrow => $"{ChequesPresentation.DirectionLabel(_cheque.Direction)} · {_cheque.Id}";

    public string HeaderTitle => $"چک شماره {ChequesPresentation.FaText(_cheque.Number)}";

    // ── Big leaf (big-leaf) ──────────────────────────────────────────────

    [ObservableProperty]
    private string _leafBankName = string.Empty;

    [ObservableProperty]
    private string _leafBranch = string.Empty;

    [ObservableProperty]
    private string _leafDirectionTag = string.Empty;

    [ObservableProperty]
    private string _leafAmount = string.Empty;

    [ObservableProperty]
    private string _leafWords = string.Empty;

    [ObservableProperty]
    private string _leafPartyLabel = string.Empty;

    [ObservableProperty]
    private string _leafParty = string.Empty;

    [ObservableProperty]
    private string _leafDue = string.Empty;

    [ObservableProperty]
    private string _leafIssue = string.Empty;

    [ObservableProperty]
    private string _leafAccount = string.Empty;

    [ObservableProperty]
    private string _leafMicr = string.Empty;

    // ── Status/due section ───────────────────────────────────────────────

    public ChequeStatusPillViewModel Status => new(_cheque.Status);

    [ObservableProperty]
    private double _dueMeterPercent;

    /// <summary>
    /// The due-meter fill's pixel width. The track spans the drawer body's inner
    /// width (480 − 2×18 drawer padding − 2×1 border − 2×14 section padding = 414),
    /// mirroring the reference's inline <c>width:{pct}%</c>.
    /// </summary>
    [ObservableProperty]
    private double _dueMeterWidth;

    [ObservableProperty]
    private IBrush _dueMeterFill = ChequesPresentation.Fill("#0d9488");

    [ObservableProperty]
    private string _dueMeterText = string.Empty;

    [ObservableProperty]
    private string _dueMeterStatusDesc = string.Empty;

    // ── Info section (kv) ────────────────────────────────────────────────

    [ObservableProperty]
    private string _infoSayad = string.Empty;

    [ObservableProperty]
    private string _infoBankBranch = string.Empty;

    [ObservableProperty]
    private string _infoParty = string.Empty;

    [ObservableProperty]
    private string _infoPartyRole = string.Empty;

    [ObservableProperty]
    private string _infoPhone = string.Empty;

    [ObservableProperty]
    private string _infoSubject = string.Empty;

    [ObservableProperty]
    private string _infoReference = string.Empty;

    // ── Lifecycle timeline (dtl) ─────────────────────────────────────────

    public ObservableCollection<ChequeDetailEventViewModel> Timeline { get; } = [];

    // ── Attachments (att-row) ────────────────────────────────────────────

    public string AttachmentCaption => $"IMG_{ChequesPresentation.FaText(_cheque.Number)}.jpg";

    // ── Internal note ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _noteText = string.Empty;

    // ── Footer actions (ch-df) ───────────────────────────────────────────

    public ObservableCollection<ChequeDrawerActionViewModel> Actions { get; } = [];

    private void BuildActions()
    {
        Actions.Clear();
        var status = _cheque.Status;

        if (status == ChequeStatus.InHand)
        {
            Actions.Add(new ChequeDrawerActionViewModel("atbank", "واگذاری به بانک", "IconBank", "outline", false));
        }

        if (status is ChequeStatus.InHand or ChequeStatus.AtBank)
        {
            Actions.Add(new ChequeDrawerActionViewModel("cleared", "ثبت وصول", "IconCheck", "primary", false));
            Actions.Add(new ChequeDrawerActionViewModel("bounced", "برگشت", "IconAlert", "outline", true));
        }

        if (status == ChequeStatus.Bounced)
        {
            Actions.Add(new ChequeDrawerActionViewModel("inhand", "بازگشت به جریان", "IconRefresh", "primary", false));
        }

        if (Actions.Count == 0)
        {
            Actions.Add(new ChequeDrawerActionViewModel("print", "چاپ رسید", "IconPrinter", "outline", false));
        }

        // The reference always appends an icon-only ghost «edit» button.
        Actions.Add(new ChequeDrawerActionViewModel("edit", string.Empty, "IconEdit", "ghost", false));
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void Close() => _dialogs.Close();

    [RelayCommand]
    private void Edit() => _onEdit?.Invoke(_cheque);

    [RelayCommand]
    private async Task RunActionAsync(ChequeDrawerActionViewModel? action)
    {
        if (action is null)
        {
            return;
        }

        switch (action.Key)
        {
            case "atbank":
                await ApplyStatusAsync(ChequeStatus.AtBank, "IconBank");
                return;
            case "cleared":
                await ApplyStatusAsync(ChequeStatus.Cleared, "IconCheck");
                return;
            case "bounced":
                await ApplyStatusAsync(ChequeStatus.Bounced, "IconAlert");
                return;
            case "inhand":
                await ApplyStatusAsync(ChequeStatus.InHand, "IconRefresh");
                return;
            case "print":
                _toast.Show("رسید چک برای چاپ آماده شد (نمونه طراحی)", Tone.Info);
                return;
            case "edit":
                Edit();
                return;
        }
    }

    private async Task ApplyStatusAsync(ChequeStatus status, string icon)
    {
        await _service.BulkUpdateStatusAsync([_cheque.Id], status);
        _cheque = _cheque with { Status = status };
        OnPropertyChanged(nameof(Status));
        ApplyChequeFields();
        BuildActions();
        _toast.Show(
            $"وضعیت چک {ChequesPresentation.FaText(_cheque.Number)} به «{ChequesPresentation.StatusLabel(status)}» تغییر کرد",
            Tone.Ok);
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void CopySayad() => _toast.Show("شناسه صیادی کپی شد", Tone.Ok);

    [RelayCommand]
    private void PreviewImage() =>
        _toast.Show("پیش‌نمایش تصویر روی چک (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void AddAttachment() =>
        _toast.Show("برای افزودن پیوست، فایل را رها کنید (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void SaveNote() => _toast.Show("یادداشت ذخیره شد (نمونه طراحی)", Tone.Ok);

    [RelayCommand]
    private void Contact() =>
        _toast.Show($"تماس با {_cheque.Party} برقرار شد (نمونه طراحی)", Tone.Ok);

    // ── Field projection ─────────────────────────────────────────────────

    private void ApplyChequeFields()
    {
        LeafBankName = Bank.Name;
        LeafBranch = _cheque.Branch;
        LeafDirectionTag = ChequesPresentation.DirectionLabel(_cheque.Direction);
        LeafAmount = ChequesPresentation.Money(_cheque.Amount);
        LeafWords = ChequesPresentation.NumberToWords(_cheque.Amount) + " تومان";
        LeafPartyLabel = _cheque.Direction == ChequeDirection.In ? "دریافت از" : "در وجه";
        LeafParty = _cheque.Party;
        LeafDue = ChequesPresentation.FaText(_cheque.DueDate);
        LeafIssue = ChequesPresentation.FaText(_cheque.IssueDate);
        LeafAccount = ChequesPresentation.FaText(_cheque.Account);
        LeafMicr = ChequesPresentation.FaText(_cheque.Sayad.Replace("-", " "));

        var (pct, tone) = ChequesPresentation.DueMeter(_cheque);
        DueMeterPercent = pct;
        DueMeterWidth = 414 * pct / 100;
        DueMeterFill = tone switch
        {
            "warn" => ChequesPresentation.Fill("#b54708"),
            "danger" => ChequesPresentation.Fill("#d92d20"),
            _ => ChequesPresentation.Fill("#0d9488"),
        };
        DueMeterText = ChequesPresentation.DueMeterText(_cheque);
        DueMeterStatusDesc = ChequesPresentation.StatusDesc(_cheque.Status) + " — آخرین بروزرسانی توسط واحد مالی";

        InfoSayad = ChequesPresentation.FaText(_cheque.Sayad);
        InfoBankBranch = $"{Bank.Name} — {_cheque.Branch}";
        InfoParty = _cheque.Party;
        InfoPartyRole = _cheque.Role;
        InfoPhone = ChequesPresentation.FaText(_cheque.Phone);
        InfoSubject = _cheque.Subject;
        InfoReference = ChequesPresentation.FaText(_cheque.Reference);
        NoteText = _cheque.Note;

        OnPropertyChanged(nameof(HeaderEyebrow));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(AttachmentCaption));
    }

    private void BuildTimeline()
    {
        Timeline.Clear();
        foreach (var e in ChequesPresentation.TimelineEvents(_cheque))
        {
            Timeline.Add(new ChequeDetailEventViewModel(
                e.Title,
                ChequesPresentation.FaText(e.Date),
                e.By,
                ChequesPresentation.ToneColor(e.Tone)));
        }
    }
}

/// <summary>One lifecycle event of the drawer's «چرخه عمر چک» timeline.</summary>
public sealed class ChequeDetailEventViewModel
{
    public ChequeDetailEventViewModel(string title, string date, string by, IBrush dotBrush)
    {
        Title = title;
        Date = date;
        By = by;
        DotBrush = dotBrush;
    }

    public string Title { get; }

    public string Date { get; }

    public string By { get; }

    public IBrush DotBrush { get; }
}

/// <summary>One footer button of the details drawer (the reference's status-aware action set).</summary>
public sealed class ChequeDrawerActionViewModel
{
    public ChequeDrawerActionViewModel(string key, string label, string iconKey, string style, bool isDanger)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
        Style = style;
        IsDanger = isDanger;
    }

    public string Key { get; }

    public string Label { get; }

    public string IconKey { get; }

    /// <summary><c>primary</c> | <c>outline</c> | <c>ghost</c> — the button class.</summary>
    public string Style { get; }

    public bool IsPrimary => Style == "primary";

    public bool IsOutline => Style == "outline";

    public bool IsGhost => Style == "ghost";

    public bool IsDanger { get; }
}
