using Animora.Application.Ports;
using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Boarding;

public sealed class BoardingBillingHandoff : SyncableEntity
{
    public string StayId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string PetId { get; init; } = string.Empty;
    public string DoctorId { get; init; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; init; }
    public string LinesJson { get; init; } = "[]";
    public int Status { get; set; }
    public BoardingHandoffKind Kind { get; init; }
    public string? InvoiceId { get; set; }
}
