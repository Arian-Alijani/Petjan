using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>One display row of the sent-SMS table (all formatting resolved here, never in XAML).</summary>
public sealed record SentSmsDisplayRow(string Id, string When, string EventName, string EventIcon, string Recipient,
    string Phone, string Body, string Segments, string StateLabel, Tone StateTone);

/// <summary>One option of the event-type filter (null = all types).</summary>
public sealed record SmsEventFilterOption(SmsEventType? EventType, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// «گزارش» section: the server-side paged table of sent SMS for the page-wide range, filterable by event type. The
/// stub seam returns an empty page, which renders a clear empty state — never fabricated rows.
/// </summary>
public sealed partial class SmsReportViewModel(ISmsService service) : SmsSectionViewModel
{
    private static readonly int[] PageSizes = [25, 50, 100];

    private SmsReportRange _range = SmsReportRange.Default;
    private CancellationTokenSource? _pageCts;
    private int _page = 1;
    private int _totalCount;
    private bool _suppressFilterReload;

    public override SmsSection Section => SmsSection.Report;

    public override string Label => "گزارش ارسال";

    public override string IconKey => "IconList";

    public RangeObservableCollection<SentSmsDisplayRow> Rows { get; } = [];

    public IReadOnlyList<SmsEventFilterOption> EventFilters { get; } =
    [
        new(null, "همه بخش‌ها"),
        .. Enum.GetValues<SmsEventType>().Select(t => new SmsEventFilterOption(t, SmsFormat.EventName(t))),
    ];

    [ObservableProperty]
    private SmsEventFilterOption? _selectedEventFilter;

    [ObservableProperty]
    private int _pageSizeIndex;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isPaging;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrevPageCommand))]
    private bool _hasPrev;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private bool _hasNext;

    [ObservableProperty]
    private string _pageText = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    private int PageSize => PageSizes[Math.Clamp(PageSizeIndex, 0, PageSizes.Length - 1)];

    protected override async Task LoadCoreAsync(SmsReportRange range, CancellationToken ct)
    {
        _range = range;
        _page = 1;
        if (SelectedEventFilter is null)
        {
            _suppressFilterReload = true;
            await PublishOnUiAsync(() => SelectedEventFilter = EventFilters[0]);
            _suppressFilterReload = false;
        }

        var page = await FetchAsync(ct);
        await PublishOnUiAsync(() => Publish(page));
    }

    protected override void ReleaseHeavyData()
    {
        CancelPaging();
        Rows.Clear();
        HasRows = false;
        _page = 1;
        _totalCount = 0;
    }

    partial void OnSelectedEventFilterChanged(SmsEventFilterOption? value)
    {
        if (!_suppressFilterReload && HasData) _ = ReloadPageAsync(1);
    }

    partial void OnPageSizeIndexChanged(int value)
    {
        if (HasData) _ = ReloadPageAsync(1);
    }

    [RelayCommand(CanExecute = nameof(HasPrev))]
    private Task PrevPageAsync() => ReloadPageAsync(_page - 1);

    [RelayCommand(CanExecute = nameof(HasNext))]
    private Task NextPageAsync() => ReloadPageAsync(_page + 1);

    [RelayCommand]
    private void ResetFilter()
    {
        if (SelectedEventFilter?.EventType is null) return;
        SelectedEventFilter = EventFilters[0];
    }

    /// <summary>Filter / paging change inside the visible section: re-reads one page without touching the page-wide load.</summary>
    private async Task ReloadPageAsync(int page)
    {
        CancelPaging();
        var cts = _pageCts = new CancellationTokenSource();
        _page = Math.Max(1, page);
        IsPaging = true;
        try
        {
            var result = await FetchAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            await PublishOnUiAsync(() => Publish(result));
        }
        catch (OperationCanceledException)
        {
        }
        catch (SmsServiceException ex)
        {
            HasError = true;
            ErrorMessage = ex.UserMessage;
        }
        catch (Exception)
        {
            HasError = true;
            ErrorMessage = "بارگذاری گزارش پیامک انجام نشد؛ دوباره تلاش کنید.";
        }
        finally
        {
            if (_pageCts == cts) IsPaging = false;
        }
    }

    private Task<SentSmsPage> FetchAsync(CancellationToken ct) =>
        service.GetSentAsync(_range, SelectedEventFilter?.EventType, _page, PageSize, ct);

    private void Publish(SentSmsPage page)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        _page = Math.Max(1, page.Page);
        _totalCount = Math.Max(0, page.TotalCount);
        Rows.ReplaceAll(page.Rows.Select(ToRow));
        HasRows = Rows.Count > 0;

        var size = Math.Max(1, page.PageSize);
        var pages = Math.Max(1, (_totalCount + size - 1) / size);
        HasPrev = _page > 1;
        HasNext = _page < pages;
        PageText = $"صفحهٔ {SmsFormat.Digits(_page)} از {SmsFormat.Digits(pages)}";
        var segments = page.Rows.Sum(r => (long)r.SegmentCount);
        Summary = _totalCount == 0
            ? "پیامکی در این بازه ثبت نشده است"
            : $"{SmsFormat.Count(_totalCount)} پیامک · {SmsFormat.Count(segments)} بخش در این صفحه";
    }

    private static SentSmsDisplayRow ToRow(SentSmsRow r) => new(
        r.Id,
        SmsFormat.JalaliDateTime(r.WhenUtc),
        SmsFormat.EventName(r.EventType),
        SmsFormat.EventIcon(r.EventType),
        string.IsNullOrWhiteSpace(r.Recipient) ? "—" : r.Recipient,
        string.IsNullOrWhiteSpace(r.Phone) ? "—" : PersianNumberFormatter.ToPersianDigits(r.Phone),
        r.Body,
        SmsFormat.Digits(r.SegmentCount),
        SmsFormat.StateName(r.State),
        SmsFormat.StateTone(r.State));

    private void CancelPaging()
    {
        var cts = _pageCts;
        _pageCts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }
}
