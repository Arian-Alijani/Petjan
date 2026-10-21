namespace Animora.Web.Admin.Models;

public sealed record AdminPage<T>(IReadOnlyList<T> Items,int Page,int PageSize,long Total,string? Stamp=null)
{
    public static (int Page,int Size) Bounds(int page,int pageSize)=>(Math.Clamp(page,1,int.MaxValue/200),Math.Clamp(pageSize,1,200));
}
/// <summary>One live-log line (already redacted server-side). <c>Level</c> is Debug/Information/Warning/Error/Fatal/Verbose.</summary>
public sealed record LogLineModel(long Sequence,DateTimeOffset Ts,string Level,string Source,string Message);
/// <summary><c>Level</c> is a MINIMUM level (Information/Warning/Error); <c>AfterSequence</c> is the poll cursor (0 = from the buffer start).</summary>
public sealed record LogFilter(string? Level=null,string? Search=null,long AfterSequence=0,int Take=200);
public sealed record LogSnapshot(IReadOnlyList<LogLineModel> Lines,long LastSequence);
public sealed record NamedStatus(string Name,string Status);
public sealed record WorkerStatus(string Name,double LastSuccessAgeSeconds);
public sealed record AdminCounters(long CommandsInProgress,long OutboxPending,long OutboxDeadLettered,long IntakeInProgress);
public sealed record AdminPg(int PoolInUse,int PoolMax);
public sealed record AdminStatus(IReadOnlyList<NamedStatus> Readiness,IReadOnlyList<WorkerStatus> Workers,AdminCounters Counters,
    AdminPg Pg,string Version,double UptimeSeconds,long ExpiringSoon,long PastDue);
public sealed record TenantListQuery(string? Status=null,string? Search=null,int Page=1,int PageSize=50);
public sealed record TenantSummary(string Id,string Code,string Name,string Status);
public sealed record SubscriptionSummary(string Id,string Status,string PlanId,int Seats,DateTimeOffset PeriodEnd,string ConcurrencyStamp);
public sealed record TenantEvent(string Type,DateTimeOffset OccurredUtc);
public sealed record TenantDetail(TenantSummary Core,SubscriptionSummary? Subscription,int SeatsUsed,IReadOnlyList<TenantEvent> Events,int DevicesCount,int LicensesCount,long GrantSequence);
public sealed record PlanSummary(string Id,string Code,string Name,int Seats,long MonthlyAmountMinor,int TrialDays,bool Active,string Stamp);
public sealed record LicenseSummary(string Id,string DeviceId,long Sequence,string Status,DateTimeOffset ExpiresUtc);
public sealed record PublicLicenseKey(string Id,string Algorithm,string PublicKey,DateTimeOffset CreatedUtc,DateTimeOffset? NotAfterUtc);
public sealed record LicenseKeySet(IReadOnlyList<PublicLicenseKey> Keys);
public sealed record StaffAccountSummary(string MembershipId,string UserId,string Name,string UserName,string Role,bool Active,int Devices);
public sealed record StaffListQuery(string? Role=null,string? Search=null,int Page=1,int PageSize=50);
public sealed record ProductSummary(string Id,string Sku,string Name,string CategoryName,string Kind,bool Active,long PriceMinor,string Currency,
    decimal OnHand,decimal ReorderLevel,string StockState,decimal SalesQty);
public sealed record CategoryOption(string Id,string Name);
public sealed record ProductListQuery(string? Search=null,string? CategoryId=null,string? Stock=null,long? MinPrice=null,long? MaxPrice=null,string? Sort=null,int Page=1,int PageSize=50);
public sealed record ProductCatalog(AdminPage<ProductSummary> Page,IReadOnlyList<CategoryOption> Categories);
public sealed class BackupRequestForm { public bool Confirmed { get; set; } }
/// <summary>BAK-02: per-tenant export request — the clinic id plus the mandatory confirmation.</summary>
public sealed class TenantBackupRequestForm { public string TenantId { get; set; } = string.Empty; public bool Confirmed { get; set; } }
/// <summary>BAK-02: one per-tenant export (<see cref="Rows"/> across <see cref="Tables"/> tables; both 0 in a plain list).</summary>
public sealed record TenantBackupReceipt(string FileName,string TenantId,long SizeBytes,DateTimeOffset CreatedUtc,int Tables,long Rows);
/// <summary>T8/T9: summary of a format-v2 clinic import bundle (<c>clinic-import.animora.json</c>) after validation or import.</summary>
public sealed record TenantImportReceipt(
    string TenantId,
    int FormatVersion,
    string SourceFingerprint,
    int Owners,
    int Pets,
    int Visits,
    int Vaccinations,
    int Antiparasites,
    int LabResults,
    int Appointments,
    int Invoices,
    int StockReceipts,
    int Products,
    int Services,
    int Categories,
    int Insurers,
    IReadOnlyList<string> Warnings);
public sealed class TenantImportForm
{
    public string BundleJson { get; set; } = string.Empty;
    public bool Confirmed { get; set; }
}
public sealed record BackupReceipt(string FileName,long SizeBytes,DateTimeOffset CreatedUtc);
public sealed record MutationReceipt(string? TemporaryPassword=null,string? TenantId=null);
public sealed class SubscriptionActionForm
{
    public string Stamp { get; set; }=""; public bool Confirmed { get; set; }
    public int? Days { get; set; } public DateTime? Until { get; set; } public int? Seats { get; set; }
    public string Reason { get; set; }=""; public string PlanCode { get; set; }="";
}
public sealed class CreateTenantForm
{
    public string Code { get; set; }=""; public string Name { get; set; }=""; public string OwnerUserName { get; set; }="";
    public string OwnerName { get; set; }=""; public string PlanCode { get; set; }="standard";
    public int? Seats { get; set; } public bool StartTrial { get; set; }=true;
    public string IdempotencyKey { get; set; }=Guid.NewGuid().ToString("N");
}
public sealed class CreatePlanForm
{
    public string Code { get; set; }=""; public string Name { get; set; }=""; public int Seats { get; set; }=1;
    public long AmountMinor { get; set; } public int TrialDays { get; set; }
}
public sealed class LicenseActionForm
{
    public string LicenseId { get; set; }=""; public string Stamp { get; set; }=""; public string Reason { get; set; }="";
    public bool Confirmed { get; set; }
}
public sealed class CreateStaffAdminForm
{
    public string Name { get; set; }=""; public string UserName { get; set; }=""; public string Role { get; set; }="Receptionist";
    public bool Confirmed { get; set; }
}
public sealed class ResetStaffPasswordForm { public string UserId { get; set; }=""; public bool Confirmed { get; set; } }
public sealed class SetStaffActiveForm { public string UserId { get; set; }=""; public bool Active { get; set; } public bool Confirmed { get; set; } }
public sealed class PlanActionForm { public string Id { get; set; }=""; public string Stamp { get; set; }=""; }
public sealed class KeyActionForm { public string Stamp { get; set; }=""; public bool Confirmed { get; set; } }

public sealed record ProvisioningSummary(string Id, string IdempotencyKey, string ClientId, string Status, int Attempts,
    string? ErrorCode, DateTimeOffset ReceivedUtc, DateTimeOffset? CompletedUtc);
public sealed record WebsiteClientSummary(string Id, string KeyId, string Label, string Status, DateTimeOffset CreatedUtc, DateTimeOffset? LastUsedUtc);
public sealed record WebsiteClientSecret(string KeyId, string Secret)
{
    public override string ToString() => $"WebsiteClientSecret {{ KeyId = {KeyId}, Secret = [redacted] }}";
}
public sealed record FileMismatchSummary(string TenantId, string TenantCode, string FileId, long SizeBytes, string? ShaPrefix, DateTimeOffset UpdatedUtc);
public sealed record AuditSummary(string Id, DateTimeOffset OccurredUtc, string? ActorId, string Action, string? TenantId,
    string Outcome, string CorrelationId, string? Detail);
public sealed record AdminAuditQuery(string? TenantId, string? Action, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 50, string? ActorId = null);
public sealed record InsightStatus(bool Enabled, bool HasConsentAdr);
public sealed class ProvisioningActionForm { public string Id { get; set; } = ""; public bool Confirmed { get; set; } }
public sealed class WebsiteClientCreateForm { public string Label { get; set; } = ""; public bool Confirmed { get; set; } }
public sealed class WebsiteClientActionForm { public string Id { get; set; } = ""; public bool Confirmed { get; set; } }
public sealed class FileMismatchActionForm
{
    public string TenantId { get; set; } = "";
    public string FileId { get; set; } = "";
    public string Operation { get; set; } = "";
    public bool Confirmed { get; set; }
}
