using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Search;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell.Search;

/// <summary>
/// Drives the Ctrl+K command palette. Owns the query text, the filtered result
/// list (with a wrap-around highlight), and the keyboard/mouse activation flow.
/// Presentation state only: all searching is delegated to <see cref="ISearchService"/>
/// and all navigation to <see cref="INavigationService"/>.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    // Input is debounced so a burst of keystrokes triggers one search, and each
    // new query cancels the previous in-flight search (mirrors layout.js's
    // single-paint model without the intermediate work).
    private readonly Debouncer _debounce = new(TimeSpan.FromMilliseconds(120));

    private readonly ISearchService _search;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    private CancellationTokenSource? _cts;
    private int _selectedIndex;

    public CommandPaletteViewModel(
        ISearchService search,
        INavigationService navigation,
        IDialogService dialogs)
    {
        _search = search;
        _navigation = navigation;
        _dialogs = dialogs;

        // Seed the default suggestions synchronously so the palette paints
        // instantly on open (the reference shows CMDK_ACTIONS.slice(0, 4)).
        Replace(_search.DefaultSuggestions, "پیشنهادها");
    }

    /// <summary>Two-way bound to the palette input.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Section header («پیشنهادها» / «نتایج»).</summary>
    [ObservableProperty]
    private string _resultsHeader = "پیشنهادها";

    /// <summary>True when the filtered list is empty (shows the empty state).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>The query echoed in the empty state («نتیجه‌ای برای «X» پیدا نشد»).</summary>
    [ObservableProperty]
    private string _emptyQuery = string.Empty;

    /// <summary>Filtered results, in display order.</summary>
    public RangeObservableCollection<CommandPaletteItemViewModel> Results { get; } = new();

    partial void OnSearchTextChanged(string value) =>
        _debounce.Debounce(() => _ = RunSearchAsync(value));

    /// <summary>Move the highlight down (wrapping).</summary>
    public void MoveDown()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SetSelected((_selectedIndex + 1) % Results.Count);
    }

    /// <summary>Move the highlight up (wrapping).</summary>
    public void MoveUp()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SetSelected((_selectedIndex - 1 + Results.Count) % Results.Count);
    }

    /// <summary>Set the highlight to a specific row (mouse hover).</summary>
    public void Highlight(CommandPaletteItemViewModel item)
    {
        var index = Results.IndexOf(item);
        if (index >= 0)
        {
            SetSelected(index);
        }
    }

    /// <summary>Activate the currently highlighted row (Enter).</summary>
    [RelayCommand]
    private void Activate()
    {
        if (_selectedIndex >= 0 && _selectedIndex < Results.Count)
        {
            Choose(Results[_selectedIndex]);
        }
    }

    /// <summary>Activate a specific row (click).</summary>
    [RelayCommand]
    private void Choose(CommandPaletteItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _dialogs.Close();
        _navigation.NavigateTo(item.Target);
    }

    /// <summary>Close the palette (Esc / backdrop).</summary>
    [RelayCommand]
    private void Close()
    {
        _cts?.Cancel();
        _debounce.Cancel();
        _dialogs.Close();
    }

    private async Task RunSearchAsync(string query)
    {
        // Cancel the superseded search, then start a fresh one.
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var results = await _search.SearchAsync(query, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Replace(results, string.IsNullOrWhiteSpace(query) ? "پیشنهادها" : "نتایج");
            EmptyQuery = query.Trim();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — nothing to render.
        }
    }

    private void Replace(IReadOnlyList<SearchResult> results, string header)
    {
        ResultsHeader = header;
        Results.ReplaceAll(BuildItems(results));
        IsEmpty = Results.Count == 0;
        SetSelected(0);
    }

    private static IEnumerable<CommandPaletteItemViewModel> BuildItems(IReadOnlyList<SearchResult> results)
    {
        foreach (var result in results)
        {
            yield return new CommandPaletteItemViewModel(result);
        }
    }

    private void SetSelected(int index)
    {
        _selectedIndex = index;
        for (var i = 0; i < Results.Count; i++)
        {
            Results[i].IsSelected = i == index;
        }
    }
}
