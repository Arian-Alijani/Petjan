using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record StaffMembershipSyncContract
{
    public string UserId { get; init; } = string.Empty;
    public int Role { get; init; }
    public int Status { get; init; }
    public DateTimeOffset JoinedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; init; }
    public int WorkStartMinute { get; init; }
    public int WorkEndMinute { get; init; }
    public int WorkDaysMask { get; init; }
}
