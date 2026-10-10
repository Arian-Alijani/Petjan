using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Commands;
using Microsoft.Extensions.Options;
namespace Animora.Server.Workers;

internal sealed class CommandIntakeRetentionWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly IBackgroundAdmission _admission;
    public CommandIntakeRetentionWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<CommandIntakeRetentionWorker> logger)
        : base("intake-retention", TimeSpan.FromDays(1), clock, heartbeat, logger) { _scopes = scopes; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var lease = await _admission.EnterAsync(ct).ConfigureAwait(false);
        await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
        sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
        var retention = sp.GetRequiredService<IOptions<CommandIntakeRetentionOptions>>().Value;
        await sp.GetRequiredService<CommandIntakeCompactor>().CompactAsync(Clock.GetUtcNow().AddDays(-retention.FullRecordDays), ct).ConfigureAwait(false);
    }
}
