using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Messaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>The internal sections of the «مدیریت پیامک» page (segmented tabs, not sidebar entries).</summary>
public enum SmsSection
{
    Overview,
    Report,
    Settings,
}

/// <summary>
/// Base of one «مدیریت پیامک» section. Mirrors the «آمار» section contract: a section loads for the page-wide
/// <see cref="SmsReportRange"/> and remembers the key it last loaded, so switching tabs never re-queries; an explicit
/// refresh forces it. Failures never escape — they become a Persian <see cref="ErrorMessage"/> — and cancellation
/// propagates as <see cref="OperationCanceledException"/> so the page can drop superseded loads.
/// </summary>
public abstract partial class SmsSectionViewModel : ObservableObject
{
    private string? _loadedKey;

    /// <summary>The section this view-model backs.</summary>
    public abstract SmsSection Section { get; }

    /// <summary>Tab label (Persian).</summary>
    public abstract string Label { get; }

    /// <summary>Icons theme key shown before the tab label.</summary>
    public abstract string IconKey { get; }

    /// <summary>False for sections that ignore the range (settings): the page hides its range chips for them.</summary>
    public virtual bool UsesRange => true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>True when a successful load is on screen (drives the content-vs-placeholder switch).</summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>Forgets the loaded key so the next <see cref="LoadAsync"/> re-queries.</summary>
    public void Invalidate() => _loadedKey = null;

    /// <summary>Leaving the page: drop per-visit data so the next visit re-queries with a fresh placeholder.</summary>
    public void Evict()
    {
        _loadedKey = null;
        HasData = false;
        ReleaseHeavyData();
    }

    /// <summary>Clears large per-visit collections. Default: nothing.</summary>
    protected virtual void ReleaseHeavyData()
    {
    }

    /// <summary>
    /// Loads the section for <paramref name="range"/>. Returns <see langword="true"/> on success (or when already
    /// current and not <paramref name="force"/>d), <see langword="false"/> when the failure was reported via
    /// <see cref="ErrorMessage"/>.
    /// </summary>
    public async Task<bool> LoadAsync(SmsReportRange range, bool force, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(range);
        var key = UsesRange ? range.RangeId : "-";
        if (!force && _loadedKey == key) return true;

        IsLoading = true;
        try
        {
            await LoadCoreAsync(range, ct);
            ct.ThrowIfCancellationRequested();
            await PublishOnUiAsync(() =>
            {
                _loadedKey = key;
                HasError = false;
                ErrorMessage = string.Empty;
                HasData = true;
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmsServiceException ex)
        {
            await PublishOnUiAsync(() => Fail(ex.UserMessage));
            return false;
        }
        catch (Exception)
        {
            await PublishOnUiAsync(() => Fail("بارگذاری اطلاعات پیامک انجام نشد؛ دوباره تلاش کنید."));
            return false;
        }
        finally
        {
            PostToUi(() => IsLoading = false);
        }
    }

    /// <summary>Reads the section's data and publishes it (through <see cref="PublishOnUiAsync"/>).</summary>
    protected abstract Task LoadCoreAsync(SmsReportRange range, CancellationToken ct);

    /// <summary>Runs <paramref name="publish"/> on the UI thread (inline when already there).</summary>
    protected static Task PublishOnUiAsync(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (Dispatcher.UIThread.CheckAccess())
        {
            publish();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(publish).GetTask();
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
