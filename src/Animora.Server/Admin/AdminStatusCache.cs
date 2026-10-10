using Animora.Domain.Results;
using Animora.Server.Workers;
using Animora.Web.Admin.Models;
namespace Animora.Server.Admin;
/// <summary>Single-instance only (A-RULE 031). No circuit backplane; one refresh independent of viewers.</summary>
internal sealed class AdminStatusCache(IServiceScopeFactory scopes, WorkerHeartbeat heartbeat, TimeProvider time,
    ILogger<AdminStatusCache> logger) : BackgroundService
{
    internal static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(10);
    private sealed record Snapshot(AdminStatus? Status);
    private Snapshot _snapshot = new(null);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    internal Result<AdminStatus> Read()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot.Status is { } status ? Result<AdminStatus>.Ok(status) : Result<AdminStatus>.Fail(ErrorCodes.ValidationFailed);
    }
    internal async Task RefreshOnceAsync(CancellationToken ct = default)
    {
        await _refresh.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<IAdminStatusSnapshotSource>().ReadAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, new(status)); heartbeat.Success("admin-status");
        }
        finally { _refresh.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        heartbeat.Register("admin-status", RefreshPeriod);
        using var timer = new PeriodicTimer(RefreshPeriod, time);
        try
        {
            do
            {
                try { await RefreshOnceAsync(stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    Volatile.Write(ref _snapshot, new(null));
                    // Do not log SQL/connection details or secrets from provider exceptions.
                    logger.LogWarning("Admin status snapshot refresh failed; worker will retry.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override void Dispose() { base.Dispose(); _refresh.Dispose(); }
}
