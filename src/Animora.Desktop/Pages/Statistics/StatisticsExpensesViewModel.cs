using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Identity;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Domain.Identity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>
/// «هزینه‌ها»: the expense ledger (paged, category filter), per-category breakdown with previous-period comparison, the
/// expense trend and the «هزینه‌های ثابت» panel. Writes (add / reverse / pay fixed / add-edit-toggle fixed) go through the
/// service, then announce <see cref="DataDomain.Finance"/> so the page, the overview and the dashboard re-read once; a
/// failed write only toasts and leaves the screen untouched. Needs <c>finance.read</c> (tab hidden otherwise); the write
/// buttons additionally need <c>finance.post</c>.
/// </summary>
public sealed partial class StatisticsExpensesViewModel : StatisticsSectionViewModel
{
    private const int PageSize = 25;

    private readonly IStatisticsQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IDataChangeNotifier _changes;
    private StatisticsRange _range = new();
    private int _page;
    private int _listVersion;
    private bool _busy;

    public StatisticsExpensesViewModel(IStatisticsQueryService service, IToastService toast, IDialogService dialogs,
        IDataChangeNotifier changes, ICapabilityGate capabilities)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _changes = changes;
        CanPost = capabilities.Can(PermissionCodes.FinancePost);

        CategoryFilters.Add(new StatisticsChoice(string.Empty, "همه") { IsSelected = true });
        foreach (var code in StatisticsFormat.CategoryCodes)
            CategoryFilters.Add(new StatisticsChoice(code, StatisticsFormat.CategoryName(code), StatisticsFormat.CategoryColor(code)));
    }

    public override StatisticsSection Section => StatisticsSection.Expenses;

    public override string Label => "هزینه‌ها";

    /// <summary>The viewer may record / reverse / pay expenses (<c>finance.post</c>).</summary>
    public bool CanPost { get; }

    public RangeObservableCollection<StatisticsChoice> CategoryFilters { get; } = [];

    public RangeObservableCollection<StatisticsExpenseRowViewModel> Rows { get; } = [];

    public RangeObservableCollection<StatisticsCategoryRow> Categories { get; } = [];

    public RangeObservableCollection<StatisticsRecurringRowViewModel> Recurring { get; } = [];

    [ObservableProperty]
    private string _total = string.Empty;

    [ObservableProperty]
    private string _previousTotal = string.Empty;

    [ObservableProperty]
    private string _delta = string.Empty;

    [ObservableProperty]
    private bool _isDeltaPositive = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DonutSlice> _categorySlices = [];

    [ObservableProperty]
    private IReadOnlyList<BarChartPoint> _trend = [];

    [ObservableProperty]
    private bool _hasCategories;

    [ObservableProperty]
    private bool _hasTrend;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _isListLoading;

    [ObservableProperty]
    private string _listSummary = string.Empty;

    [ObservableProperty]
    private bool _hasRecurring;

    [ObservableProperty]
    private string _recurringSummary = string.Empty;

    [ObservableProperty]
    private bool _hasRecurringAttention;

    private string? SelectedCategory => CategoryFilters.FirstOrDefault(c => c.IsSelected) is { Key.Length: > 0 } c ? c.Key : null;

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        _range = range;
        var statsTask = _service.GetExpenseStatsAsync(range, ct);
        var pageTask = _service.GetExpensesAsync(range, SelectedCategory, 1, PageSize, ct);
        var recurringTask = _service.GetRecurringExpensesAsync(ct);
        await Task.WhenAll(statsTask, pageTask, recurringTask);

        PublishStats(statsTask.Result);
        PublishPage(pageTask.Result, append: false);
        PublishRecurring(recurringTask.Result);
        IsEmpty = false; // the screen always offers «ثبت هزینه» and the fixed-expense panel, even with an empty ledger
    }

    private void PublishStats(ExpenseStats stats)
    {
        Total = StatisticsFormat.Money(stats.TotalMinor);
        PreviousTotal = StatisticsFormat.Money(stats.PreviousTotalMinor);
        (Delta, IsDeltaPositive) = StatisticsFormat.Delta(stats.TotalMinor, stats.PreviousTotalMinor, higherIsBetter: false);
        CountText = StatisticsFormat.Count(stats.Categories.Sum(c => c.Count));

        var cats = stats.Categories.Where(c => c.AmountMinor > 0).OrderByDescending(c => c.AmountMinor).ToArray();
        var sum = cats.Sum(c => c.AmountMinor);
        CategorySlices = cats.Select(c => new DonutSlice(c.AmountMinor, StatisticsFormat.CategoryColor(c.CategoryCode))).ToArray();
        Categories.ReplaceAll(cats.Select(c =>
        {
            var (delta, up) = StatisticsFormat.Delta(c.AmountMinor, c.PreviousAmountMinor, higherIsBetter: false);
            return new StatisticsCategoryRow(StatisticsFormat.CategoryName(c.CategoryCode), StatisticsFormat.Money(c.AmountMinor),
                StatisticsFormat.Percent(c.AmountMinor, sum), delta, up, sum == 0 ? 0 : c.AmountMinor / (double)sum,
                StatisticsFormat.Brush(StatisticsFormat.CategoryColor(c.CategoryCode)));
        }));
        HasCategories = cats.Length > 0;

        var monthly = StatisticsFormat.IsMonthly(stats.Trend);
        Trend = stats.Trend.Select(p => new BarChartPoint(p.ExpenseMinor, StatisticsFormat.BucketLabel(p.Start, monthly),
            $"{StatisticsFormat.BucketLabel(p.Start, monthly)} · {StatisticsFormat.Money(p.ExpenseMinor)}")).ToArray();
        HasTrend = stats.Trend.Any(p => p.ExpenseMinor != 0);
    }

    private void PublishPage(ExpensePage page, bool append)
    {
        var items = page.Items.Select(r => new StatisticsExpenseRowViewModel(r)).ToArray();
        if (append) Rows.AddRange(items);
        else Rows.ReplaceAll(items);
        _page = page.Page;
        HasMore = page.HasMore;
        HasRows = Rows.Count > 0;
        ListSummary = page.TotalCount == 0 ? string.Empty
            : $"{StatisticsFormat.Count(Rows.Count)} از {StatisticsFormat.Count(page.TotalCount)} مورد";
    }

    private void PublishRecurring(RecurringExpenseList list)
    {
        Recurring.ReplaceAll(list.Items.Select(i => new StatisticsRecurringRowViewModel(i)));
        HasRecurring = list.Items.Count > 0;
        var attention = list.Items.Count(i => i.NeedsAttention);
        HasRecurringAttention = attention > 0;
        RecurringSummary = attention > 0 ? $"{StatisticsFormat.Count(attention)} مورد نیازمند پرداخت" : string.Empty;
    }

    // ── ledger filter / paging (re-query the list only) ─────────────────────────────

    [RelayCommand]
    private async Task SelectCategoryAsync(StatisticsChoice? choice)
    {
        if (choice is null || choice.IsSelected) return;
        foreach (var c in CategoryFilters) c.IsSelected = ReferenceEquals(c, choice);
        await ReloadListAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsListLoading || !HasMore) return;
        var version = _listVersion;
        IsListLoading = true;
        try
        {
            var page = await _service.GetExpensesAsync(_range, SelectedCategory, _page + 1, PageSize);
            if (version == _listVersion) PublishPage(page, append: true);
        }
        catch (StatisticsQueryException ex)
        {
            _toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task ReloadListAsync()
    {
        var version = ++_listVersion;
        IsListLoading = true;
        try
        {
            var page = await _service.GetExpensesAsync(_range, SelectedCategory, 1, PageSize);
            if (version == _listVersion) PublishPage(page, append: false);
        }
        catch (StatisticsQueryException ex)
        {
            _toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            if (version == _listVersion) IsListLoading = false;
        }
    }

    // ── writes ──────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddExpense()
    {
        if (!CanPost) return;
        _dialogs.ShowModal(new StatisticsExpenseModalViewModel(_service, _toast, _dialogs, OnWritten), maxWidth: 560);
    }

    [RelayCommand]
    private void ReverseExpense(StatisticsExpenseRowViewModel? row)
    {
        if (!CanPost || row is null || !row.CanReverse) return;
        _dialogs.ShowModal(new StatisticsReverseModalViewModel(_service, _toast, _dialogs, row, OnWritten), maxWidth: 520);
    }

    [RelayCommand]
    private void AddRecurring()
    {
        if (!CanPost) return;
        _dialogs.ShowModal(new StatisticsRecurringModalViewModel(_service, _toast, _dialogs, null, OnWritten), maxWidth: 560);
    }

    [RelayCommand]
    private void EditRecurring(StatisticsRecurringRowViewModel? row)
    {
        if (!CanPost || row is null) return;
        _dialogs.ShowModal(new StatisticsRecurringModalViewModel(_service, _toast, _dialogs, row.Item, OnWritten), maxWidth: 560);
    }

    [RelayCommand]
    private Task PayRecurringAsync(StatisticsRecurringRowViewModel? row) => RunWriteAsync(row is { CanPay: true } && CanPost,
        async () =>
        {
            await _service.PayRecurringExpenseAsync(row!.Item.Id);
            return $"«{row.Title}» پرداخت و در دفتر هزینه‌ها ثبت شد";
        });

    [RelayCommand]
    private Task ToggleRecurringAsync(StatisticsRecurringRowViewModel? row) => RunWriteAsync(row is not null && CanPost,
        async () =>
        {
            await _service.SetRecurringExpenseActiveAsync(row!.Item.Id, !row.IsActive);
            return row.IsActive ? $"«{row.Title}» غیرفعال شد" : $"«{row.Title}» فعال شد";
        });

    /// <summary>Runs one write with a re-entrancy guard; success toasts + announces, failure only toasts.</summary>
    private async Task RunWriteAsync(bool allowed, Func<Task<string>> write)
    {
        if (!allowed || _busy) return;
        _busy = true;
        try
        {
            var message = await write();
            OnWritten(message);
        }
        catch (StatisticsQueryException ex)
        {
            _toast.Show(ex.UserMessage, Tone.Danger);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnWritten(string message)
    {
        _toast.Show(message, Tone.Ok);
        // The page re-reads once on this announcement (and so do the overview and the dashboard).
        _changes.Notify(DataDomain.Finance, DataDomain.Dashboard);
    }
}
