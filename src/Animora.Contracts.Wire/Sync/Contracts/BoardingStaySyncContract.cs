using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record BoardingStaySyncContract
{
    public string PetId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public DateTimeOffset CheckInUtc { get; init; }
    public DateTimeOffset PlannedCheckOutUtc { get; init; }
    public DateTimeOffset? ActualCheckOutUtc { get; init; }
    public int State { get; init; }
    public JsonElement? DailyRate { get; init; }
    public int PaymentMode { get; init; }
    public string? CareNote { get; init; }
    public string? DietNote { get; init; }
    public string? InvoiceId { get; init; }
    public string? PrepaidInvoiceId { get; init; }
    public long? PrepaidRoomChargeMinor { get; init; }
    public long? FrozenTotalMinor { get; init; }
    public JsonElement Items { get; init; }
    public JsonElement RoomHistory { get; init; }
    public JsonElement BillingHandoffs { get; init; }
}
