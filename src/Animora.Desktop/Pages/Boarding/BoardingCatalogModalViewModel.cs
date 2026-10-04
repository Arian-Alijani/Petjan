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
/// The boarding «افزودن خدمت یا کالا» picker. Replaces the in-modal popup (which the stay-detail modal's
/// <c>ClipToBounds</c> panel clipped away) with a dedicated centered modal that behaves like the Sales catalog
/// modal: search box, source filter chips with counts, dense rows. A row click adds the item (with the
/// quantity box value) to the stay through <see cref="IBoardingQueryService.AddItemAsync"/>; the modal stays
/// open so several lines can be added, and «بازگشت» returns to the stay-detail modal.
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

    public RangeObservableCollection<BoardingCatalogRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private bool _hasRows = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _addedText = string.Empty;

    private int _addedCount;

    public string Subtitle => $"افزودن به اقلام «{_petName}»";

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
        foreach (var filter in Filters)
        {
            filter.IsActive = filter.Key == key;
        }

        Render();
    }

    partial void OnQueryChanged(string value) => Render();

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

        Rows.ReplaceAll(_visible.Select(item =>
        {
            var kind = item.Source == BoardingItemSource.ClinicItem ? BoardingItemKind.Item : BoardingItemKind.Service;
            var (bg, fg) = BoardingPresentation.ItemTagBrushes(kind);
            return new BoardingCatalogRowViewModel(item, SourceLabel(item.Source), IconFor(item.Source),
                BoardingPresentation.ItemKindLabel(kind), bg, fg);
        }));

        HasRows = Rows.Count > 0;
        CountText = Rows.Count > 0 ? $"{BoardingPresentation.Fa(Rows.Count)} نتیجه" : string.Empty;
    }

    [RelayCommand]
    private async Task AddAsync(string? reference)
    {
        var item = _all.FirstOrDefault(x => x.Ref == reference);
        if (item is null || _isAdding)
        {
            return;
        }

        var parsed = int.TryParse(PersianNumberFormatter.ToAsciiDigits(QuantityText.Trim()),
            NumberStyles.None, CultureInfo.InvariantCulture, out var q) ? q : 1;
        var quantity = Math.Clamp(parsed, 1, 999);

        _isAdding = true;
        try
        {
            await _service.AddItemAsync(_stayId, item.Ref, quantity);
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

        _addedCount += quantity;
        AddedText = $"{BoardingPresentation.Fa(_addedCount)} مورد افزوده شد";
        _toast.Show($"«{item.Title}» به اقلام «{_petName}» اضافه شد", Tone.Ok);
        _onChanged?.Invoke();
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

/// <summary>One source filter chip of the boarding catalog modal.</summary>
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

/// <summary>One row of the boarding catalog modal (kind-tinted icon tile, title, group caption, price).</summary>
public sealed class BoardingCatalogRowViewModel
{
    public BoardingCatalogRowViewModel(
        BoardingSelectableItem item,
        string groupLabel,
        string iconKey,
        string kindLabel,
        IBrush icoBackground,
        IBrush icoForeground)
    {
        Ref = item.Ref;
        Title = item.Title;
        GroupLabel = groupLabel;
        IconKey = iconKey;
        KindLabel = kindLabel;
        PriceText = BoardingPresentation.Money(item.Price);
        IcoBackground = icoBackground;
        IcoForeground = icoForeground;
    }

    public string Ref { get; }

    public string Title { get; }

    public string GroupLabel { get; }

    public string IconKey { get; }

    public string KindLabel { get; }

    public string PriceText { get; }

    public IBrush IcoBackground { get; }

    public IBrush IcoForeground { get; }
}
