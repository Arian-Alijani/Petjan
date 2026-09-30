namespace Animora.Application.Ports;

/// <summary>Tenant-scoped local sync marker. E03 extends the cursor behind this seam.</summary>
public interface ISyncCursorStore
{
    Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default);
    Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default);
}
