namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>Snapshot of the local store state reported by startup and maintenance.</summary>
public sealed record LocalStoreDiagnostics(
    string DatabasePath,
    string NativeSqliteVersion,
    bool EncryptionActive,
    KeySource KeySource,
    string SchemaVersion,
    bool RecoveredFromCrash,
    bool Quarantined,
    long DatabaseBytes,
    DateTimeOffset? LastBackupUtc);
