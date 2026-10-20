using System;
using Animora.Contracts.Visits;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One billable line in the rx step's list, mirroring the reference's
/// <c>rxLineHtml</c> row (kind badge «خ»/«ک», title, «قیمت: … · جمع: …»
/// caption, an editable quantity input and a remove button). Editing the
/// quantity writes straight through to the shared line and asks the owning
/// ViewModel to recompute the running subtotal — the reference's
/// <c>updateSubtotalOnly</c> behaviour. The input shows Persian digits exactly
/// like the reference's <c>toFa(entry.qty)</c>.
/// </summary>
public sealed partial class RxLineViewModel : ObservableObject
{
    private readonly Action _onQuantityChanged;
    private int _quantity;

    public RxLineViewModel(RxLine line, Action onQuantityChanged)
    {
        Line = line;
        _onQuantityChanged = onQuantityChanged;
        _quantity = line.Quantity;
    }

    public RxLine Line { get; }

    public string Id => Line.Id;

    public string Title => Line.Title;

    public bool IsService => Line.Kind == RxCatalogKind.Service;

    public bool IsItem => Line.Kind == RxCatalogKind.Item;

    /// <summary>Kind badge glyph — «خ» for a service, «ک» for an item.</summary>
    public string BadgeGlyph => IsService ? "خ" : "ک";

    public IBrush BadgeBackground => IsService
        ? VisitsPresentation.Fill("#eef2fe")
        : VisitsPresentation.Fill("#f4f3ff");

    public IBrush BadgeForeground => IsService
        ? VisitsPresentation.Fill("#3a56d4")
        : VisitsPresentation.Fill("#7b5cc4");

    /// <summary>Largest quantity a single line accepts — keeps <c>price × qty</c> far inside <c>long</c> (V11).</summary>
    public const int MaxQuantity = 9999;

    /// <summary>Unit price × quantity with the same <c>checked</c> arithmetic the backend uses; saturates at
    /// <see cref="long.MaxValue"/> instead of wrapping when a pathological price would overflow.</summary>
    public long LineTotal
    {
        get
        {
            try { return checked(Line.UnitPrice * _quantity); }
            catch (OverflowException) { return long.MaxValue; }
        }
    }

    public string LineTotalText => VisitsPresentation.Money(LineTotal);

    /// <summary>The reference's «قیمت: … · جمع: …» caption line.</summary>
    public string PriceCaption => $"قیمت: {VisitsPresentation.Money(Line.UnitPrice)} · جمع:";

    /// <summary>The review line's kind tag — «خدمت» for a service, «کالا» for an item.</summary>
    public string KindLabel => IsService ? "خدمت" : "کالا";

    /// <summary>The review line's «{qty} × {unitPrice}» caption (reference reviewLineHtml).</summary>
    public string QtyXPriceText =>
        $"{PersianNumberFormatter.ToPersianDigits(_quantity)} × {VisitsPresentation.Money(Line.UnitPrice)}";

    /// <summary>
    /// The quantity input's text. Displays Persian digits (reference
    /// <c>toFa(entry.qty)</c>); setting it parses either Persian or ASCII digits,
    /// clamps to 1..<see cref="MaxQuantity"/> and writes through to the shared line.
    /// </summary>
    public string QuantityText
    {
        get => PersianNumberFormatter.ToPersianDigits(_quantity);
        set
        {
            var ascii = PersianNumberFormatter.ToAsciiDigits(value).Trim();
            var parsed = int.TryParse(ascii, out var number) ? number : (ascii.Length > 0 && ascii.All(char.IsAsciiDigit) ? MaxQuantity : 1);

            // Reference clamps to ≥ 1 (Math.max(1, parseInt(...) || 1)); the upper bound is ours.
            var clamped = Math.Clamp(parsed, 1, MaxQuantity);
            _quantity = clamped;
            Line.Quantity = clamped;

            OnPropertyChanged();
            OnPropertyChanged(nameof(LineTotal));
            OnPropertyChanged(nameof(LineTotalText));
            OnPropertyChanged(nameof(QtyXPriceText));
            _onQuantityChanged();
        }
    }
}
