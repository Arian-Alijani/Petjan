using Animora.Domain.Subscriptions;
namespace Animora.Persistence.PostgreSql.Licensing;

public enum LicenseGrantStatus : short { Active, Suspended, Revoked }
public sealed class LicenseGrant
{
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public int SeatLimit { get; set; }
    public int MaxDevices { get; set; }
    public DateTimeOffset ValidUntilUtc { get; set; }
    public LicenseGrantStatus Status { get; set; }
    public long Sequence { get; set; }
    public int SourceEventSequence { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public static LicenseGrantStatus MapStatus(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod => LicenseGrantStatus.Active,
        SubscriptionStatus.Cancelled => LicenseGrantStatus.Revoked,
        _ => LicenseGrantStatus.Suspended,
    };
}
