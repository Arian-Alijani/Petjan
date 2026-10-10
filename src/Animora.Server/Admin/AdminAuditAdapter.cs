using Animora.Domain.Results;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Animora.Persistence.PostgreSql.Audit;
using Animora.Persistence.PostgreSql.Operator;
namespace Animora.Server.Admin;
internal sealed class AdminAuditAdapter(AuditQueries queries, AdminSession session) : IAdminAuditQueries
{
    public async Task<Result<AdminPage<AuditSummary>>> OperatorAsync(AdminAuditQuery query, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        return Map(await queries.OperatorAsync(query.Action, query.TenantId, query.From, query.To, query.Page, query.PageSize, ct).ConfigureAwait(false));
    }
    public async Task<Result<AdminPage<AuditSummary>>> TenantAsync(AdminAuditQuery query, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (string.IsNullOrWhiteSpace(query.TenantId) || query.From is not { } from || query.To is not { } to ||
            to <= from || to - from > TimeSpan.FromDays(90)) return Result<AdminPage<AuditSummary>>.Fail(ErrorCodes.ValidationFailed);
        return Map(await queries.TenantAsync(query.TenantId, from, to, query.Page, query.PageSize, ct).ConfigureAwait(false));
    }
    private static Result<AdminPage<AuditSummary>> Map(Result<OperatorPage<AuditRow>> result)
    {
        if (result.IsFailure || result.Value is not { } rows) return Result<AdminPage<AuditSummary>>.Fail(result.Error);
        return Result<AdminPage<AuditSummary>>.Ok(new(rows.Items.Select(x => new AuditSummary(x.Id, x.OccurredUtc, x.ActorId,
            x.Action, x.TenantId, x.Outcome, x.CorrelationId, x.Detail)).ToArray(), rows.Page, rows.Size, rows.Total));
    }
}
