using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Options;

public sealed class LocalStoreOptions
{
    public const string SectionName = "LocalStore";
    private string _backupDirectory = string.Empty;

    public string DatabasePath { get; set; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Animora", "animora.db")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Animora", "animora.db");
    public bool EncryptionEnabled { get; set; }
    public int BusyTimeoutMs { get; set; } = 5000;
    public int MaxPageSize { get; set; } = 200;

    /// <summary>Dev-only plaintext key file when the OS keystore is unavailable; logged on every read.</summary>
    public bool AllowInsecureKeyFallback { get; set; }
    /// <summary>Encryption provider approval (K2). Encryption cannot be enabled until approved.</summary>
    public bool K2Approved { get; set; }
    /// <summary>
    /// Set by development/test compositions that knowingly run an unencrypted store, so the startup does not
    /// emit the "plaintext store in a shipped configuration" warning (SQL-01). Never set by the shipped app.
    /// </summary>
    public bool PlaintextStoreAcknowledged { get; set; }
    /// <summary>Informational: how the shipped defaults resolved encryption (set by <see cref="LocalStoreEncryptionDefaults"/>).</summary>
    public StoreEncryptionDecision EncryptionDecision { get; set; }
    public int WalCheckpointPages { get; set; } = 1000;
    public int BackupRetentionCount { get; set; } = 5;

    /// <summary>SQL-02: relayed (<c>Succeeded</c>) outbox rows older than this are deleted. Dead-lettered rows are never pruned.
    /// Must be &gt;= <see cref="ActivityRetentionDays"/> so the activity feed can always be rebuilt for its whole retained window.</summary>
    public int OutboxRetentionDays { get; set; } = 180;

    /// <summary>SQL-02: activity-feed rows older than this are deleted (the feed is a cache with a proof, not the audit trail).</summary>
    public int ActivityRetentionDays { get; set; } = 180;

    /// <summary>SQL-02: rows removed per DELETE statement, so one compaction never holds a huge write lock.</summary>
    public int CompactionBatchSize { get; set; } = 1000;

    /// <summary>SQL-02: when a compaction pass removes at least this many rows the scheduler reclaims the space with VACUUM.</summary>
    public int VacuumAfterCompactedRows { get; set; } = 5000;

    /// <summary>When true, first-run seeding also inserts realistic sample owners/pets so screens are
    /// immediately testable. Explicit opt-in only: a fresh clinic never receives sample users.</summary>
    public bool SeedSampleData { get; set; }

    /// <summary>Explicit external legacy-preview input; never configured for normal clinic storage.</summary>
    public string? ClinicBakPreviewPath { get; set; }

    /// <summary>
    /// Allows a sale movement to take the derived on-hand quantity below zero. Enabled by default so
    /// offline posting is not blocked by a stale or incomplete stock snapshot. The authoritative server
    /// never allows it, so such a post is flagged provisional with <c>STOCK_WOULD_GO_NEGATIVE</c> on the
    /// invoice (D07) until settlement accepts or rejects it.
    /// </summary>
    public bool AllowNegativeStockOnSale { get; set; } = true;

    /// <summary>Directory containing the database file.</summary>
    public string DataDirectory => Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!;

    /// <summary>Backup directory; defaults to <c>&lt;DataDirectory&gt;/backups</c> when empty.</summary>
    public string BackupDirectory
    {
        get => string.IsNullOrWhiteSpace(_backupDirectory) ? Path.Combine(DataDirectory, "backups") : _backupDirectory;
        set => _backupDirectory = value ?? string.Empty;
    }

    public Result Validate()
    {
        if (ClinicBakPreviewPath is not null)
        {
            try
            {
                var directory = new DirectoryInfo(DataDirectory);
                if (SeedSampleData || directory.Parent?.Name != "Animora.LocalTests" ||
                    directory.Name != Seed.ClinicBakPreviewData.ProfileName(ClinicBakPreviewPath))
                    return Result.Fail(ErrorCodes.ValidationFailed, "Clinic preview requires its own content-addressed isolated profile and no sample fixtures.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Result.Fail(ErrorCodes.ValidationFailed, "Clinic preview input is missing or invalid.");
            }
        }
        if (EncryptionEnabled && !K2Approved) return Result.Fail(ErrorCodes.StoreEncryptionNotApproved);
        if (BackupRetentionCount <= 0)
            return Result.Fail(ErrorCodes.ValidationFailed, "BackupRetentionCount must be positive.");
        if (OutboxRetentionDays < 7 || ActivityRetentionDays < 7)
            return Result.Fail(ErrorCodes.ValidationFailed, "Outbox/Activity retention must be at least 7 days.");
        if (OutboxRetentionDays < ActivityRetentionDays)
            return Result.Fail(ErrorCodes.ValidationFailed,
                "OutboxRetentionDays must be >= ActivityRetentionDays (the activity feed is rebuilt from the outbox).");
        if (CompactionBatchSize is < 1 or > 50000)
            return Result.Fail(ErrorCodes.ValidationFailed, "CompactionBatchSize must be between 1 and 50000.");
        if (WalCheckpointPages <= 0)
            return Result.Fail(ErrorCodes.ValidationFailed, "WalCheckpointPages must be positive.");
        return Result.Ok();
    }
}
