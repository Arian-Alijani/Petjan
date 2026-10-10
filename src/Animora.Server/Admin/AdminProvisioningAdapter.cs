using Animora.Domain.Results;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Animora.Persistence.PostgreSql.Provisioning;
namespace Animora.Server.Admin;
internal sealed class AdminProvisioningAdapter(ProvisioningQueries queries, IProvisioningIntentStore store,
    AdminSession session, TimeProvider time) : IAdminProvisioningQueries, IAdminProvisioningCommands
{
    public async Task<Result<AdminPage<ProvisioningSummary>>> ListAsync(string? status, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        ProvisioningIntentStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ProvisioningIntentStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
                return Result<AdminPage<ProvisioningSummary>>.Fail(ErrorCodes.ValidationFailed);
            filter = parsed;
        }
        var rows = await queries.ListAsync(filter, page, pageSize, ct).ConfigureAwait(false);
        return Result<AdminPage<ProvisioningSummary>>.Ok(new(rows.Items.Select(x => new ProvisioningSummary(x.Id, x.IdempotencyKey,
            x.ClientId, x.Status.ToString(), x.Attempts, x.ErrorCode, x.ReceivedUtc, x.CompletedUtc)).ToArray(), rows.Page, rows.Size, rows.Total));
    }
    public async Task<Result<MutationReceipt>> RetryAsync(string id, bool confirm, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (!confirm || string.IsNullOrWhiteSpace(id) || id.Length > 26) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        var result = await store.RetryAsync(id, session.Actor, time.GetUtcNow(), ct).ConfigureAwait(false);
        return result.IsSuccess ? Result<MutationReceipt>.Ok(new()) : Result<MutationReceipt>.Fail(result.Error);
    }
}
