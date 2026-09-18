using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Backs the Boarding «انتقال به صندوق» modal (Animora-UI/assets/boarding.js
/// <c>openTransferModal</c>): a draft-invoice preview listing the stay line plus
/// every consumed line, the amount to transfer, and a confirm that flags the
/// stay's invoice as transferred.
/// </summary>
public sealed partial class TransferModalViewModel : ObservableObject
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onTransferred;

    private readonly BoardingStay _stay;

    public TransferModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        BoardingStay stay,
        IReadOnlyList<BoardingStayItem> items,
        Action? onTransferred = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _stay = stay;
        _onTransferred = onTransferred;

        var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);

        HintLead = "فاکتور «";
        HintPet = stay.PetName;
        HintMiddle = "» — ";
        HintOwner = stay.OwnerName;
        HintTail = "» به صورت پیش‌نویس در صندوق ایجاد می‌شود:";

        Lines.Add(new TransferLineViewModel(
            $"اقامت پانسیون — اتاق {PersianNumberFormatter.ToPersianDigits(stay.Room)}",
            nights,
            stay.DailyRate));

        foreach (var item in items)
        {
            Lines.Add(new TransferLineViewModel(item.Title, item.Quantity, item.Price));
        }

        var total = (long)nights * stay.DailyRate + items.Sum(it => (long)it.Quantity * it.Price);
        TotalText = BoardingPresentation.Money(total);
    }

    public ObservableCollection<TransferLineViewModel> Lines { get; } = [];

    public string HintLead { get; }
    public string HintPet { get; }
    public string HintMiddle { get; }
    public string HintOwner { get; }
    public string HintTail { get; }

    public string TotalText { get; }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        await _service.TransferAsync(_stay.Id);
        _dialogs.Close();
        _toast.Show($"فاکتور «{_stay.PetName}» به مبلغ {TotalText} به صندوق منتقل شد (نمونه طراحی)");
        _onTransferred?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}

/// <summary>
/// One line of the transfer preview, mirroring the reference's <c>.bd-tline</c>.
/// </summary>
public sealed class TransferLineViewModel
{
    public TransferLineViewModel(string title, int quantity, long price)
    {
        Title = title;
        QuantityText = $"× {BoardingPresentation.Fa(quantity)}";
        AmountText = BoardingPresentation.Money((long)quantity * price);
    }

    public string Title { get; }
    public string QuantityText { get; }
    public string AmountText { get; }
}
