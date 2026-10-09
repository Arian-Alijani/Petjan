using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Clinical;

/// <summary>Immutable billing snapshot committed with visit completion; consumed by S09.</summary>
public sealed class VisitBillingHandoff : SyncableEntity
{
    public string VisitId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string PetId { get; init; } = string.Empty;
    public string DoctorId { get; init; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; init; }
    public string LinesJson { get; init; } = "[]";
    public int Status { get; set; }
    public string? InvoiceId { get; set; }
}
