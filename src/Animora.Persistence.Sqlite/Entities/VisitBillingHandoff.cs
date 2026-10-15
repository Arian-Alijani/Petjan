using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

public enum HandoffStatus { Ready, Invoiced, Cancelled }

/// <summary>Device-local immutable-at-handoff billing snapshot; one row per visit.</summary>
[LocalOnly]
public sealed class VisitBillingHandoff
{
    public const int LinesJsonMaxLength = 65536;

    private VisitBillingHandoff() { }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string VisitId { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string PetId { get; private set; } = string.Empty;
    public string DoctorId { get; private set; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; private set; }
    public string LinesJson { get; private set; } = string.Empty;
    public HandoffStatus Status { get; private set; }
    public string? InvoiceId { get; private set; }

    /// <summary>Seed/test convenience: a handoff with a fresh random id (production paths pass the operation-derived id).</summary>
    public static VisitBillingHandoff Create(string tenantId, string visitId, string ownerId,
        string petId, string doctorId, DateTimeOffset completedUtc, string linesJson) =>
        Create(IdGenerator.NewId(completedUtc), tenantId, visitId, ownerId, petId, doctorId, completedUtc, linesJson);

    /// <param name="id">Operation-derived handoff id, equal on the client and the server for the same completion (D03).</param>
    public static VisitBillingHandoff Create(string id, string tenantId, string visitId, string ownerId,
        string petId, string doctorId, DateTimeOffset completedUtc, string linesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(visitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctorId);
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, LinesJsonMaxLength);
        return new VisitBillingHandoff
        {
            Id = id, TenantId = tenantId, VisitId = visitId,
            OwnerId = ownerId, PetId = petId, DoctorId = doctorId,
            CompletedUtc = completedUtc, LinesJson = linesJson, Status = HandoffStatus.Ready,
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
