namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>Where the local store key comes from (reported by diagnostics).</summary>
public enum KeySource
{
    None,
    OsKeystore,
    InsecureFallback,
}
