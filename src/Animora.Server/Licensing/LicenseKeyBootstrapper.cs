using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Licensing;

/// <summary>Deferred bootstrap after the schema check; unavailable PostgreSQL must not kill the host.</summary>
internal sealed class LicenseKeyBootstrapper(IServiceScopeFactory scopes, IBackgroundAdmission admission,
    ILogger<LicenseKeyBootstrapper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var lease = await admission.AcquireAsync(timeout.Token).ConfigureAwait(false);
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<WorkerExecutionContext>().MarkWorker();
                var schema = scope.ServiceProvider.GetRequiredService<AnimoraServerDbContext>();
                var applied = await schema.Database.GetAppliedMigrationsAsync(timeout.Token).ConfigureAwait(false);
                if (ServerSchemaVersion.IsExpectedHead(applied.LastOrDefault()))
                {
                    await scope.ServiceProvider.GetRequiredService<LicenseKeyManager>().EnsureAsync(timeout.Token).ConfigureAwait(false);
                    logger.LogInformation("license.key_bootstrapped"); return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("license.key_bootstrap_deferred"); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
