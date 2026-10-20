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
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onChanged;

    // Optional «بازگشت» target (e.g. boarding re-opens the stay-detail modal); null = plain close.
    private readonly Action? _onBack;

    // Destination noun shown in the success toast («به فاکتور / به اقلام اقامت افزوده شد»).
    private readonly string _addedToLabel;

    // The two seams that make this picker reusable: how to load the catalog and
    // how to add a chosen item. The Sales/Cashier path binds these to
    // ISalesQueryService (GetCatalogAsync / AddLineAsync into an invoice); the
    // visit flow binds them to IVisitEncounterService (its own catalog + the
    // encounter's rx list) so the exact same drawer serves both surfaces.
    private readonly Func<Task<CatalogSnapshot>> _loadCatalog;
    private readonly Func<CatalogItem, Task> _addItem;

    private readonly List<CatalogItem> _all = [];
    private readonly List<CatalogFilterRef> _filterRefs = [];
    private readonly List<RecentItemRef> _recent = [];
    private readonly List<CatalogItem> _visible = [];

    // Units already added to the destination (invoice / stay / encounter) during this open session,
    // per catalog item id. Combined with the item's on-hand figure this caps how many of a
    // stock-tracked good can be added before the picker reloads with a fresh on-hand snapshot, so a
    // 2-in-stock product can never be added more than twice from a single open drawer.
    private readonly Dictionary<string, int> _sessionAdded = [];

    // Non-null = «pick one item» mode (e.g. choosing a stock item for a medication reminder): no price, the
    // drawer closes after the pick and nothing is added to any invoice.
    private readonly CatalogPickerOptions? _picker;

    private string _filterKey;
    private int _loadGeneration;

    /// <summary>
    /// Sales/Cashier constructor: the picker loads the sales catalog and adds a
    /// chosen row to the open invoice through <see cref="ISalesQueryService"/>.
    /// </summary>
    public CatalogModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string invoiceId,
        Action? onChanged = null)
        : this(
            service.GetCatalogAsync,
            item => service.AddLineAsync(invoiceId, item, 1),
            toast,
            dialogs,
            onChanged)
    {
    }

    /// <summary>
    /// Reusable constructor: the picker is driven entirely by the supplied
    /// <paramref name="loadCatalog"/> / <paramref name="addItem"/> seams, so any
    /// surface (sales invoice, visit encounter, …) can reuse the identical
    /// drawer UI. <paramref name="initialFilter"/> pre-selects a category chip
    /// (<c>"svc"</c> for services, <c>"item"</c> for goods, <c>"all"</c> for the
    /// whole catalog). <paramref name="onBack"/> replaces the plain close with a
    /// custom «بازگشت» action; <paramref name="addedToLabel"/> names the target in the toast.
    /// </summary>
    public CatalogModalViewModel(
        Func<Task<CatalogSnapshot>> loadCatalog,
        Func<CatalogItem, Task> addItem,
        IToastService toast,
        IDialogService dialogs,
        Action? onChanged = null,
        string initialFilter = "all",
        Action? onBack = null,
        string addedToLabel = "فاکتور",
        CatalogPickerOptions? picker = null)
    {
        _picker = picker;
        Title = picker?.Title ?? "افزودن خدمت یا کالا";
        SearchPlaceholder = picker?.SearchPlaceholder ?? "جستجوی نام، دستهٔ خدمت یا کالا…";
        FooterHint = picker?.FooterHint
                     ?? "می‌تونی چند بار کلیک کنی تا تعداد بیشتری اضافه بشه — یا از داخل فاکتور تعداد رو تنظیم کن.";
        ShowPrice = picker?.ShowPrice ?? true;
        _onBack = onBack;
        _addedToLabel = addedToLabel;
        _loadCatalog = loadCatalog;
        _addItem = addItem;
        _toast = toast;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _filterKey = string.IsNullOrEmpty(initialFilter) ? "all" : initialFilter;

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری کاتالوگ ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public string Title { get; }

    public string SearchPlaceholder { get; }

    public string FooterHint { get; }

    public bool ShowPrice { get; }

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

    /// <summary>Refresh the open picker after local writes or a peer sync, without losing the search/filter.</summary>
    public async Task RefreshStockAsync()
    {
        try { await LoadAsync(); }
        catch (Exception) { _toast.Show("به‌روزرسانی موجودی ناموفق بود؛ دوباره تلاش کنید", Tone.Danger); }
    }

    public void CancelPendingRefresh() => _loadGeneration++;

    /// <summary>
    /// Whole units of <paramref name="item"/> still addable right now: its on-hand units minus whatever
    /// has already been added in this session. Untracked goods/services are unlimited. This is the single
    /// stock gate every add path (row, recent chip, keyboard «اینتر») consults.
    /// </summary>
    private int RemainingUnits(CatalogItem item)
    {
        if (!item.TracksStock)
        {
            return int.MaxValue;
        }

        var used = _sessionAdded.TryGetValue(item.Id, out var n) ? n : 0;
        return Math.Max(0, item.AvailableUnits - used);
    }

    /// <summary>True when at least one more unit of <paramref name="item"/> can be added.</summary>
    private bool CanAdd(CatalogItem item) => RemainingUnits(item) > 0;

    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        var snapshot = await _loadCatalog();
        if (generation != _loadGeneration) return;
        _all.Clear();
        _all.AddRange(snapshot.Items);

        // The reloaded snapshot already reflects every persisted add, so the per-session tally resets:
        // remaining availability is read straight off the fresh on-hand figures again.
        _sessionAdded.Clear();

        _filterRefs.Clear();
        _filterRefs.AddRange(snapshot.Filters);

        _recent.Clear();
        _recent.AddRange(snapshot.Recent);

        _filterKey = ResolveEffectiveFilter(_filterKey);
        BuildFilters();
        Render();
    }

    /// <summary>
    /// Guards the opening chip: a pre-selected chip («خدمات» / «دارو و کالا» / a category) that matches no row of the
    /// freshly loaded catalog would open an empty drawer even though addable goods/services exist under another chip
    /// (e.g. «افزودن خدمت» when only goods were created). In that case fall back to «همه» so every active
    /// product/service stays visible and addable.
    /// </summary>
    private string ResolveEffectiveFilter(string key)
    {
        if (key == "all" || _all.Count == 0)
        {
            return key;
        }

        var hasRows = key is "svc" or "item"
            ? _all.Any(x => x.Kind == key)
            : _all.Any(x => x.GroupKey == key);
        return hasRows ? key : "all";
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
        HighlightIndex = Math.Clamp(HighlightIndex, 0, Math.Max(0, _visible.Count - 1));

        var built = new List<CatalogRowViewModel>(_visible.Count);
        for (var i = 0; i < _visible.Count; i++)
        {
            var (bg, fg) = SalePresentation.LineTagBrushes(
                _visible[i].Kind == "svc" ? InvoiceLineKind.Service : InvoiceLineKind.Item, false);
            built.Add(new CatalogRowViewModel(_visible[i], bg, fg, q, CanAdd(_visible[i]))
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
                    chips.Add(new CatalogRecentChipViewModel(item, CanAdd(item)));
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

        // Stock gate (applies on every add surface that uses this picker — invoice, stay, encounter, …):
        // a stock-tracked good can never be added beyond what is on hand. Once exhausted the add is
        // refused with a toast instead of silently stacking an unavailable unit onto the destination.
        if (!CanAdd(item))
        {
            _toast.Show(
                item.AvailableUnits <= 0
                    ? $"«{item.Title}» موجودی ندارد و قابل افزودن نیست"
                    : $"موجودی «{item.Title}» کافی نیست؛ همهٔ {SalePresentation.Fa(item.AvailableUnits)} عدد موجود افزوده شد",
                Tone.Danger);
            return;
        }

        try
        {
            await _addItem(item);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                    ex, "افزودن آیتم ناموفق بود؛ دوباره تلاش کنید"),
                Tone.Danger);
            return;
        }

        if (_picker is not null)
        {
            _toast.Show($"«{item.Title}» انتخاب شد", Tone.Ok);
            _onChanged?.Invoke();
            Close();
            return;
        }

        // Count this unit against the item's on-hand so the next add sees one fewer remaining, then
        // refresh the add-buttons so a row/chip greys out the moment its stock is exhausted.
        if (item.TracksStock)
        {
            _sessionAdded[item.Id] = (_sessionAdded.TryGetValue(item.Id, out var n) ? n : 0) + 1;
            RefreshAddability(item.Id);
        }

        _toast.Show($"«{item.Title}» به {_addedToLabel} افزوده شد", Tone.Ok);
        _onChanged?.Invoke();
        PromoteRecent(item);
    }

    /// <summary>Re-evaluates the stock gate for the given item on every visible row and recent chip.</summary>
    private void RefreshAddability(string itemId)
    {
        var item = _all.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return;
        }

        var canAdd = CanAdd(item);
        foreach (var row in Rows)
        {
            if (row.Id == itemId)
            {
                row.CanAdd = canAdd;
            }
        }

        foreach (var chip in Recent)
        {
            if (chip.Id == itemId)
            {
                chip.CanAdd = canAdd;
            }
        }
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
            .Select(x => new CatalogRecentChipViewModel(x!, CanAdd(x!))));
        OnPropertyChanged(nameof(HasRecent));
    }

    [RelayCommand]
    private void Close()
    {
        if (_onBack is null)
        {
            _dialogs.Close();
            return;
        }

        _onBack();
    }
}

/// <summary>
/// Switches the catalog drawer into «pick one item» mode: custom copy, no price column, and the drawer closes
/// (returning to the caller through its back action) as soon as an item is chosen.
/// </summary>
public sealed record CatalogPickerOptions(string Title, string SearchPlaceholder, string FooterHint, bool ShowPrice = false);

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
public sealed partial class CatalogRecentChipViewModel : ObservableObject
{
    public CatalogRecentChipViewModel(CatalogItem item, bool canAdd = true)
    {
        Id = item.Id;
        Title = item.Title;
        IconKey = item.IconKey;
        IsItem = item.Kind == "item";
        IsOutOfStock = item.IsOutOfStock;
        _canAdd = canAdd && !item.IsOutOfStock;
        (IcoBackground, IcoForeground) = SalePresentation.LineTagBrushes(
            IsItem ? InvoiceLineKind.Item : InvoiceLineKind.Service, false);
        if (IsOutOfStock)
            (IcoBackground, IcoForeground) = (SalePresentation.PlaceholderAvatarBrush(), SalePresentation.PlaceholderAvatarForeground());
    }

    public string Id { get; }

    public string Title { get; }

    public string IconKey { get; }

    public bool IsItem { get; }

    public bool IsOutOfStock { get; }

    /// <summary>False once the item is out of stock / fully consumed this session: the chip greys out and its tap is disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable))]
    private bool _canAdd;

    /// <summary>True when the chip must read as unavailable (grey style + disabled), covering both no-stock and exhausted-this-session.</summary>
    public bool IsUnavailable => IsOutOfStock || !CanAdd;

    public IBrush TextForeground => IsOutOfStock
        ? BrushCache.Solid("#667085") : BrushCache.Solid("#101828");

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

    public CatalogRowViewModel(CatalogItem item, IBrush icoBackground, IBrush icoForeground, string query, bool canAdd = true)
    {
        Id = item.Id;
        IconKey = item.IconKey;
        GroupLabel = item.GroupLabel;
        IsItem = item.Kind == "item";
        KindLabel = IsItem ? "کالا" : "خدمت";
        PriceText = SalePresentation.Money(item.Price);
        IsOutOfStock = item.IsOutOfStock;
        _canAdd = canAdd && !item.IsOutOfStock;
        IcoBackground = IsOutOfStock ? SalePresentation.PlaceholderAvatarBrush() : icoBackground;
        IcoForeground = IsOutOfStock ? SalePresentation.PlaceholderAvatarForeground() : icoForeground;

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

    public bool IsOutOfStock { get; }

    /// <summary>False once the item is out of stock / fully consumed this session: the row greys out and its tap is disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable))]
    [NotifyPropertyChangedFor(nameof(AddBackground))]
    private bool _canAdd;

    /// <summary>True when the row must read as unavailable (grey style + disabled), covering both no-stock and exhausted-this-session.</summary>
    public bool IsUnavailable => IsOutOfStock || !CanAdd;

    public IBrush TextForeground => IsOutOfStock
        ? BrushCache.Solid("#667085") : BrushCache.Solid("#101828");

    public string KindLabel { get; }

    public string PriceText { get; }

    public IBrush AddBackground => IsUnavailable
        ? SalePresentation.PlaceholderAvatarForeground() : BrushCache.Solid("#0e9384");

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }

    public string PreText { get; }

    public string MatchText { get; }

    public string PostText { get; }

    public bool HasMatch { get; }
}
