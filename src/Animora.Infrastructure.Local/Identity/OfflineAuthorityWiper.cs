using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Identity;

/// <summary>
/// <see cref="IOfflineAuthorityWiper"/>: on device revocation the capability snapshot is revoked (re-signed as revoked, so no write ever
/// passes again), then the sealed licence, the session and the credential mirror are deleted. Every step is attempted even if an earlier one
/// fails; the first failure is reported.
/// </summary>
public sealed class OfflineAuthorityWiper(ProtectedSessionStore sessions, SealedEntitlementStore sealedStore,
    IServiceScopeFactory scopes, IClock clock) : IOfflineAuthorityWiper
{
    /// <inheritdoc />
    public async Task<Result> WipeAsync(CancellationToken ct = default)
    {
        var first = Result.Ok();

        var mirror = await ((IOfflineCredentialMirrorStore)sessions).ReadAsync(ct).ConfigureAwait(false);
        if (mirror.IsSuccess && mirror.Value is { } m)
        {
            using var di = scopes.CreateScope();
            var capabilities = di.ServiceProvider.GetRequiredService<ICapabilityStore>();
            var revoked = await capabilities.RevokeAsync(m.TenantId, m.UserId, m.DeviceId, clock.UtcNow, ct).ConfigureAwait(false);
            if (revoked.IsFailure) first = revoked;
        }
        else if (mirror.IsFailure)
        {
            first = mirror.ToResult();
        }

        var sealedResult = await sealedStore.ClearAsync(ct).ConfigureAwait(false);
        if (first.IsSuccess && sealedResult.IsFailure) first = sealedResult;

        var sessionResult = await sessions.WipeAllAsync(ct).ConfigureAwait(false);
        if (first.IsSuccess && sessionResult.IsFailure) first = sessionResult;
        return first;
    }
}
