using System.Globalization;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

internal sealed class AdminLicenseAdapter(OperatorReadQueries queries,LicenseAdministration licenses,LicenseKeyManager keys,AdminSession session) : IAdminLicenseCommands
{
    private static Result<MutationReceipt> Receipt(Result r)=>r.IsFailure?Result<MutationReceipt>.Fail(r.Error):Result<MutationReceipt>.Ok(new());
    public async Task<Result<AdminPage<LicenseSummary>>> ListAsync(string tenantId,int page=1,int pageSize=50,CancellationToken ct=default)
    { _=session.OperatorId;var rows=await queries.LicensesAsync(tenantId,page,pageSize,ct).ConfigureAwait(false);return Result<AdminPage<LicenseSummary>>.Ok(new(rows.Items.Select(l=>new LicenseSummary(l.Id,l.DeviceId,l.Sequence,l.Status.ToString(),l.ExpiresUtc)).ToArray(),rows.Page,rows.Size,rows.Total,rows.Sequence.ToString(CultureInfo.InvariantCulture))); }
    public async Task<Result<MutationReceipt>> RevokeAsync(string tenantId,LicenseActionForm form,CancellationToken ct=default)=>
        !form.Confirmed?Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed):Receipt(await licenses.RevokeAsync(tenantId,form.LicenseId,form.Stamp,form.Reason,session.Actor,Guid.NewGuid().ToString("N"),ct).ConfigureAwait(false));
    public async Task<Result<MutationReceipt>> RecheckAsync(string tenantId,string ifMatchStamp,CancellationToken ct=default)=>
        Receipt(await licenses.RecheckAsync(tenantId,ifMatchStamp,session.Actor,Guid.NewGuid().ToString("N"),ct).ConfigureAwait(false));
    public async Task<Result<MutationReceipt>> RotateKeyAsync(string ifMatchStamp,CancellationToken ct=default)=>
        (await keys.RotateAsync(ifMatchStamp,session.Actor,ct).ConfigureAwait(false)).Map(_=>new MutationReceipt());
    public async Task<Result<LicenseKeySet>> GetKeySetAsync(CancellationToken ct=default)
    { _=session.OperatorId;var result=await keys.PublicAsync(ct).ConfigureAwait(false);return Result<LicenseKeySet>.Ok(new(result.Keys.Select(k=>new PublicLicenseKey(k.KeyId,k.Algorithm,k.PublicKeyDerBase64,k.NotBeforeUtc,k.NotAfterUtc)).ToArray())); }
}
