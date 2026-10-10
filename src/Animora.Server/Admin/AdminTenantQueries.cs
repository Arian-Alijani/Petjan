using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

internal sealed class AdminTenantQueries(OperatorReadQueries queries,AdminSession session) : IAdminTenantQueries
{
    public async Task<Result<AdminPage<TenantSummary>>> ListAsync(TenantListQuery query,CancellationToken ct=default)
    {
        _=session.OperatorId;
        if(query.Search?.Length>200||query.Status?.Length>40)return Result<AdminPage<TenantSummary>>.Fail(ErrorCodes.ValidationFailed);
        var page=await queries.TenantsAsync(query.Status,query.Search,query.Page,query.PageSize,ct).ConfigureAwait(false);
        return Result<AdminPage<TenantSummary>>.Ok(new(page.Items.Select(t=>new TenantSummary(t.Id,t.Code??"",t.DisplayName,t.Status.ToString())).ToArray(),page.Page,page.Size,page.Total));
    }
    public async Task<Result<TenantDetail>> GetAsync(string id,CancellationToken ct=default)
    {
        _=session.OperatorId;var row=await queries.TenantAsync(id,ct).ConfigureAwait(false);
        if(row is null)return Result<TenantDetail>.Fail(ErrorCodes.NotFound);
        var t=row.Tenant;var s=row.Subscription;
        return Result<TenantDetail>.Ok(new(new(t.Id,t.Code??"",t.DisplayName,t.Status.ToString()),
            s is null?null:new(s.Id,s.Status.ToString(),s.PlanId,s.SeatLimit,s.CurrentPeriodEndUtc,s.ConcurrencyStamp),
            row.Seats,row.Events.Select(e=>new TenantEvent(e.Type,e.OccurredUtc)).ToArray(),row.Devices,row.Licenses,row.GrantSequence));
    }
}
