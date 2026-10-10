using System.Text;
using System.Text.Json;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Resilience;
using Animora.Server.Workers;
using Microsoft.AspNetCore.DataProtection;
namespace Animora.Server.Provisioning;

internal sealed class ProvisioningIntentWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly IBackgroundAdmission _admission;
    public ProvisioningIntentWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, TimeProvider clock,
        WorkerHeartbeat heartbeat, ILogger<ProvisioningIntentWorker> logger)
        : base("provisioning-intent", TimeSpan.FromSeconds(2), clock, heartbeat, logger) { _scopes = scopes; _admission = admission; }
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var admission = await _admission.EnterAsync(ct).ConfigureAwait(false);
        await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
        sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
        var store = sp.GetRequiredService<IProvisioningIntentStore>();
        // One live claim at a time stays inside the 60-second lease even on a slow host; <=5 requirement is preserved.
        var claims = await store.ClaimAsync(Clock.GetUtcNow(), 1, ct).ConfigureAwait(false);
        Period = claims.Count == 0 ? TimeSpan.FromSeconds(Math.Min(15, Period.TotalSeconds * 2)) : TimeSpan.FromSeconds(2);
        foreach (var claim in claims)
        {
            try
            {
                var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("Animora.Provisioning.Intent.v1");
                var bytes = claim.PayloadProtected is null ? null : protector.Unprotect(claim.PayloadProtected);
                ProvisioningRequest? r = null;
                try { if (bytes != null) r = JsonSerializer.Deserialize<ProvisioningRequest>(bytes, JsonSerializerOptions.Web); }
                finally { if (bytes != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
                if (r is null) { await store.FailAsync(claim, ErrorCodes.ValidationFailed.Code, true, TimeSpan.Zero, Clock.GetUtcNow(), ct).ConfigureAwait(false); continue; }
                var actor = new ActorRef(ActorKind.WebsiteIntent, claim.ClientId);
                var result = await sp.GetRequiredService<ProvisioningEngine>().ProvisionAsync(r, actor, ct).ConfigureAwait(false);
                if (result.IsFailure || result.Value is not { } created)
                { await store.FailAsync(claim, result.Error.Code, ProvisioningIntentStore.IsPermanent(result.Error.Code), ServerRetryPolicy.NextDelay(claim.Attempts, Random.Shared.NextDouble), Clock.GetUtcNow(), ct).ConfigureAwait(false); continue; }
                var temporaryPassword = created.OwnerTemporaryPassword;
                if (created.WasReplay && r.OwnerInitialPassword is null)
                {
                    // A crash after engine commit cannot retrieve the once-returned password. Rotate it, audited, before completing the intent.
                    var recovered = await sp.GetRequiredService<ProvisioningReplayCredentials>().RecoverAsync(created.TenantId, actor, ct).ConfigureAwait(false);
                    if (recovered.IsFailure) { await store.FailAsync(claim, recovered.Error.Code, false, ServerRetryPolicy.NextDelay(claim.Attempts, Random.Shared.NextDouble), Clock.GetUtcNow(), ct).ConfigureAwait(false); continue; }
                    temporaryPassword = recovered.Value;
                }
                var password = temporaryPassword is null ? null : Encoding.UTF8.GetBytes(protector.Protect(temporaryPassword));
                var json = JsonSerializer.Serialize(new { tenantId = created.TenantId, tenantCode = created.TenantCode, subscriptionId = created.SubscriptionId });
                await store.CompleteAsync(claim, json, password, Clock.GetUtcNow(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            { await store.FailAsync(claim, ErrorCodes.Unexpected.Code, false, ServerRetryPolicy.NextDelay(claim.Attempts, Random.Shared.NextDouble), Clock.GetUtcNow(), ct).ConfigureAwait(false); }
        }
    }
}
