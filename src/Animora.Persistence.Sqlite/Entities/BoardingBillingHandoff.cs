using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Why a boarding handoff was raised. A stay can raise two over its life: the <see cref="Room"/> charge
/// collected in advance for a prepaid stay (at admission), then the <see cref="Final"/> settlement at
/// checkout. A postpaid stay only ever raises a single <see cref="Final"/> handoff. Persisted value 0 is
/// <see cref="Final"/> so existing rows keep their meaning.
/// </summary>
public enum BoardingHandoffKind
{
    /// <summary>Settlement at checkout (room + items, or items-only when the room was prepaid).</summary>
    Final = 0,

    /// <summary>The prepaid room charge raised at admission; does not complete the checkout.</summary>
    Room = 1,
}

/// <summary>Device-local billing payload for a stay awaiting an invoice.</summary>
[LocalOnly]
public sealed class BoardingBillingHandoff
{
    public const int LinesJsonMaxLength = 65536;
    private BoardingBillingHandoff() { }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string StayId { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string PetId { get; private set; } = string.Empty;
    public string DoctorId { get; private set; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; private set; }
    public string LinesJson { get; private set; } = string.Empty;
    public HandoffStatus Status { get; private set; }

    /// <summary>Whether this is the prepaid room charge or the final checkout settlement.</summary>
    public BoardingHandoffKind Kind { get; private set; }

    public string? InvoiceId { get; private set; }

    /// <summary>Seed/test convenience: a handoff with a fresh random id (production paths pass the operation-derived id).</summary>
    public static BoardingBillingHandoff Create(string tenantId, string stayId, string ownerId,
        string petId, DateTimeOffset completedUtc, string linesJson,
        BoardingHandoffKind kind = BoardingHandoffKind.Final) =>
        Create(IdGenerator.NewId(completedUtc), tenantId, stayId, ownerId, petId, completedUtc, linesJson, kind);

    /// <param name="id">Operation-derived handoff id, equal on the client and the server for the same operation (D03).</param>
    public static BoardingBillingHandoff Create(string id, string tenantId, string stayId, string ownerId,
        string petId, DateTimeOffset completedUtc, string linesJson,
        BoardingHandoffKind kind = BoardingHandoffKind.Final)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, LinesJsonMaxLength);
        return new BoardingBillingHandoff
        {
            Id = id, TenantId = tenantId, StayId = stayId,
            OwnerId = ownerId, PetId = petId, CompletedUtc = completedUtc,
            LinesJson = linesJson, Status = HandoffStatus.Ready, Kind = kind,
        };
    }

    public void MarkInvoiced(string invoiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        if (Status != HandoffStatus.Ready) throw new InvalidOperationException("Handoff is not ready.");
        InvoiceId = invoiceId;
        Status = HandoffStatus.Invoiced;
    }

    public void Cancel()
    {
        if (Status != HandoffStatus.Ready) throw new InvalidOperationException("Handoff is not ready.");
        Status = HandoffStatus.Cancelled;
    }
}
