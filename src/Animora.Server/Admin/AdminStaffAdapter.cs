using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

/// <summary>Per-clinic staff roster for the operator console (read-only; mutations live in the staff commands adapter).</summary>
internal sealed class AdminStaffAdapter(OperatorReadQueries queries,AdminSession session) : IAdminStaffQueries
{
    public async Task<Result<AdminPage<StaffAccountSummary>>> ListAsync(string tenantId,StaffListQuery query,CancellationToken ct=default)
    {
        _=session.OperatorId;
        if(string.IsNullOrWhiteSpace(tenantId)||tenantId.Length>26||query.Search?.Length>100||query.Role?.Length>32)
            return Result<AdminPage<StaffAccountSummary>>.Fail(ErrorCodes.ValidationFailed);
        var page=await queries.StaffAsync(tenantId,query.Role,query.Search,query.Page,query.PageSize,ct).ConfigureAwait(false);
        if(page is null)return Result<AdminPage<StaffAccountSummary>>.Fail(ErrorCodes.NotFound);
        return Result<AdminPage<StaffAccountSummary>>.Ok(new(page.Items.Select(r=>new StaffAccountSummary(r.MembershipId,r.UserId,r.Name,r.UserName,
            r.Role.ToString(),r.Active,r.Devices)).ToArray(),page.Page,page.Size,page.Total));
    }
}
