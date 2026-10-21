using Animora.Domain.Results;
using Animora.Web.Admin.Models;
namespace Animora.Web.Admin.Ports;

public interface IAdminSession { string OperatorId { get; } string Name { get; } }
public interface IAdminStatusReader { Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct=default); }
/// <summary>Operator-only tail of the server log. Pull-based: the island polls only while the operator has live mode switched on.</summary>
public interface IAdminLiveLog { Task<Result<LogSnapshot>> ReadAsync(LogFilter filter,CancellationToken ct=default); }
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
    /// <summary>Resets one clinic user's password; the new temporary password is returned once in the receipt.</summary>
    Task<Result<MutationReceipt>> ResetStaffPasswordAsync(string tenantId,ResetStaffPasswordForm form,CancellationToken ct=default);
    /// <summary>Enables or disables one clinic user. Disabling ends the user's live sessions.</summary>
    Task<Result<MutationReceipt>> SetStaffActiveAsync(string tenantId,SetStaffActiveForm form,CancellationToken ct=default);
}
/// <summary>Operator-only full database backup (pg_dump). Files stay on the server; the download is a separate antiforgery-protected POST.</summary>
public interface IAdminBackup
{
    Task<Result<IReadOnlyList<BackupReceipt>>> ListAsync(CancellationToken ct=default);
    /// <summary>Creates a new full backup. <see cref="BackupRequestForm.Confirmed"/> is mandatory.</summary>
    Task<Result<BackupReceipt>> CreateAsync(BackupRequestForm form,CancellationToken ct=default);
    /// <summary>BAK-02: per-tenant exports (newest first); each file holds exactly one clinic's rows.</summary>
    Task<Result<IReadOnlyList<TenantBackupReceipt>>> ListTenantAsync(CancellationToken ct=default);
    /// <summary>BAK-02: exports exactly one clinic's data into an isolated artifact. <see cref="TenantBackupRequestForm.Confirmed"/> is mandatory.</summary>
    Task<Result<TenantBackupReceipt>> CreateTenantAsync(TenantBackupRequestForm form,CancellationToken ct=default);
}
public interface IAdminProductQueries
{
    Task<Result<ProductCatalog>> ListAsync(string tenantId,ProductListQuery query,CancellationToken ct=default);
}
/// <summary>Operator-only legacy <c>Clinic.bak</c> bundle (format v2) validator and tenant-isolated importer (T8/T9).</summary>
public interface IAdminTenantImport
{
    Task<Result<TenantImportReceipt>> ValidateAsync(string tenantId, ReadOnlyMemory<byte> utf8Json, CancellationToken ct = default);
    Task<Result<TenantImportReceipt>> ImportAsync(string tenantId, ReadOnlyMemory<byte> utf8Json, bool confirmed, CancellationToken ct = default);
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
/// <summary>Vendor-authored messages to a clinic's users (read-only on the desktop). <see cref="SendNotificationForm.Confirmed"/> is mandatory.</summary>
public interface IAdminNotificationCommands
{
    Task<Result<MutationReceipt>> SendAsync(string tenantId,SendNotificationForm form,CancellationToken ct=default);
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
