using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Workers;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;

/// <summary>
/// BAK-03: scheduled full-database backup. Closes the "recovery point = last manual click" gap by calling the exact same
/// <see cref="DatabaseBackupService.CreateAsync"/> the operator button uses, so it inherits the single-flight gate (a manual
/// backup in progress makes this tick skip, never collide), the fixed command line / env-only secrets and the retention prune.
/// Every outcome is written to the append-only operator audit as <c>backup.scheduled</c>/<c>backup.scheduled.failed</c> under the
/// System actor. The worker wakes hourly and only dumps when the newest on-disk backup (by its name-embedded UTC stamp, BAK-06) and
/// the last attempt are both older than the cadence — so restarts never trigger an extra dump and a failing pg_dump is retried once per cadence,
/// not hourly. Disable with <c>Admin:ScheduledBackupEnabled=false</c>.
/// <para>PITR / WAL archiving is Postgres-ops territory (archive_command + base backups) and is documented as the recommended complement,
/// not implemented in-app.</para>
/// </summary>
internal sealed class ScheduledBackupWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AdminOptions> _admin; private readonly IOptions<DatabaseOptions> _database;
    private DateTimeOffset? _lastAttemptUtc;
    private bool _warmedUp;

    public ScheduledBackupWorker(IServiceScopeFactory scopes, IOptions<AdminOptions> admin,
        IOptions<DatabaseOptions> database, TimeProvider clock, WorkerHeartbeat heartbeat, ILogger<ScheduledBackupWorker> logger)
        : base("scheduled-backup", TimeSpan.FromHours(1), clock, heartbeat, logger)
    { _scopes = scopes; _admin = admin; _database = database; }

    internal static TimeSpan Cadence(AdminOptions options) => TimeSpan.FromHours(Math.Clamp(options.ScheduledBackupIntervalHours, 1, 168));

    /// <summary>True when a scheduled dump is due: neither the newest backup nor the last attempt is within the cadence.</summary>
    internal static bool IsDue(DateTimeOffset now, TimeSpan cadence, DateTimeOffset? newestBackupUtc, DateTimeOffset? lastAttemptUtc)
    {
        var latest = newestBackupUtc is { } b && lastAttemptUtc is { } a ? (b > a ? b : a) : newestBackupUtc ?? lastAttemptUtc;
        return latest is null || now - latest.Value >= cadence;
    }

    protected override async Task TickAsync(CancellationToken ct)
    {
        // The base loop ticks immediately on start; the first real check happens one period later so a (re)start never triggers a dump.
        if (!_warmedUp) { _warmedUp = true; return; }
        var options = _admin.Value;
        if (!options.ScheduledBackupEnabled) return;
        var now = Clock.GetUtcNow();
        var newest = DatabaseBackupService.List(options.BackupDirectory).Count > 0
            ? DatabaseBackupService.List(options.BackupDirectory)[0].CreatedUtc : (DateTimeOffset?)null;
        if (!IsDue(now, Cadence(options), newest, _lastAttemptUtc)) return;

        // No shared worker permit is held: a dump can run for minutes and must not starve the outbox/relay workers. The
        // single-flight gate inside DatabaseBackupService already serialises this against manual backups.
        _lastAttemptUtc = now;
        var result = await DatabaseBackupService.CreateAsync(_database.Value.ConnectionString, options.BackupDirectory,
            options.PgDumpPath, now, ct).ConfigureAwait(false);
        // A manual backup holds the gate: skip silently, the next cadence (or the manual one itself) covers it.
        if (result.IsFailure && result.Error == ErrorCodes.ConcurrencyConflict) { _lastAttemptUtc = null; return; }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();
        var actor = new ActorRef(ActorKind.System);
        if (result.IsFailure)
            db.Audit.Add(OperatorAudit.Create(Clock.GetUtcNow(), actor, "backup.scheduled.failed", null,
                Guid.NewGuid().ToString("N"), "failure", new { code = result.Error.Code }));
        else
            db.Audit.Add(OperatorAudit.Create(Clock.GetUtcNow(), actor, "backup.scheduled", null,
                Guid.NewGuid().ToString("N"), "success", new { file = result.Value!.FileName, bytes = result.Value.SizeBytes }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
