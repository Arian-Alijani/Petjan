using System.Net;
using System.Net.Http;
using Animora.Client.Services.Identity;
using Animora.Contracts.Profile;
using Animora.Contracts.Wire.Licensing;

namespace Animora.Client.Services.Profile;

/// <summary>
/// Server-backed <see cref="IClinicAccountStatusSource"/> for the Profile page's «اشتراک باقی‌مانده» (remaining
/// subscription) panel. Reads <c>GET /licensing/status</c> through the shared online client (<see cref="OnlineApi"/>),
/// so the stored bearer token is attached by the pipeline's <c>BearerAuthHandler</c>. The licence grant's plan code and
/// <c>ValidUntilUtc</c> become the plan name and subscription expiry the UI already renders.
///
/// <para>Registered only when the server endpoint is configured (see <c>AddAnimoraOnlineIdentity</c>). An unreachable
/// server, an unauthenticated session, or a tenant without a grant all degrade to an empty/stale status instead of
/// throwing, so the panel shows «اطلاعات در دسترس نیست» rather than crashing the page.</para>
/// </summary>
public sealed class ServerClinicAccountStatusSource(OnlineApi api) : IClinicAccountStatusSource
{
    public async Task<ClinicAccountStatus> GetAsync(CancellationToken ct = default)
    {
        var exchange = await api.SendAsync(HttpMethod.Get, "licensing/status", body: null, bearer: null, ct)
            .ConfigureAwait(false);
        using var response = exchange.Response;
        if (response is null)
        {
            // Server unreachable / timed out — report nothing rather than a wrong number.
            return new ClinicAccountStatus(IsStale: true);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // No server session (bearer missing/expired) or device not approved: no reliable status to show.
            return new ClinicAccountStatus(IsStale: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new ClinicAccountStatus(IsStale: true);
        }

        var status = await OnlineApi.ReadAsync<LicenseStatusResponse>(response, ct).ConfigureAwait(false);
        if (status is null)
        {
            return new ClinicAccountStatus(IsStale: true);
        }

        // The grant status is LicenseGrantStatus.ToString() ("Active"/"Suspended"/"Revoked"), or "NotGranted" when the
        // tenant has no grant row. Only an Active grant surfaces its expiry as the live subscription; a suspended/revoked/
        // missing grant leaves the expiry null so the UI shows "منقضی شده"/"اطلاعات در دسترس نیست" via Remaining().
        var isActive = string.Equals(status.GrantStatus, "Active", StringComparison.OrdinalIgnoreCase);
        return new ClinicAccountStatus(
            PlanName: string.IsNullOrWhiteSpace(status.PlanCode) ? null : status.PlanCode,
            SubscriptionExpiresAtUtc: isActive ? status.ValidUntilUtc : null,
            ObservedAtUtc: status.ServerTimeUtc,
            IsStale: false);
    }
}
