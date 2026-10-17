using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Hosting;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

/// <summary>Operator-side staff mutations for one clinic. Passwords come back once through <see cref="MutationReceipt"/>.</summary>
internal sealed class AdminStaffCommandsAdapter(ClinicStaffAdministration staff,AdminSession session,IHttpContextAccessor accessor) : IAdminStaffCommands
{
    private string Correlation=>accessor.HttpContext is { } http?CorrelationHeader.Resolve(http):Guid.NewGuid().ToString("N");
    public async Task<Result<MutationReceipt>> CreateStaffAsync(string tenantId,CreateStaffAdminForm form,CancellationToken ct=default)
    {
        var actor=session.Actor;
        // Posted SSR forms bind absent text fields as null, so every string is read null-safely.
        if(!form.Confirmed||string.IsNullOrWhiteSpace(form.Name)||string.IsNullOrWhiteSpace(form.UserName)||string.IsNullOrWhiteSpace(form.Role))
            return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        return (await staff.CreateAsync(tenantId,form.Name,form.UserName,form.Role,actor,Correlation,ct).ConfigureAwait(false)).Map(p=>new MutationReceipt(p,tenantId));
    }
}
