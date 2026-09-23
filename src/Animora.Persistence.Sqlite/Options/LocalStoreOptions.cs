namespace Animora.Persistence.Sqlite.Options;

public sealed class LocalStoreOptions
{
    public const string SectionName = "LocalStore";
    public string DatabasePath { get; set; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Animora", "animora.db")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Animora", "animora.db");
    public bool EncryptionEnabled { get; set; }
    public int BusyTimeoutMs { get; set; } = 5000;
    public int MaxPageSize { get; set; } = 200;
}
