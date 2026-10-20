using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Animora.Client.Services.Diagnostics;

/// <summary>Runs bounded local maintenance after the UI is responsive.</summary>
public sealed class MaintenanceScheduler(
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<MaintenanceScheduler> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private DateTimeOffset? _lastBackupUtc;
    private DateTimeOffset? _lastVacuumUtc;
    private DateTimeOffset? _lastCompactUtc;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            await RunDueMaintenanceAsync(ct);
            while (await timer.WaitForNextTickAsync(ct))
                await RunDueMaintenanceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The maintenance scheduler loop stopped unexpectedly");
        }
    }

    internal async Task RunDueMaintenanceAsync(CancellationToken ct = default)
    {
        if (LocalCommandProcessor.IsPassRunning) return;
        var now = clock.UtcNow;
        if (_lastBackupUtc is null || now - _lastBackupUtc >= TimeSpan.FromDays(1))
        {
            await RunOneAsync(StoreMaintenanceKind.Backup, now, ct);
            _lastBackupUtc = now;
        }
        // SQL-02: retention runs before the weekly vacuum so the freed pages are reclaimed in the same cycle.
        var reclaimNow = false;
        if (_lastCompactUtc is null || now - _lastCompactUtc >= TimeSpan.FromDays(1))
        {
            reclaimNow = await CompactAsync(ct);
            _lastCompactUtc = now;
        }
        if (reclaimNow || _lastVacuumUtc is null || now - _lastVacuumUtc >= TimeSpan.FromDays(7))
        {
            await RunOneAsync(StoreMaintenanceKind.Vacuum, now, ct);
            _lastVacuumUtc = now;
        }
    }

    /// <summary>Runs the SQL-02 retention pass; returns true when it removed enough rows to justify a VACUUM now.</summary>
    private async Task<bool> CompactAsync(CancellationToken ct)
    {
        if (LocalCommandProcessor.IsPassRunning) return false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
            var threshold = scope.ServiceProvider.GetRequiredService<IOptions<LocalStoreOptions>>().Value.VacuumAfterCompactedRows;
            var result = await maintenance.CompactAsync(null, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning("Local store compaction failed: {Code} {Detail}", result.Error.Code, result.Detail);
                return false;
            }
            return result.Value!.Total >= threshold;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Local store compaction threw; retrying on the next cycle");
            return false;
        }
    }

    private async Task RunOneAsync(StoreMaintenanceKind kind, DateTimeOffset now, CancellationToken ct)
    {
        if (LocalCommandProcessor.IsPassRunning) return;
        await using var scope = scopes.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
        var success = kind == StoreMaintenanceKind.Backup
            ? (await maintenance.BackupAsync(ct).ConfigureAwait(false)).IsSuccess
            : (await maintenance.VacuumAsync(ct).ConfigureAwait(false)).IsSuccess;

        var audit = scope.ServiceProvider.GetService<IAuditWriter>();
        if (audit is not null)
        {
            var operationId = IdGenerator.NewId(now);
            await audit.WriteAsync(new AuditRecord(string.Empty, string.Empty, string.Empty, now,
                AuditCategory.Maintenance, $"store.{kind.ToString().ToLowerInvariant()}", null, null,
                operationId, operationId, success ? AuditOutcome.Success : AuditOutcome.Failed,
                null, null, null, null, null), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }
}
