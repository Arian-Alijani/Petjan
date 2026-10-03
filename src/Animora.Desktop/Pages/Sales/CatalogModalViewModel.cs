using System.Collections.ObjectModel;
using System.Globalization;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the «افزودن خدمت یا کالا» catalog picker (mirrors the reference's
/// <c>openCatalog</c>/<c>renderCatalog</c> drawer): the search box with
/// highlight-on-search (<c>highlight</c>/<c>updateHl</c>), the category filter
/// chips with counts (<c>renderCatFilters</c>), the recent-items strip
/// (<c>renderCatRecent</c>) and the dense scannable rows, each adding a line to
/// the current invoice (<c>addToInvoice</c>) through
/// <see cref="ISalesQueryService.AddLineAsync"/>.
/// </summary>
public sealed partial class CatalogModalViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _invoiceId;
    private readonly Action? _onChanged;

    private readonly List<CatalogItem> _all = [];
    private readonly List<CatalogFilterRef> _filterRefs = [];
    private readonly List<RecentItemRef> _recent = [];
    private readonly List<CatalogItem> _visible = [];

    private string _filterKey = "all";

    public CatalogModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string invoiceId,
        Action? onChanged = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _invoiceId = invoiceId;
        _onChanged = onChanged;

        _ = LoadAsync();
    }

    public RangeObservableCollection<CatalogFilterViewModel> Filters { get; } = [];

    public RangeObservableCollection<CatalogRecentChipViewModel> Recent { get; } = [];

    public RangeObservableCollection<CatalogRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _hasRows = true;

    public bool HasRecent => Recent.Count > 0 && Query.Trim().Length == 0;

    public bool HasFilters => Filters.Count > 0;

    public int HighlightIndex { get; private set; }

    private async Task LoadAsync()
    {
        var snapshot = await _service.GetCatalogAsync();
        _all.Clear();
        _all.AddRange(snapshot.Items);

        _filterRefs.Clear();
        _filterRefs.AddRange(snapshot.Filters);

        _recent.Clear();
        _recent.AddRange(snapshot.Recent);

        BuildFilters();
        Render();
    }

    private void BuildFilters()
    {
        Filters.ReplaceAll(
            _filterRefs.Select(f => new CatalogFilterViewModel(f) { IsActive = f.Key == _filterKey }));

        OnPropertyChanged(nameof(HasFilters));
    }

    [RelayCommand]
    private void SetFilter(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _filterKey = key;
        HighlightIndex = 0;
        foreach (var f in Filters)
        {
            f.IsActive = f.Key == key;
        }

        Render();
    }

    private void Render()
    {
        var q = Query.Trim();
        var norm = PersianText.Normalize(q);

        IEnumerable<CatalogItem> list = _all;
        if (_filterKey is "svc" or "item")
        {
            list = list.Where(x => x.Kind == _filterKey);
        }
        else if (_filterKey != "all")
        {
            list = list.Where(x => x.GroupKey == _filterKey);
        }

        if (norm.Length > 0)
        {
            list = list.Where(x => PersianText.Contains(PersianText.Normalize(x.Title), norm)
                                || PersianText.Contains(PersianText.Normalize(x.GroupLabel), norm));
        }

        _visible.Clear();
        _visible.AddRange(list);

        var built = new List<CatalogRowViewModel>(_visible.Count);
        for (var i = 0; i < _visible.Count; i++)
        {
            var (bg, fg) = SalePresentation.LineTagBrushes(
                _visible[i].Kind == "svc" ? InvoiceLineKind.Service : InvoiceLineKind.Item, false);
            built.Add(new CatalogRowViewModel(_visible[i], bg, fg, q)
            {
                IsHighlighted = i == HighlightIndex,
            });
        }

        Rows.ReplaceAll(built);

        HasRows = Rows.Count > 0;
        var chips = new List<CatalogRecentChipViewModel>();
        if (norm.Length == 0)
        {
            foreach (var r in _recent)
            {
                var item = _all.FirstOrDefault(x => x.Kind == r.Kind && x.Id == r.Ref);
                if (item is not null)
                {
                    chips.Add(new CatalogRecentChipViewModel(item));
                }
            }
        }

        Recent.ReplaceAll(chips);

        OnPropertyChanged(nameof(HasRecent));
        CountText = Rows.Count > 0 ? $"{SalePresentation.Fa(Rows.Count)} نتیجه" : string.Empty;
    }

    [ObservableProperty]
    private string _countText = string.Empty;

    partial void OnQueryChanged(string value)
    {
        HighlightIndex = 0;
        Render();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (_visible.Count == 0)
        {
            return;
        }

        HighlightIndex = Math.Max(0, HighlightIndex - 1);
        UpdateHighlight();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (_visible.Count == 0)
        {
            return;
        }

        HighlightIndex = Math.Min(_visible.Count - 1, HighlightIndex + 1);
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].IsHighlighted = i == HighlightIndex;
        }
    }

    [RelayCommand]
    private async Task AddAsync(string? id)
    {
        var item = _all.FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }

        try
        {
            await _service.AddLineAsync(_invoiceId, item, 1);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                    ex, "افزودن آیتم ناموفق بود؛ دوباره تلاش کنید"),
                Tone.Danger);
            return;
        }

        _toast.Show($"«{item.Title}» به فاکتور افزوده شد", Tone.Ok);
        _onChanged?.Invoke();
        PromoteRecent(item);
    }

    [RelayCommand]
    private async Task AddHighlightedAsync()
    {
        if (HighlightIndex >= 0 && HighlightIndex < _visible.Count)
        {
            await AddAsync(_visible[HighlightIndex].Id);
        }
    }

    /// <summary>
    /// Moves the just-added item to the front of the recent strip locally (X23): the full catalog is not
    /// re-pulled per add, and only the small recent chip list is rebuilt, not the whole row list.
    /// </summary>
    private void PromoteRecent(CatalogItem item)
    {
        _recent.RemoveAll(r => r.Kind == item.Kind && r.Ref == item.Id);
        _recent.Insert(0, new RecentItemRef(item.Kind, item.Id));
        const int maxRecent = 10;
        if (_recent.Count > maxRecent)
        {
            _recent.RemoveRange(maxRecent, _recent.Count - maxRecent);
        }

        if (Query.Trim().Length > 0)
        {
            return;
        }

        Recent.ReplaceAll(_recent
            .Select(r => _all.FirstOrDefault(x => x.Kind == r.Kind && x.Id == r.Ref))
            .Where(x => x is not null)
            .Select(x => new CatalogRecentChipViewModel(x!)));
        OnPropertyChanged(nameof(HasRecent));
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();
}

/// <summary>One category filter chip (mirrors <c>.cat-filter</c>).</summary>
public sealed partial class CatalogFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public CatalogFilterViewModel(CatalogFilterRef reference)
    {
        Key = reference.Key;
        Label = reference.Label;
        CountText = SalePresentation.Fa(reference.Count);
    }

    public string Key { get; }

    public string Label { get; }

    public string CountText { get; }
}

/// <summary>One recent-item chip (mirrors <c>.cat-recent-chip</c>).</summary>
public sealed class CatalogRecentChipViewModel
{
    public CatalogRecentChipViewModel(CatalogItem item)
    {
        Id = item.Id;
        Title = item.Title;
        IconKey = item.IconKey;
        IsItem = item.Kind == "item";
        (IcoBackground, IcoForeground) = SalePresentation.LineTagBrushes(
            IsItem ? InvoiceLineKind.Item : InvoiceLineKind.Service, false);
    }

    public string Id { get; }

    public string Title { get; }

    public string IconKey { get; }

    public bool IsItem { get; }

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }
}

/// <summary>
/// One catalog row (mirrors <c>.cat-row</c>): kind-tinted icon tile, the
/// highlight-split name, the kind + group caption, the price and the add button.
/// </summary>
public sealed partial class CatalogRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isHighlighted;

    public CatalogRowViewModel(CatalogItem item, IBrush icoBackground, IBrush icoForeground, string query)
    {
        Id = item.Id;
        IconKey = item.IconKey;
        GroupLabel = item.GroupLabel;
        IsItem = item.Kind == "item";
        KindLabel = IsItem ? "کالا" : "خدمت";
        PriceText = SalePresentation.Money(item.Price);
        IcoBackground = icoBackground;
        IcoForeground = icoForeground;

        // Highlight-on-search: split the title into before / match / after. The match is located with the
        // same folding the filter uses (Persian digits, Arabic yeh/kaf, case) on a length-preserving copy,
        // so every row the filter keeps highlights the right span of the original title (X24).
        var match = PersianText.Fold(query.Trim());
        var idx = match.Length == 0
            ? -1
            : PersianText.Fold(item.Title).IndexOf(match, StringComparison.Ordinal);

        if (idx >= 0)
        {
            PreText = item.Title[..idx];
            MatchText = item.Title.Substring(idx, match.Length);
            PostText = item.Title[(idx + match.Length)..];
            HasMatch = true;
        }
        else
        {
            PreText = item.Title;
            MatchText = string.Empty;
            PostText = string.Empty;
            HasMatch = false;
        }
    }

    public string Id { get; }

    public string IconKey { get; }

    public string GroupLabel { get; }

    public bool IsItem { get; }

    public string KindLabel { get; }

    public string PriceText { get; }

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }

    public string PreText { get; }

    public string MatchText { get; }

    public string PostText { get; }

    public bool HasMatch { get; }
}
