using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

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
    public string? InvoiceId { get; private set; }

    public static BoardingBillingHandoff Create(string tenantId, string stayId, string ownerId,
        string petId, DateTimeOffset completedUtc, string linesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, LinesJsonMaxLength);
        return new BoardingBillingHandoff
        {
            Id = IdGenerator.NewId(completedUtc), TenantId = tenantId, StayId = stayId,
            OwnerId = ownerId, PetId = petId, CompletedUtc = completedUtc,
            LinesJson = linesJson, Status = HandoffStatus.Ready,
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
