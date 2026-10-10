using Animora.Domain.Results;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Animora.Persistence.PostgreSql.Files;
namespace Animora.Server.Admin;
internal sealed class AdminFilesAdapter(FileMismatchService service, AdminSession session) : IAdminFileQueries, IAdminFileCommands
{
    public async Task<Result<AdminPage<FileMismatchSummary>>> ListMismatchesAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        var rows = await service.ListAsync(page, pageSize, ct).ConfigureAwait(false);
        return Result<AdminPage<FileMismatchSummary>>.Ok(new(rows.Items.Select(x => new FileMismatchSummary(x.TenantId,
            x.TenantCode ?? x.TenantId, x.Id, x.SizeBytes, x.ShaPrefix, x.UpdatedUtc)).ToArray(), rows.Page, rows.Size, rows.Total));
    }
    public Task<Result<MutationReceipt>> RequeueAsync(FileMismatchActionForm form, CancellationToken ct = default) => ChangeAsync(form, true, ct);
    public Task<Result<MutationReceipt>> AbandonAsync(FileMismatchActionForm form, CancellationToken ct = default) => ChangeAsync(form, false, ct);
    private async Task<Result<MutationReceipt>> ChangeAsync(FileMismatchActionForm form, bool requeue, CancellationToken ct)
    {
        _ = session.OperatorId;
        if (!form.Confirmed || string.IsNullOrWhiteSpace(form.TenantId) || form.TenantId.Length > 26 ||
            string.IsNullOrWhiteSpace(form.FileId) || form.FileId.Length > 26) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        var result = requeue ? await service.RequeueAsync(form.TenantId, form.FileId, session.Actor, ct).ConfigureAwait(false)
            : await service.AbandonAsync(form.TenantId, form.FileId, session.Actor, ct).ConfigureAwait(false);
        return result.IsSuccess ? Result<MutationReceipt>.Ok(new()) : Result<MutationReceipt>.Fail(result.Error);
    }
}
