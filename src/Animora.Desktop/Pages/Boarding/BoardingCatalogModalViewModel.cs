using System.Globalization;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// The boarding «افزودن خدمت یا کالا» picker. Mirrors the Sales catalog modal
/// (<see cref="Animora.Desktop.Pages.Sales.CatalogModalViewModel"/>) exactly in look and behavior:
/// the search box with highlight-on-search, the source filter chips with counts, the recent-items
/// strip and the dense scannable rows. A row (or recent chip) click adds the item to the stay/room
/// through <see cref="IBoardingQueryService.AddItemAsync"/>; the modal stays open so several lines can
/// be added (click again to add more), and «بستن» returns to the stay-detail modal.
/// </summary>
public sealed partial class BoardingCatalogModalViewModel : ObservableObject
{
    private const string AllKey = "all";

    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _stayId;
    private readonly string _petName;
    private readonly Action? _onChanged;
    private readonly Action? _onBack;

    private readonly List<BoardingSelectableItem> _all = [];
    private readonly List<BoardingSelectableItem> _visible = [];
    private readonly List<string> _recent = [];
    private string _filterKey = AllKey;
    private bool _isAdding;

    public BoardingCatalogModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string stayId,
        string petName,
        Action? onChanged = null,
        Action? onBack = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _stayId = stayId;
        _petName = petName;
        _onChanged = onChanged;
        _onBack = onBack;

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری فهرست خدمات و کالاها ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public RangeObservableCollection<BoardingCatalogFilterViewModel> Filters { get; } = [];

    public RangeObservableCollection<BoardingCatalogRecentChipViewModel> Recent { get; } = [];

    public RangeObservableCollection<BoardingCatalogRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _hasRows = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    public bool HasRecent => Recent.Count > 0 && Query.Trim().Length == 0;

    public bool HasFilters => Filters.Count > 0;

    public int HighlightIndex { get; private set; }

    private async Task LoadAsync()
    {
        var items = await _service.GetSelectableItemsAsync();
        _all.Clear();
        _all.AddRange(items);
        BuildFilters();
        Render();
    }

    private void BuildFilters()
    {
        var chips = new List<BoardingCatalogFilterViewModel>
        {
            new(AllKey, "همه", _all.Count) { IsActive = _filterKey == AllKey },
        };

        foreach (var source in new[]
                 {
                     BoardingItemSource.BoardingService,
                     BoardingItemSource.ClinicService,
                     BoardingItemSource.ClinicItem,
                 })
        {
            var count = _all.Count(x => x.Source == source);
            if (count == 0)
            {
                continue;
            }

            var key = source.ToString();
            chips.Add(new BoardingCatalogFilterViewModel(key, SourceLabel(source), count) { IsActive = _filterKey == key });
        }

        Filters.ReplaceAll(chips);
        OnPropertyChanged(nameof(HasFilters));
    }

    /// <summary>Persian group label of a catalog source (reference optgroups).</summary>
    internal static string SourceLabel(BoardingItemSource source) => source switch
    {
        BoardingItemSource.BoardingService => "خدمات پانسیون",
        BoardingItemSource.ClinicService => "خدمات کلینیک",
        _ => "کالاها",
    };

    private static string IconFor(BoardingItemSource source) => source switch
    {
        BoardingItemSource.BoardingService => "IconHome",
        BoardingItemSource.ClinicService => "IconSyringe",
        _ => "IconBox",
    };

    [RelayCommand]
    private void SetFilter(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _filterKey = key;
        HighlightIndex = 0;
        foreach (var filter in Filters)
        {
            filter.IsActive = filter.Key == key;
        }

        Render();
    }

    partial void OnQueryChanged(string value)
    {
        HighlightIndex = 0;
        Render();
    }

    private void Render()
    {
        var q = Query.Trim();
        var norm = PersianText.Normalize(q);

        IEnumerable<BoardingSelectableItem> list = _all;
        if (_filterKey != AllKey)
        {
            list = list.Where(x => x.Source.ToString() == _filterKey);
        }

        if (norm.Length > 0)
        {
            list = list.Where(x => PersianText.Contains(PersianText.Normalize(x.Title), norm)
                                || PersianText.Contains(PersianText.Normalize(SourceLabel(x.Source)), norm));
        }

        _visible.Clear();
        _visible.AddRange(list);

        var built = new List<BoardingCatalogRowViewModel>(_visible.Count);
        for (var i = 0; i < _visible.Count; i++)
        {
            var row = BuildRow(_visible[i], q);
            row.IsHighlighted = i == HighlightIndex;
            built.Add(row);
        }

        Rows.ReplaceAll(built);

        HasRows = Rows.Count > 0;

        // Recent strip: only shown when not searching (matches Sales). It is seeded empty and promoted
        // locally as items are added, so it tracks this session's picks without re-pulling the catalog.
        var chips = new List<BoardingCatalogRecentChipViewModel>();
        if (norm.Length == 0)
        {
            foreach (var reference in _recent)
            {
                var item = _all.FirstOrDefault(x => x.Ref == reference);
                if (item is not null)
                {
                    chips.Add(BuildChip(item));
                }
            }
        }

        Recent.ReplaceAll(chips);
        OnPropertyChanged(nameof(HasRecent));

        CountText = Rows.Count > 0 ? $"{BoardingPresentation.Fa(Rows.Count)} نتیجه" : string.Empty;
    }

    private static BoardingItemKind KindOf(BoardingSelectableItem item) =>
        item.Source == BoardingItemSource.ClinicItem ? BoardingItemKind.Item : BoardingItemKind.Service;

    private BoardingCatalogRowViewModel BuildRow(BoardingSelectableItem item, string query)
    {
        var kind = KindOf(item);
        var (bg, fg) = BoardingPresentation.ItemTagBrushes(kind);
        return new BoardingCatalogRowViewModel(item, SourceLabel(item.Source), IconFor(item.Source),
            BoardingPresentation.ItemKindLabel(kind), bg, fg, query);
    }

    private BoardingCatalogRecentChipViewModel BuildChip(BoardingSelectableItem item)
    {
        var (bg, fg) = BoardingPresentation.ItemTagBrushes(KindOf(item));
        return new BoardingCatalogRecentChipViewModel(item, IconFor(item.Source), bg, fg);
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
    private async Task AddAsync(string? reference)
    {
        var item = _all.FirstOrDefault(x => x.Ref == reference);
        if (item is null || _isAdding)
        {
            return;
        }

        _isAdding = true;
        try
        {
            await _service.AddItemAsync(_stayId, item.Ref, 1);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                PatientsPresentation.CommandFailureMessage(ex, "افزودن مورد ناموفق بود؛ دوباره تلاش کنید"),
                Tone.Danger);
            return;
        }
        finally
        {
            _isAdding = false;
        }

        _toast.Show($"«{item.Title}» به اقلام «{_petName}» اضافه شد", Tone.Ok);
        _onChanged?.Invoke();
        PromoteRecent(item);
    }

    [RelayCommand]
    private async Task AddHighlightedAsync()
    {
        if (HighlightIndex >= 0 && HighlightIndex < _visible.Count)
        {
            await AddAsync(_visible[HighlightIndex].Ref);
        }
    }

    /// <summary>
    /// Moves the just-added item to the front of the recent strip locally: the full catalog is not
    /// re-pulled per add, and only the small recent chip list is rebuilt, not the whole row list.
    /// </summary>
    private void PromoteRecent(BoardingSelectableItem item)
    {
        _recent.RemoveAll(r => r == item.Ref);
        _recent.Insert(0, item.Ref);
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
            .Select(r => _all.FirstOrDefault(x => x.Ref == r))
            .Where(x => x is not null)
            .Select(x => BuildChip(x!)));
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>Returns to the stay-detail modal (which re-reads the stay so the new lines show up).</summary>
    [RelayCommand]
    private void Back()
    {
        if (_onBack is null)
        {
            _dialogs.Close();
            return;
        }

        _onBack();
    }
}

/// <summary>One source filter chip of the boarding catalog modal (mirrors Sales <c>.cat-filter</c>).</summary>
public sealed partial class BoardingCatalogFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public BoardingCatalogFilterViewModel(string key, string label, int count)
    {
        Key = key;
        Label = label;
        CountText = BoardingPresentation.Fa(count);
    }

    public string Key { get; }

    public string Label { get; }

    public string CountText { get; }
}

/// <summary>One recent-item chip of the boarding catalog modal (mirrors Sales <c>.cat-recent-chip</c>).</summary>
public sealed class BoardingCatalogRecentChipViewModel
{
    public BoardingCatalogRecentChipViewModel(
        BoardingSelectableItem item,
        string iconKey,
        IBrush icoBackground,
        IBrush icoForeground)
    {
        Ref = item.Ref;
        Title = item.Title;
        IconKey = iconKey;
        IcoBackground = icoBackground;
        IcoForeground = icoForeground;
    }

    public string Ref { get; }

    public string Title { get; }

    public string IconKey { get; }

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }
}

/// <summary>
/// One row of the boarding catalog modal (mirrors Sales <c>.cat-row</c>): kind-tinted icon tile, the
/// highlight-split title, the kind + group caption, the price and the add button.
/// </summary>
public sealed partial class BoardingCatalogRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isHighlighted;

    public BoardingCatalogRowViewModel(
        BoardingSelectableItem item,
        string groupLabel,
        string iconKey,
        string kindLabel,
        IBrush icoBackground,
        IBrush icoForeground,
        string query)
    {
        Ref = item.Ref;
        Title = item.Title;
        GroupLabel = groupLabel;
        IconKey = iconKey;
        KindLabel = kindLabel;
        PriceText = BoardingPresentation.Money(item.Price);
        IcoBackground = icoBackground;
        IcoForeground = icoForeground;

        // Highlight-on-search: split the title into before / match / after using the same folding the
        // filter uses (Persian digits, Arabic yeh/kaf, case) on a length-preserving copy, so every kept
        // row highlights the right span of the original title.
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

    public string Ref { get; }

    public string Title { get; }

    public string GroupLabel { get; }

    public string IconKey { get; }

    public string KindLabel { get; }

    public string PriceText { get; }

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }

    public string PreText { get; }

    public string MatchText { get; }

    public string PostText { get; }

    public bool HasMatch { get; }
}
