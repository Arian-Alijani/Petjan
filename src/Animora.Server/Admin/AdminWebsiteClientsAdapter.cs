using Animora.Domain.Results;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Server.Provisioning;
namespace Animora.Server.Admin;
internal sealed class AdminWebsiteClientsAdapter(WebsiteClientStore store, WebsiteClientService service, AdminSession session) : IAdminWebsiteClients
{
    public async Task<Result<AdminPage<WebsiteClientSummary>>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        var rows = await store.ListAsync(page, pageSize, ct).ConfigureAwait(false);
        return Result<AdminPage<WebsiteClientSummary>>.Ok(new(rows.Items.Select(x => new WebsiteClientSummary(x.Id, x.KeyId,
            x.Label, x.Status.ToString(), x.CreatedUtc, x.LastUsedUtc)).ToArray(), rows.Page, rows.Size, rows.Total));
    }
    public async Task<Result<WebsiteClientSecret>> CreateAsync(WebsiteClientCreateForm form, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (!form.Confirmed || string.IsNullOrWhiteSpace(form.Label) || form.Label.Length > 100)
            return Result<WebsiteClientSecret>.Fail(ErrorCodes.ValidationFailed);
        var result = await service.CreateAsync(form.Label, session.Actor, ct).ConfigureAwait(false);
        return result.IsSuccess && result.Value is { } issued ? Result<WebsiteClientSecret>.Ok(new(issued.KeyId, issued.Secret))
            : Result<WebsiteClientSecret>.Fail(result.Error);
    }
    public async Task<Result<MutationReceipt>> RevokeAsync(string id, bool confirm, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (!confirm || string.IsNullOrWhiteSpace(id) || id.Length > 26) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        var result = await service.RevokeAsync(id, session.Actor, ct).ConfigureAwait(false);
        return result.IsSuccess ? Result<MutationReceipt>.Ok(new()) : Result<MutationReceipt>.Fail(result.Error);
    }
}
