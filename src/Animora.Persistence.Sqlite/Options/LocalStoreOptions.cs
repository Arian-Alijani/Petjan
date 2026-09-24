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
    public int WalCheckpointPages { get; set; } = 1000;
    public int BackupRetentionCount { get; set; } = 5;

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
        if (EncryptionEnabled && !K2Approved) return Result.Fail(ErrorCodes.StoreEncryptionNotApproved);
        if (BackupRetentionCount <= 0)
            return Result.Fail(ErrorCodes.ValidationFailed, "BackupRetentionCount must be positive.");
        if (WalCheckpointPages <= 0)
            return Result.Fail(ErrorCodes.ValidationFailed, "WalCheckpointPages must be positive.");
        return Result.Ok();
    }
}
