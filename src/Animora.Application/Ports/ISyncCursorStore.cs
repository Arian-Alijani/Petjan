namespace Animora.Application.Ports;

/// <summary>Tenant-scoped durable last-pass marker. E03 extends its persistence, not the UI contract.</summary>
public interface ISyncCursorStore
{
    Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default);
    Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default);
}
