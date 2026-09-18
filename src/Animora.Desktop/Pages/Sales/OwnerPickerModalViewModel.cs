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

        _ = SearchAsync();
    }

    public ObservableCollection<OwnerRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _hasRows = true;

    [ObservableProperty]
    private bool _showNoResult;

    partial void OnQueryChanged(string value) => _ = SearchAsync();

    private async Task SearchAsync()
    {
        var owners = await _service.GetOwnersAsync(Query.Trim());
        Rows.Clear();
        foreach (var o in owners)
        {
            Rows.Add(new OwnerRowViewModel(o));
        }

        HasRows = Rows.Count > 0;
        ShowNoResult = Rows.Count == 0;
    }

    [RelayCommand]
    private async Task SelectOwnerAsync(string? id)
    {
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            return;
        }

        var pick = new OwnerPickResult(
            false, row.Id, row.FullName, row.PetName, row.Mobile);
        await _service.PickOwnerAsync(_invoiceId, pick);
        _dialogs.Close();
        _toast.Show($"مشتری «{row.FullName}» انتخاب شد", Tone.Ok);
        _onPicked?.Invoke();
    }

    [RelayCommand]
    private async Task SelectGuestAsync()
    {
        await _service.PickOwnerAsync(_invoiceId, new OwnerPickResult(true, string.Empty, string.Empty, string.Empty, string.Empty));
        _dialogs.Close();
        _onPicked?.Invoke();
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
        HasBalance = owner.Balance > 0;
        BalanceText = "بدهی " + SalePresentation.Money(owner.Balance);
    }

    public string Id { get; }

    public string FullName { get; }

    public string PetName { get; }

    public string FileCode { get; }

    public string Mobile { get; }

    public bool HasMobile { get; }

    public string Initial { get; }

    public IBrush AvatarBrush { get; }

    public bool HasBalance { get; }

    public string BalanceText { get; }
}
