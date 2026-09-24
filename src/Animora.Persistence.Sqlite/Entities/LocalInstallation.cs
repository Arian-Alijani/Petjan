using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Installation-level identity of this local store; exactly one row. Never replicated.</summary>
[LocalOnly]
public sealed class LocalInstallation
{
    public const int FingerprintHashLength = 64;
    public const int CurrentSchemaVersion = 3;

    private LocalInstallation()
    {
    }

    public string InstallationId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    /// <summary>Hex SHA-256 of the machine fingerprint (never the raw fingerprint).</summary>
    public string MachineFingerprintHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int SchemaVersion { get; private set; }

    internal static LocalInstallation Create(string installationId, string tenantId, string fingerprintHash,
        DateTimeOffset nowUtc) => new()
    {
        InstallationId = installationId,
        TenantId = tenantId,
        MachineFingerprintHash = fingerprintHash,
        CreatedAtUtc = nowUtc,
        SchemaVersion = CurrentSchemaVersion,
    };
}
