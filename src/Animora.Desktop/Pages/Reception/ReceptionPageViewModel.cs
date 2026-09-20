using System.Collections.ObjectModel;
using Animora.Contracts.Reception;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Reception;

public sealed partial class ReceptionPageViewModel : PageViewModelBase
{
    private const int PageSize = 8;
    private readonly IReceptionQueueService _queueService;
    private readonly IDialogService _dialog;
    private readonly IToastService _toast;
    private string _activeFilter = "all";

    // Cached result of the last FilterQueue() run, set at the end of
    // ApplyFilter(). TotalCount/CanGoNext read this instead of re-running the
    // Where/OrderBy pipeline on every binding evaluation — recomputed only
    // when the filter's actual inputs (queue, filter, search) change
    // (PERFORMANCE.md §13).
    private int _filteredCount;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _pageNumber = 1;

    [ObservableProperty]
    private string _lastRefreshText = string.Empty;

    public ReceptionPageViewModel(
        IReceptionQueueService queueService,
        IDialogService dialog,
        IToastService toast)
    {
        _queueService = queueService;
        _dialog = dialog;
        _toast = toast;
        Title = "پذیرش و صف امروز";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("پذیرش");
        // Non-blocking init (PERFORMANCE.md §4): the mock resolves synchronously,
        // so the queue is populated at construction while a real async service
        // would no longer block the UI thread.
        _ = RefreshAsync();
    }

    public RangeObservableCollection<QueueTicket> Queue { get; } = [];
    public RangeObservableCollection<ReceptionQueueRowViewModel> VisibleQueue { get; } = [];

    public string TodayText => JalaliDate.FromDateTime(DateTime.Today).ToString("long");
    public string WaitingCount => Count(QueueTicketState.Waiting);
    public string CalledCount => Count(QueueTicketState.Called);
    public string InVisitCount => Count(QueueTicketState.InVisit);
    public string DoneCount => Count(QueueTicketState.Done);
    public string UrgentCount => PersianNumberFormatter.ToPersianDigits(Queue.Count(x => x.IsUrgent && IsActive(x.State)));

    // ── Precomposed lines (Avalonia perf: avoid concatenation-only <Run>) ──
    // Each string below is the exact final text the matching TextBlock showed
    // when it was assembled from several <Run>s; composing it here means the
    // View sets Text once. Raised alongside their source props (see NotifyStats
    // / OnLastRefreshTextChanged) so the bound TextBlock refreshes in step.
    public string TodayLine => "امروز، " + TodayText;
    public string RefreshLine => "آخرین بازخوانی: " + LastRefreshText + " · داده‌ها نمونه هستند";
    public string AverageWaitLine => AverageWaitText + " دقیقه";
    public string UrgentCountLine => UrgentCount + " مورد";
    public string DoneCountLine => DoneCount + " مورد";
    // Reads the cache populated by ApplyFilter() instead of re-running
    // FilterQueue() (PERFORMANCE.md §13); ApplyFilter always runs at least
    // once before this is read (constructor → RefreshAsync → ApplyFilter).
    public string TotalCount => PersianNumberFormatter.ToPersianDigits(_filteredCount);
    public string PageNumberText => PersianNumberFormatter.ToPersianDigits(PageNumber);
    public string AverageWaitText => PersianNumberFormatter.ToPersianDigits(CalculateAverageWait());
    public string NextPatientText
    {
        get
        {
            var next = Queue.Where(x => IsActive(x.State))
                .OrderByDescending(x => x.IsUrgent)
                .ThenBy(x => x.ArrivalTime)
                .FirstOrDefault();
            return next is null ? "—" : $"{next.PatientName} · {next.DoctorName}";
        }
    }

    public bool HasRows => VisibleQueue.Count > 0;
    public bool IsEmpty => !HasRows;
    public bool IsAllFilter => _activeFilter == "all";
    public bool IsActiveFilter => _activeFilter == "active";
    public bool IsUrgentFilter => _activeFilter == "urgent";
    public bool IsDoneFilter => _activeFilter == "done";
    public bool CanGoPrevious => PageNumber > 1;
    // Same cache as TotalCount — avoids a second redundant FilterQueue() run
    // per binding evaluation (PERFORMANCE.md §13).
    public bool CanGoNext => PageNumber * PageSize < _filteredCount;

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value)
    {
        PageNumber = 1;
        _searchDebounce.Debounce(ApplyFilter);
    }

    partial void OnLastRefreshTextChanged(string value) => OnPropertyChanged(nameof(RefreshLine));

    partial void OnPageNumberChanged(int value) => NotifyPageState();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var tickets = await _queueService.GetQueueAsync();
            Queue.ReplaceAll(tickets);

            LastRefreshText = PersianNumberFormatter.ToPersianDigits(DateTime.Now.ToString("HH:mm"));
            ApplyFilter();
            NotifyStats();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AdmitAsync(ReceptionQueueRowViewModel? row) =>
        await MutateAsync(row, _queueService.AdmitAsync, $"«{row?.Ticket.PatientName}» فراخوانده شد (نمونه طراحی)");

    [RelayCommand]
    private async Task EnterVisitAsync(ReceptionQueueRowViewModel? row) =>
        await MutateAsync(row, _queueService.EnterVisitAsync, $"«{row?.Ticket.PatientName}» وارد اتاق ویزیت شد (نمونه طراحی)");

    [RelayCommand]
    private async Task LeaveQueueAsync(ReceptionQueueRowViewModel? row) =>
        await MutateAsync(row, _queueService.LeaveQueueAsync, $"«{row?.Ticket.PatientName}» از صف خارج شد (نمونه طراحی)", Tone.Danger);

    [RelayCommand]
    private async Task MarkNoShowAsync(ReceptionQueueRowViewModel? row) =>
        await MutateAsync(row, _queueService.MarkNoShowAsync, $"«{row?.Ticket.PatientName}» عدم‌مراجعه ثبت شد (نمونه طراحی)", Tone.Warn);

    [RelayCommand]
    private async Task ToggleUrgentAsync(ReceptionQueueRowViewModel? row) =>
        await MutateAsync(row, _queueService.ToggleUrgentAsync, row?.Ticket.IsUrgent == true ? "اولویت عادی شد (نمونه طراحی)" : "اورژانس فعال شد (نمونه طراحی)", row?.Ticket.IsUrgent == true ? Tone.Ok : Tone.Danger);

    [RelayCommand]
    private async Task RemoveAsync(ReceptionQueueRowViewModel? row)
    {
        if (row?.Ticket.State == QueueTicketState.Called)
        {
            await MarkNoShowAsync(row);
        }
        else
        {
            await LeaveQueueAsync(row);
        }
    }

    [RelayCommand]
    private async Task CallDoctorAsync(ReceptionQueueRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _queueService.AdmitAsync(row.Ticket.Id);
        _toast.Show($"پزشک {row.Ticket.DoctorName.Replace("دکتر ", string.Empty)} فراخوانده شد (نمونه طراحی)", Tone.Info);
        await RefreshAsync();
    }

    [RelayCommand]
    private void CallOwner(ReceptionQueueRowViewModel? row)
    {
        if (row is not null)
        {
            _toast.Show($"تماس با {row.Ticket.OwnerPhone}… (نمونه طراحی)", Tone.Info);
        }
    }

    [RelayCommand]
    private void ViewEncounter() => _toast.Show("پرونده ویزیت باز شد (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void FilterAdvanced() => _toast.Show("فیلتر پیشرفته به‌زودی (نمونه طراحی)", Tone.Warn);

    [RelayCommand]
    private void OpenWalkInModal()
    {
        var doctors = Queue
            .GroupBy(x => x.DoctorId)
            .Select(x => x.First())
            .Select(x => new ReceptionDoctor(x.DoctorId, x.DoctorName, x.DoctorSpecialty))
            .ToArray();
        _dialog.ShowModal(new WalkInModalViewModel(doctors, SubmitWalkInAsync, _dialog));
    }

    [RelayCommand]
    private async Task SubmitWalkInAsync(WalkInRequest? request)
    {
        if (request is null)
        {
            return;
        }

        await _queueService.CreateWalkInAsync(request);
        _toast.Show($"مراجعهٔ «{(string.IsNullOrWhiteSpace(request.Name) ? "بدون‌نام" : request.Name.Trim())}» ثبت شد (نمونه طراحی)", request.IsUrgent ? Tone.Danger : Tone.Ok);
        PageNumber = 1;
        _activeFilter = "all";
        await RefreshAsync();
    }

    [RelayCommand]
    private void SetFilter(string? filter)
    {
        _activeFilter = filter is "active" or "urgent" or "done" ? filter : "all";
        PageNumber = 1;
        ApplyFilter();
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsUrgentFilter));
        OnPropertyChanged(nameof(IsDoneFilter));
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            PageNumber--;
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNext)
        {
            PageNumber++;
            ApplyFilter();
        }
    }

    private async Task MutateAsync(
        ReceptionQueueRowViewModel? row,
        Func<string, Task> action,
        string message,
        Tone tone = Tone.Ok)
    {
        if (row is null)
        {
            return;
        }

        await action(row.Ticket.Id);
        _toast.Show(message, tone);
        await RefreshAsync();
    }

    private void ApplyFilter()
    {
        var filtered = FilterQueue();
        _filteredCount = filtered.Count;

        var maxPage = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        if (PageNumber > maxPage)
        {
            PageNumber = maxPage;
        }

        var start = (PageNumber - 1) * PageSize;

        // Batched rebuild (PERFORMANCE.md §5): a single Reset via
        // RangeObservableCollection.ReplaceAll instead of Clear()+Add in a
        // loop, which raised one CollectionChanged per row. Row numbering
        // (start + index + 1) is unchanged.
        VisibleQueue.ReplaceAll(filtered.Skip(start).Take(PageSize)
            .Select((ticket, index) => new ReceptionQueueRowViewModel(ticket, start + index + 1)));

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TotalCount));
        NotifyPageState();
    }

    private List<QueueTicket> FilterQueue()
    {
        IEnumerable<QueueTicket> result = _activeFilter switch
        {
            "active" => Queue.Where(x => IsActive(x.State)),
            "urgent" => Queue.Where(x => x.IsUrgent && IsActive(x.State)),
            "done" => Queue.Where(x => x.State == QueueTicketState.Done),
            _ => Queue,
        };

        var query = SearchText.Trim();
        if (query.Length > 0)
        {
            result = result.Where(x =>
                x.PatientName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.OwnerName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.DoctorName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        return result.OrderByDescending(x => x.IsUrgent && IsActive(x.State))
            .ThenBy(x => x.ArrivalTime)
            .ToList();
    }

    private int CalculateAverageWait()
    {
        var active = Queue.Where(x => IsActive(x.State)).ToArray();
        return active.Length == 0
            ? 0
            : (int)Math.Round(active.Average(x => Math.Max(0, (DateTimeOffset.Now - x.ArrivalTime).TotalMinutes)));
    }

    private string Count(QueueTicketState state) => PersianNumberFormatter.ToPersianDigits(Queue.Count(x => x.State == state));

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(CalledCount));
        OnPropertyChanged(nameof(InVisitCount));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(UrgentCount));
        OnPropertyChanged(nameof(AverageWaitText));
        OnPropertyChanged(nameof(DoneCountLine));
        OnPropertyChanged(nameof(UrgentCountLine));
        OnPropertyChanged(nameof(AverageWaitLine));
        OnPropertyChanged(nameof(NextPatientText));
    }

    private void NotifyPageState()
    {
        OnPropertyChanged(nameof(PageNumberText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private static bool IsActive(QueueTicketState state) =>
        state is QueueTicketState.Waiting or QueueTicketState.Called or QueueTicketState.InVisit;
}
