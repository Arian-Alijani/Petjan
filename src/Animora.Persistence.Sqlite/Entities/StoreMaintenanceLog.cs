using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

public enum StoreMaintenanceKind
{
    Backup,
    Restore,
    Quarantine,
    Vacuum,
    CrashRecovery,
    Checkpoint,
}

/// <summary>Append-only record of a local store maintenance operation. Never replicated.</summary>
[LocalOnly]
public sealed class StoreMaintenanceLog
{
    public const int DetailLength = 400;

    private StoreMaintenanceLog()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public StoreMaintenanceKind Kind { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Detail { get; private set; }

    internal static StoreMaintenanceLog Create(StoreMaintenanceKind kind, string? detail, DateTimeOffset nowUtc) => new()
    {
        Id = IdGenerator.NewId(nowUtc),
        Kind = kind,
        OccurredAtUtc = nowUtc,
        Detail = detail is { Length: > DetailLength } ? detail[..DetailLength] : detail,
    };
}
