using Animora.Domain.Results;
using Animora.Web.Admin.Models;
namespace Animora.Web.Admin.Ports;

public interface IAdminSession { string OperatorId { get; } string Name { get; } }
public interface IAdminStatusReader { Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct=default); }
public interface IAdminTenantQueries
{
    Task<Result<AdminPage<TenantSummary>>> ListAsync(TenantListQuery query,CancellationToken ct=default);
    Task<Result<TenantDetail>> GetAsync(string id,CancellationToken ct=default);
}
public interface IAdminStaffQueries
{
    Task<Result<AdminPage<StaffAccountSummary>>> ListAsync(string tenantId,StaffListQuery query,CancellationToken ct=default);
}
public interface IAdminStaffCommands
{
    Task<Result<MutationReceipt>> CreateStaffAsync(string tenantId,CreateStaffAdminForm form,CancellationToken ct=default);
    Task<Result<MutationReceipt>> ResetStaffPasswordAsync(string tenantId,ResetStaffPasswordForm form,CancellationToken ct=default);
}
public interface IAdminProductQueries
{
    Task<Result<ProductCatalog>> ListAsync(string tenantId,ProductListQuery query,CancellationToken ct=default);
}
public interface IAdminSubscriptionCommands
{
    Task<Result<MutationReceipt>> MutateAsync(string id,string operation,SubscriptionActionForm form,CancellationToken ct=default);
    Task<Result<MutationReceipt>> ExtendAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"extend",form,ct);
    Task<Result<MutationReceipt>> ReduceAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"reduce",form,ct);
    Task<Result<MutationReceipt>> SuspendAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"suspend",form,ct);
    Task<Result<MutationReceipt>> ReinstateAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"reinstate",form,ct);
    Task<Result<MutationReceipt>> CancelAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"cancel",form,ct);
    Task<Result<MutationReceipt>> RenewAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"renew",form,ct);
    Task<Result<MutationReceipt>> ChangeSeatsAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"seats",form,ct);
    Task<Result<MutationReceipt>> ChangePlanAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"plan",form,ct);
    Task<Result<MutationReceipt>> ResetOwnerPasswordAsync(string id,SubscriptionActionForm form,CancellationToken ct=default)=>MutateAsync(id,"reset-owner-password",form,ct);
    Task<Result<MutationReceipt>> CreateTenantAsync(CreateTenantForm form,string idempotencyKey,CancellationToken ct=default);
}
public interface IAdminPlanCommands
{
    Task<Result<AdminPage<PlanSummary>>> ListAsync(int page=1,int pageSize=50,CancellationToken ct=default);
    Task<Result<PlanSummary>> CreateAsync(CreatePlanForm form,CancellationToken ct=default);
    Task<Result<MutationReceipt>> DeactivateAsync(string id,string ifMatchStamp,CancellationToken ct=default);
}
public interface IAdminLicenseCommands
{
    Task<Result<AdminPage<LicenseSummary>>> ListAsync(string tenantId,int page=1,int pageSize=50,CancellationToken ct=default);
    Task<Result<MutationReceipt>> RevokeAsync(string tenantId,LicenseActionForm form,CancellationToken ct=default);
    Task<Result<MutationReceipt>> RecheckAsync(string tenantId,string ifMatchStamp,CancellationToken ct=default);
    Task<Result<MutationReceipt>> RotateKeyAsync(string ifMatchStamp,CancellationToken ct=default);
    Task<Result<LicenseKeySet>> GetKeySetAsync(CancellationToken ct=default);
}

public interface IAdminProvisioningQueries
{
    Task<Result<AdminPage<ProvisioningSummary>>> ListAsync(string? status, int page = 1, int pageSize = 50, CancellationToken ct = default);
}
public interface IAdminProvisioningCommands
{
    Task<Result<MutationReceipt>> RetryAsync(string id, bool confirm, CancellationToken ct = default);
}
public interface IAdminWebsiteClients
{
    Task<Result<AdminPage<WebsiteClientSummary>>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<Result<WebsiteClientSecret>> CreateAsync(WebsiteClientCreateForm form, CancellationToken ct = default);
    Task<Result<MutationReceipt>> RevokeAsync(string id, bool confirm, CancellationToken ct = default);
}
public interface IAdminFileQueries
{
    Task<Result<AdminPage<FileMismatchSummary>>> ListMismatchesAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
}
public interface IAdminFileCommands
{
    Task<Result<MutationReceipt>> RequeueAsync(FileMismatchActionForm form, CancellationToken ct = default);
    Task<Result<MutationReceipt>> AbandonAsync(FileMismatchActionForm form, CancellationToken ct = default);
}
public interface IAdminAuditQueries
{
    Task<Result<AdminPage<AuditSummary>>> OperatorAsync(AdminAuditQuery query, CancellationToken ct = default);
    Task<Result<AdminPage<AuditSummary>>> TenantAsync(AdminAuditQuery query, CancellationToken ct = default);
}
public interface IAdminInsightStatus
{
    Task<Result<InsightStatus>> GetAsync(CancellationToken ct = default);
}
