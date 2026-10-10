namespace Animora.Persistence.PostgreSql.Options;

/// <summary>
/// PostgreSQL connection + pool budget (ARCHITECTURE §17). The connection string is bound
/// ONLY from the environment (<c>Database__ConnectionString</c>); <c>SecretGuard</c> rejects
/// it in JSON files, so it never appears in appsettings.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Npgsql connection string (env-only secret).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Max pooled connections of the read/write OLTP data source.</summary>
    public int OltpWriteMax { get; set; } = 6;

    /// <summary>Max pooled connections of the read-only OLTP data source.</summary>
    public int OltpReadMax { get; set; } = 3;

    /// <summary>Max pooled connections of the DataProtection key-ring data source.</summary>
    public int KeyRingMax { get; set; } = 1;

    /// <summary>Dedicated pool for bounded feed, recovery and relay jobs.</summary>
    public int WorkerMax { get; set; } = 2;

    /// <summary>Server memory profile in GiB (2 or 4); decides the total pool budget.</summary>
    public int PoolSum => OltpWriteMax + OltpReadMax + KeyRingMax + WorkerMax;

    public int ProfileGiB { get; set; } = 2;
}
