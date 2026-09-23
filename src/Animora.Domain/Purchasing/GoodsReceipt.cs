using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Purchasing;

/// <summary>Lifecycle state of a goods receipt.</summary>
public enum GoodsReceiptState
{
    /// <summary>Being composed; lines may still change.</summary>
    Draft = 0,

    /// <summary>Posted to the stock ledger. Immutable — a correction is a reversing receipt.</summary>
    Posted = 1,

    /// <summary>Offset by a later reversing receipt. Terminal.</summary>
    Reversed = 2,
}

/// <summary>
/// The goods-receipt aggregate root: what physically arrived from a supplier, either against a
/// <see cref="PurchaseOrder"/> or as a direct receipt without one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A posted receipt is immutable (A-RULE 002).</b> <see cref="Post"/> freezes the lines
/// (<c>RECEIPT_LOCKED</c>) and raises <see cref="GoodsReceiptPostedEvent"/>; the movement writer
/// (D07/C06) then appends one <see cref="Inventory.MovementKind.Receipt"/> movement per line. The
/// receipt never writes the ledger itself and never edits a posted line: a correction is a
/// <b>new</b> receipt with <see cref="ReversesReceiptId"/> set, whose lines become
/// <see cref="Inventory.MovementKind.ReturnOut"/> movements.
/// </para>
/// </remarks>
public sealed class GoodsReceipt : AggregateRoot
{
    private readonly List<GoodsReceiptLine> _lines = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private GoodsReceipt()
    {
    }

    /// <summary>Per-tenant sequential receipt number, assigned by the Application layer.</summary>
    public string ReceiptNumber { get; private set; } = string.Empty;

    /// <summary>The order being received against; <see langword="null"/> for a direct receipt.</summary>
    public string? PurchaseOrderId { get; private set; }

    /// <summary>The supplier the goods came from.</summary>
    public string SupplierId { get; private set; } = string.Empty;

    /// <summary>When the goods arrived, UTC.</summary>
    public DateTimeOffset ReceivedUtc { get; private set; }

    /// <summary>The staff member who received the goods.</summary>
    public string ReceivedByUserId { get; private set; } = string.Empty;

    /// <summary>The supplier's invoice / waybill reference.</summary>
    public string? InvoiceReference { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public GoodsReceiptState State { get; private set; } = GoodsReceiptState.Draft;

    /// <summary>
    /// The posted receipt this one offsets; <see langword="null"/> for an original receipt. Its
    /// lines are written to the ledger as <see cref="Inventory.MovementKind.ReturnOut"/> movements.
    /// </summary>
    public string? ReversesReceiptId { get; private set; }

    /// <summary>The received lines; frozen once <see cref="GoodsReceiptState.Posted"/>.</summary>
    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    /// <summary><see langword="true"/> once the lines may no longer change.</summary>
    public bool AreLinesLocked => State != GoodsReceiptState.Draft;

    /// <summary><see langword="true"/> when this receipt offsets an earlier one.</summary>
    public bool IsReversal => ReversesReceiptId is not null;

    /// <summary>Creates an empty receipt in <see cref="GoodsReceiptState.Draft"/>.</summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="receiptNumber">Per-tenant sequential number, assigned by the Application layer.</param>
    /// <param name="supplierId">The supplier the goods came from.</param>
    /// <param name="receivedUtc">When the goods arrived, UTC.</param>
    /// <param name="receivedByUserId">The staff member receiving them.</param>
    /// <param name="purchaseOrderId">The order received against; <see langword="null"/> for a direct receipt.</param>
    /// <param name="invoiceReference">The supplier's invoice / waybill reference.</param>
    /// <param name="reversesReceiptId">The posted receipt this one offsets, when it is a correction.</param>
    public static Result<GoodsReceipt> Create(
        string tenantId,
        string receiptNumber,
        string supplierId,
        DateTimeOffset receivedUtc,
        string receivedByUserId,
        string? purchaseOrderId = null,
        string? invoiceReference = null,
        string? reversesReceiptId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedByUserId);

        return new GoodsReceipt
        {
            Id = IdGenerator.NewId(receivedUtc),
            TenantId = tenantId,
            ReceiptNumber = receiptNumber.Trim(),
            PurchaseOrderId = Clean(purchaseOrderId),
            SupplierId = supplierId,
            ReceivedUtc = receivedUtc,
            ReceivedByUserId = receivedByUserId,
            InvoiceReference = Clean(invoiceReference),
            State = GoodsReceiptState.Draft,
            ReversesReceiptId = Clean(reversesReceiptId),
        };
    }

    /// <summary>
    /// Appends a received line. Refused once posted (<c>RECEIPT_LOCKED</c>); the quantity must be
    /// strictly positive — the movement factory owns the sign of the resulting ledger entry.
    /// </summary>
    public Result AddLine(GoodsReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (line.Quantity.Value <= 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"Receipt line '{line.LineId}' must carry a positive quantity; got {line.Quantity.Value}.");
        }

        _lines.Add(line);
        return Result.Ok();
    }

    /// <summary>Removes a line by its id. Refused once posted.</summary>
    public Result RemoveLine(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = IndexOf(lineId);
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Line '{lineId}' is not on receipt '{Id}'.");
        }

        _lines.RemoveAt(index);
        return Result.Ok();
    }

    /// <summary>
    /// Posts the receipt: the lines become final (<c>RECEIPT_LOCKED</c>) and
    /// <see cref="GoodsReceiptPostedEvent"/> is raised so the movement writer appends one ledger
    /// entry per line. Posting twice is refused.
    /// </summary>
    public Result Post(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (State != GoodsReceiptState.Draft)
        {
            return Result.Fail(
                ErrorCodes.ReceiptLocked,
                $"Receipt '{Id}' is {State}; only a draft can be posted.");
        }

        if (_lines.Count == 0)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, $"Receipt '{Id}' has no lines to post.");
        }

        State = GoodsReceiptState.Posted;

        var now = clock.UtcNow;
        Raise(new GoodsReceiptPostedEvent(
            IdGenerator.NewId(now), TenantId, now, Id, SupplierId, _lines.Count));
        return Result.Ok();
    }

    /// <summary>
    /// Marks this posted receipt as offset by a later reversing receipt (the one carrying
    /// <see cref="ReversesReceiptId"/> = this id). The lines and their ledger entries are
    /// untouched — reversal is additive, never an edit.
    /// </summary>
    public Result MarkReversed()
    {
        if (State != GoodsReceiptState.Posted)
        {
            return Result.Fail(
                ErrorCodes.ReceiptLocked,
                $"Receipt '{Id}' is {State}; only a posted receipt can be reversed.");
        }

        State = GoodsReceiptState.Reversed;
        return Result.Ok();
    }

    /// <summary>Updates the supplier invoice reference. Refused once posted.</summary>
    public Result SetInvoiceReference(string? invoiceReference)
    {
        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        InvoiceReference = Clean(invoiceReference);
        return Result.Ok();
    }

    private Result EnsureLinesEditable() =>
        AreLinesLocked
            ? Result.Fail(ErrorCodes.ReceiptLocked, $"Receipt '{Id}' is {State}; it is immutable.")
            : Result.Ok();

    private int IndexOf(string lineId)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (string.Equals(_lines[i].LineId, lineId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
