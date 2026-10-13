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

    /// <summary>Max pooled connections of the read/write OLTP data source. Unset in configuration = <see cref="ForProfile"/> default.</summary>
    public int OltpWriteMax { get; set; } = 6;

    /// <summary>Max pooled connections of the read-only OLTP data source. Unset in configuration = <see cref="ForProfile"/> default.</summary>
    public int OltpReadMax { get; set; } = 3;

    /// <summary>Max pooled connections of the DataProtection key-ring data source.</summary>
    public int KeyRingMax { get; set; } = 1;

    /// <summary>Dedicated pool for bounded feed, recovery and relay jobs. Unset in configuration = <see cref="ForProfile"/> default.</summary>
    public int WorkerMax { get; set; } = 2;

    /// <summary>
    /// IMP-01: recommended pool sizes for a memory profile. They exactly fill the validator's connection budget of the
    /// profile (2 GiB: 6+3+1+2 = 12, 4 GiB: 11+5+1+3 = 20), so moving <c>Database:ProfileGiB</c> from 2 to 4 scales
    /// every pool automatically; an explicit <c>Database:*Max</c> value always wins.
    /// </summary>
    public static (int OltpWrite, int OltpRead, int KeyRing, int Worker) ForProfile(int profileGiB) =>
        profileGiB == 4 ? (11, 5, 1, 3) : (6, 3, 1, 2);

    /// <summary>
    /// IMP-09: request-path sync admission permits (concurrent snapshot builds per instance). 0 = auto =
    /// <c>max(2, OltpWriteMax - 2)</c> (keeps ~2 write connections free for mutations).
    /// </summary>
    public int SyncAdmissionMax { get; set; }

    /// <summary>IMP-05: seconds a sync request waits for an admission permit before it is shed with 503 + Retry-After (1..60). Default 5.</summary>
    public int SyncAdmissionWaitSeconds { get; set; } = 5;

    /// <summary>IMP-05: max callers allowed to QUEUE for a sync permit; further callers are shed at once. 0 = auto = 4 x permits (1..1024).</summary>
    public int SyncAdmissionQueueMax { get; set; }

    /// <summary>IMP-09: background-worker admission permits. 0 = auto = <c>clamp(WorkerMax, 1, 2)</c>.</summary>
    public int WorkerAdmissionMax { get; set; }

    /// <summary>IMP-09: Npgsql command timeout in seconds (5..300). Default 30.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>IMP-01: seconds an idle pooled connection lives before it is pruned (Npgsql <c>Connection Idle Lifetime</c>, 10..3600). Default 300.</summary>
    public int ConnectionIdleLifetimeSeconds { get; set; } = 300;

    /// <summary>IMP-01: how often idle connections are pruned (Npgsql <c>Connection Pruning Interval</c>, 1..600). Default 10.</summary>
    public int ConnectionPruningIntervalSeconds { get; set; } = 10;

    /// <summary>Server memory profile in GiB (2 or 4); decides the total pool budget.</summary>
    public int PoolSum => OltpWriteMax + OltpReadMax + KeyRingMax + WorkerMax;

    public int ProfileGiB { get; set; } = 2;

    /// <summary>
    /// How long a request waits for a free pooled connection (Npgsql <c>Timeout</c>) before failing fast.
    /// Pool exhaustion then surfaces as a typed 503 + Retry-After instead of hanging up to the 30 s command timeout.
    /// </summary>
    public int PoolWaitSeconds { get; set; } = 5;
}
