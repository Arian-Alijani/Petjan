using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>The four internal sections of the «آمار» page (segmented tabs, not sidebar entries).</summary>
public enum StatisticsSection
{
    Overview,
    Clients,
    Sales,
    Expenses,
}

/// <summary>
/// Base of one «آمار» section. A section loads for the page-wide <see cref="StatisticsRange"/> and remembers which range
/// it last loaded, so switching tabs or re-selecting the same range never re-queries (PERFORMANCE.md: guard redundant
/// reloads); a data change or an explicit retry forces it. Failures never escape: they become <see cref="ErrorMessage"/>
/// (a Persian, whitelisted message) shown with a retry, and a cancelled load simply stops.
/// </summary>
public abstract partial class StatisticsSectionViewModel : ObservableObject
{
    private string? _loadedKey;

    /// <summary>The section this view-model backs.</summary>
    public abstract StatisticsSection Section { get; }

    /// <summary>Tab label (Persian).</summary>
    public abstract string Label { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>True once a load for the current range succeeded and found nothing to show.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>True when a successful load is on screen (drives the content-vs-placeholder switch).</summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>True when the section already shows data for <paramref name="range"/>.</summary>
    public bool IsCurrentFor(StatisticsRange range) => _loadedKey == KeyOf(range);

    /// <summary>Forgets the loaded range so the next <see cref="LoadAsync"/> re-queries.</summary>
    public void Invalidate() => _loadedKey = null;

    /// <summary>
    /// Loads the section for <paramref name="range"/>. Returns <see langword="true"/> on success (or when already current
    /// and not <paramref name="force"/>d), <see langword="false"/> when the failure was reported via <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task<bool> LoadAsync(StatisticsRange range, bool force, CancellationToken ct)
    {
        var key = KeyOf(range);
        if (!force && _loadedKey == key) return true;

        IsLoading = true;
        try
        {
            await LoadCoreAsync(range, ct);
            ct.ThrowIfCancellationRequested();
            _loadedKey = key;
            HasError = false;
            ErrorMessage = string.Empty;
            HasData = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StatisticsQueryException ex)
        {
            Fail(ex.UserMessage);
            return false;
        }
        catch (Exception)
        {
            Fail("بارگذاری آمار انجام نشد؛ دوباره تلاش کنید.");
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Queries and publishes the section's data; sets <see cref="IsEmpty"/>. Throws on failure.</summary>
    protected abstract Task LoadCoreAsync(StatisticsRange range, CancellationToken ct);

    private void Fail(string message)
    {
        _loadedKey = null;
        ErrorMessage = message;
        HasError = true;
    }

    private static string KeyOf(StatisticsRange range) => $"{range.RangeId}|{range.CustomFromDay}|{range.CustomToDay}";
}
