using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

internal sealed class AdminSubscriptionAdapter(ISubscriptionService subscriptions,OwnerCredentialAdministration owners,
    ProvisioningEngine provisioning,AdminSession session) : IAdminSubscriptionCommands
{
    public async Task<Result<MutationReceipt>> MutateAsync(string id,string operation,SubscriptionActionForm form,CancellationToken ct=default)
    {
        var actor=session.Actor;
        if(string.IsNullOrWhiteSpace(form.Stamp)||form.Stamp.Length>64||form.Reason.Length>200||form.PlanCode.Length>32)
            return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        if(operation is "suspend" or "cancel" or "reduce" && !form.Confirmed)return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        if(operation=="reset-owner-password")return (await owners.ResetAsync(id,form.Stamp,actor,ct).ConfigureAwait(false)).Map(p=>new MutationReceipt(p));
        if(operation is not ("extend" or "reduce" or "suspend" or "reinstate" or "cancel" or "renew" or "seats" or "plan"))return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        DateTimeOffset? until=form.Until is { } date?new DateTimeOffset(DateTime.SpecifyKind(date,DateTimeKind.Utc)):null;
        var result=await subscriptions.MutateAsync(id,form.Stamp,new(operation,form.Days,until,form.Reason,true,form.Seats,
            string.IsNullOrWhiteSpace(form.PlanCode)?null:form.PlanCode),actor,ct).ConfigureAwait(false);
        return result.Map(_=>new MutationReceipt());
    }
    public async Task<Result<MutationReceipt>> CreateTenantAsync(CreateTenantForm form,string idempotencyKey,CancellationToken ct=default)
    {
        var result=await provisioning.ProvisionAsync(new(idempotencyKey,form.Code,form.Name,null,null,form.OwnerUserName,form.OwnerName,null,form.PlanCode,form.Seats,form.StartTrial),session.Actor,ct).ConfigureAwait(false);
        return result.Map(r=>new MutationReceipt(r.WasReplay?null:r.OwnerTemporaryPassword,r.TenantId));
    }
}
