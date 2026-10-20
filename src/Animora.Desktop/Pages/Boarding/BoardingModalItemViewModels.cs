using Animora.Contracts.Boarding;
using Animora.Contracts.Sales;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// One day cell of the detail modal's day timeline, mirroring the reference's
/// <c>.bd-dm-day</c> block (boarding.js <c>_openDetailFor</c>): the night's
/// ordinal, its «d MMM» date, and a dot when the day recorded consumed lines.
/// </summary>
public sealed class BoardingDayCellViewModel
{
    public BoardingDayCellViewModel(int ordinal, DateOnly date, bool isPast, bool isToday, bool hasDot, string tooltip)
    {
        NumberText = BoardingPresentation.Fa(ordinal);
        DateText = BoardingPresentation.Medium(date);
        IsPast = isPast;
        IsToday = isToday;
        HasDot = hasDot;
        Tooltip = tooltip;

        // Reference .bd-dm-day states: today → brand-600 fill (white text);
        // past → brand-50 fill with brand-100 border; otherwise panel white.
        Background = isToday
            ? BoardingPresentation.Fill("#4a6cf7")
            : isPast
                ? BoardingPresentation.Fill("#eef2fe")
                : BoardingPresentation.Fill("#ffffff");
        BorderBrush = isToday
            ? BoardingPresentation.Fill("#4a6cf7")
            : isPast
                ? BoardingPresentation.Fill("#ccfbf1")
                : BoardingPresentation.Fill("#e6e9ec");
        NumberBrush = isToday ? BoardingPresentation.Fill("#ffffff") : BoardingPresentation.Fill("#101828");
        DateBrush = isToday ? BoardingPresentation.Fill("#ccf2ee") : BoardingPresentation.Fill("#667085");
        DotBrush = isToday ? BoardingPresentation.Fill("#ffffff") : BoardingPresentation.Fill("#4a6cf7");
    }

    public string NumberText { get; }
    public string DateText { get; }
    public bool IsPast { get; }
    public bool IsToday { get; }
    public bool HasDot { get; }
    public string Tooltip { get; }
    public IBrush Background { get; }
    public IBrush BorderBrush { get; }
    public IBrush NumberBrush { get; }
    public IBrush DateBrush { get; }
    public IBrush DotBrush { get; }
}

/// <summary>
/// One consumed line row of the detail modal, mirroring the reference's
/// <c>.bd-dm-cons-row</c> (tag / title+sub / qty / amount / remove). The remove
/// button is hidden for a finished («done») stay, exactly as the reference
/// hides it when the stay is not editable.
/// </summary>
public sealed partial class BoardingConsumedRowViewModel : ObservableObject
{
    public BoardingConsumedRowViewModel(int index, BoardingStayItem item, bool editable)
    {
        Index = index;
        KindLabel = BoardingPresentation.ItemKindLabel(item.Kind);
        (TagBackground, TagForeground) = BoardingPresentation.ItemTagBrushes(item.Kind);
        Title = item.Title;
        SubText = $"{BoardingPresentation.Medium(item.At)} · فی {BoardingPresentation.Money(item.Price)}";
        QuantityText = $"× {BoardingPresentation.Fa(item.Quantity)}";
        AmountText = BoardingPresentation.Money((long)item.Quantity * item.Price);
        IsRemovable = editable;
    }

    public int Index { get; }
    public string KindLabel { get; }
    public IBrush TagBackground { get; }
    public IBrush TagForeground { get; }
    public string Title { get; }
    public string SubText { get; }
    public string QuantityText { get; }
    public string AmountText { get; }
    public bool IsRemovable { get; }
}

/// <summary>
/// One payment-mode radio of the new-stay modal, mirroring the reference's
/// <c>.bd-pay-opt</c> pair («پس‌پرداخت» default / «پیش‌پرداخت»).
/// </summary>
public sealed partial class BoardingPaymentOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public BoardingPaymentOptionViewModel(BoardingPaymentMode mode, string title, string hint)
    {
        Mode = mode;
        Title = title;
        Hint = hint;
    }

    public BoardingPaymentMode Mode { get; }
    public string Title { get; }
    public string Hint { get; }
}

/// <summary>
/// One known-pet suggestion in the new-stay modal's autocomplete list,
/// mirroring the reference's <c>.bd-ac-item</c> rows.
/// </summary>
public sealed class BoardingKnownPetViewModel
{
    public BoardingKnownPetViewModel(BoardingKnownPet pet)
    {
        Pet = pet;
        SpeciesIconKey = BoardingPresentation.SpeciesIconKey(pet.Species);
        SpeciesLabel = BoardingPresentation.SpeciesLabel(pet.Species);
        SearchText = PersianText.Normalize($"{pet.PetName} {pet.Breed} {pet.OwnerName} {pet.OwnerPhone}");
    }

    public BoardingKnownPet Pet { get; }
    public string PetName => Pet.PetName;
    public string Breed => Pet.Breed;
    public string OwnerName => Pet.OwnerName;
    public string SpeciesIconKey { get; }
    public string SpeciesLabel { get; }
    public string SearchText { get; }
}

/// <summary>
/// One room tile of the Settings tab, mirroring the reference's
/// <c>.bd-room-tile</c> (boarding.js <c>renderSettingsRooms</c>).
/// </summary>
public sealed class BoardingRoomTileViewModel
{
    public BoardingRoomTileViewModel(RoomInfo room)
    {
        Room = room;
        Label = PersianNumberFormatter.ToPersianDigits(room.Label);
        Name = string.IsNullOrWhiteSpace(room.Name) || room.Name == room.Label
            ? string.Empty
            : PersianNumberFormatter.ToPersianDigits(room.Name);
        TypeLabel = BoardingPresentation.RoomKindLabel(room.Kind);
        SpeciesIconKey = BoardingPresentation.SpeciesIconKey(room.Species);
        IsOccupied = room.IsOccupied;
        StatusText = room.Status switch
        {
            BoardingRoomStatus.Maintenance => "در تعمیر",
            BoardingRoomStatus.Retired => "بازنشسته",
            _ => room.IsOccupied ? "اشغال" : "آزاد",
        };
        IsOutOfService = room.Status != BoardingRoomStatus.Available;
        CapacityText = "ظرفیت " + PersianNumberFormatter.ToPersianDigits(room.Capacity);
    }

    public RoomInfo Room { get; }
    public string Name { get; }
    public bool HasName => Name.Length > 0;
    public bool IsOutOfService { get; }
    public bool IsAvailable => !IsOccupied && !IsOutOfService;
    public string CapacityText { get; }

    public string Label { get; }
    public string TypeLabel { get; }
    public string SpeciesIconKey { get; }
    public bool IsOccupied { get; }
    public string StatusText { get; }
}

/// <summary>
/// One service row of the Settings tab, mirroring the reference's
/// <c>.bd-svc-row</c> (boarding.js <c>renderSettingsSvcs</c>).
/// </summary>
public sealed class BoardingServiceRowViewModel
{
    public BoardingServiceRowViewModel(ServiceRate rate)
    {
        Rate = rate;
        Title = rate.Title;
        PriceText = Animora.Desktop.Pages.Sales.SalePresentation.Money(rate.Price) + " تومان";
        IsActive = rate.IsActive;
        BillingText = rate.Billing switch
        {
            ServiceRateBilling.PerDay => "روزانه",
            ServiceRateBilling.PerStay => "هر اقامت",
            _ => "هر بار",
        };
    }

    public ServiceRate Rate { get; }
    public string Title { get; }
    public string PriceText { get; }
    public bool IsActive { get; }
    public bool IsInactive => !IsActive;
    public string BillingText { get; }
    public string SubtitleText => IsActive ? BillingText : BillingText + " · غیرفعال";
}
