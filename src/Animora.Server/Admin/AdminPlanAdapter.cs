using System.Globalization;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

internal sealed class AdminPlanAdapter(OperatorReadQueries queries,OperatorPlanAdministration plans,AdminSession session) : IAdminPlanCommands
{
    private static PlanSummary Map(Plan p)=>new(p.Id,p.Code,p.Name,p.DefaultSeatLimit,p.MonthlyAmountMinor,p.TrialDays,p.IsActive,p.CreatedUtc.ToString("O",CultureInfo.InvariantCulture));
    public async Task<Result<AdminPage<PlanSummary>>> ListAsync(int page=1,int pageSize=50,CancellationToken ct=default)
    { _=session.OperatorId;var result=await queries.PlansAsync(page,pageSize,ct).ConfigureAwait(false);return Result<AdminPage<PlanSummary>>.Ok(new(result.Items.Select(Map).ToArray(),result.Page,result.Size,result.Total)); }
    public async Task<Result<PlanSummary>> CreateAsync(CreatePlanForm form,CancellationToken ct=default)=>
        (await plans.CreateAsync(form.Code,form.Name,form.Seats,form.AmountMinor,form.TrialDays,session.Actor,ct).ConfigureAwait(false)).Map(Map);
    public async Task<Result<MutationReceipt>> DeactivateAsync(string id,string ifMatchStamp,CancellationToken ct=default)
    { var result=await plans.DeactivateAsync(id,ifMatchStamp,session.Actor,ct).ConfigureAwait(false);return result.IsFailure?Result<MutationReceipt>.Fail(result.Error):Result<MutationReceipt>.Ok(new()); }
}
