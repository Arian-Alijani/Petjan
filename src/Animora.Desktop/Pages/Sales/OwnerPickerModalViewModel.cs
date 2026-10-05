using System.Collections.ObjectModel;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Backs the «انتخاب مشتری» picker (mirrors the reference's <c>openPicker</c> /
/// <c>selectOwner</c> / <c>selectGuest</c> / <c>.sw-modal</c>): a searchable owner
/// directory with avatar, file code, mobile and balance, plus the guest-sale
/// option. The chosen owner/guest is assigned through
/// <see cref="ISalesQueryService.PickOwnerAsync"/>.
/// </summary>
public sealed partial class OwnerPickerModalViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly string _invoiceId;
    private readonly Action? _onPicked;

    public OwnerPickerModalViewModel(
        ISalesQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string invoiceId,
        Action? onPicked = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _invoiceId = invoiceId;
        _onPicked = onPicked;

        ApplyIdleState();
    }

    /// <summary>Owners are never bulk-loaded: the list stays empty until the user types at least this many chars.</summary>
    private const int MinQueryLength = 2;

    private const int PageSize = 20;

    private const string HintMessage = "برای انتخاب مشتری، حداقل ۲ حرف از نام یا شمارهٔ تماس را وارد کنید.";

    public RangeObservableCollection<OwnerRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _hasRows;

    /// <summary>Kept for the legacy «موردی یافت نشد» binding: true only after a real search returned nothing.</summary>
    [ObservableProperty]
    private bool _showNoResult;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _resultCount = string.Empty;

    [ObservableProperty]
    private bool _showMessage = true;

    [ObservableProperty]
    private string _message = HintMessage;

    // Typing is debounced (like every other search field) so a fast typist issues one lookup, not one per
    // character (PERFORMANCE.md §2/§13).
    private readonly Debouncer _queryDebounce = new(TimeSpan.FromMilliseconds(250));

    // Monotonic request token: a slow earlier response must never overwrite a newer query's rows.
    private int _generation;

    partial void OnQueryChanged(string value)
    {
        // Invalidate any in-flight lookup and reset the list immediately; only a settled >=2-char query hits the store.
        _generation++;
        _queryDebounce.Cancel();
        ApplyIdleState();
        if (PersianText.Normalize(value ?? string.Empty).Length >= MinQueryLength)
        {
            Message = "در حال جست‌وجو…";
            _queryDebounce.Debounce(() => _ = SearchAsync());
        }
    }

    private void ApplyIdleState()
    {
        Rows.ReplaceAll([]);
        HasRows = false;
        ShowNoResult = false;
        IsSearching = false;
        ResultCount = string.Empty;
        ShowMessage = true;
        Message = HintMessage;
    }

    private async Task SearchAsync()
    {
        var term = Query.Trim();
        if (PersianText.Normalize(term).Length < MinQueryLength)
        {
            return;
        }

        var token = ++_generation;
        IsSearching = true;
        try
        {
            var owners = await _service.GetOwnersAsync(term);
            if (token != _generation)
            {
                return;
            }

            Rows.ReplaceAll(owners.Take(PageSize).Select(o => new OwnerRowViewModel(o)));
            HasRows = Rows.Count > 0;
            ShowNoResult = Rows.Count == 0;
            ResultCount = PersianNumberFormatter.ToPersianDigits(Rows.Count.ToString()) + " نتیجه";
            var hasMore = owners.Count > PageSize;
            ShowMessage = !HasRows || hasMore;
            Message = hasMore
                ? "۲۰ نتیجه اول نمایش داده شد؛ برای یافتن مشتری، جست‌وجو را دقیق‌تر کنید."
                : "مشتری‌ای با این مشخصات پیدا نشد؛ نام یا شمارهٔ تماس را بررسی کنید.";
        }
        catch (Exception) when (token == _generation)
        {
            Rows.ReplaceAll([]);
            HasRows = false;
            ShowNoResult = false;
            ResultCount = string.Empty;
            ShowMessage = true;
            Message = "جست‌وجو انجام نشد. دوباره جست‌وجو کنید.";
        }
        catch (Exception)
        {
            // A stale lookup failed after a newer query took over: ignore.
        }
        finally
        {
            if (token == _generation)
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private async Task SelectOwnerAsync(string? id)
    {
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            return;
        }

        var pick = new OwnerPickResult(false, row.Id);
        if (!await TryPickAsync(pick))
        {
            return;
        }

        _dialogs.Close();
        _toast.Show($"مشتری «{row.FullName}» انتخاب شد", Tone.Ok);
        _onPicked?.Invoke();
    }

    [RelayCommand]
    private async Task SelectGuestAsync()
    {
        if (!await TryPickAsync(new OwnerPickResult(true, string.Empty)))
        {
            return;
        }

        _dialogs.Close();
        _onPicked?.Invoke();
    }

    /// <summary>Assigns the pick; a backend failure keeps the picker open with a danger toast (X09).</summary>
    private async Task<bool> TryPickAsync(OwnerPickResult pick)
    {
        try
        {
            await _service.PickOwnerAsync(_invoiceId, pick);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                    ex, "انتخاب مشتری ناموفق بود؛ دوباره تلاش کنید"),
                Tone.Danger);
            return false;
        }
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();
}

/// <summary>One owner row of the picker (mirrors <c>.sw-owner-row</c>).</summary>
public sealed class OwnerRowViewModel
{
    public OwnerRowViewModel(OwnerRef owner)
    {
        Id = owner.Id;
        FullName = string.IsNullOrEmpty(owner.PetName)
            ? $"{owner.FirstName} {owner.LastName}"
            : $"{owner.FirstName} {owner.LastName} · {owner.PetName}";
        PetName = owner.PetName;
        FileCode = owner.FileCode;
        Mobile = string.IsNullOrEmpty(owner.Mobile)
            ? string.Empty
            : PersianNumberFormatter.ToPersianDigits(owner.Mobile);
        HasMobile = Mobile.Length > 0;
        Initial = string.IsNullOrEmpty(owner.FirstName) ? "?" : owner.FirstName[..1];
        AvatarBrush = SalePresentation.AvatarBrush(owner.Tone);
    }

    public string Id { get; }

    public string FullName { get; }

    public string PetName { get; }

    public string FileCode { get; }

    public string Mobile { get; }

    public bool HasMobile { get; }

    public string Initial { get; }

    public IBrush AvatarBrush { get; }

}
