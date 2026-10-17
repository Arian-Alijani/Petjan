using Animora.Domain.Identity;
using Animora.Domain.Subscriptions;

namespace Animora.Server.Operator.Models;

internal sealed record CreateClinicStaffRequest(string Name, string UserName, string Role);
internal sealed record OperatorLoginRequest(string UserName, string Password);
internal sealed record OperatorPasswordRequest(string OldPassword, string NewPassword);
internal sealed record PlanRequest(string Code, string Name, int DefaultSeatLimit, long MonthlyAmountMinor, int TrialDays);
internal sealed record TenantListItem(string Id, string? Code, string DisplayName, TenantStatus TenantStatus,
    SubscriptionStatus? SubscriptionStatus, DateTimeOffset? PeriodEndUtc, int? SeatLimit, int SeatsUsed);
internal sealed record CreateTenantRequest(string TenantCode, string DisplayName, string? LegalName,
    string? Timezone, string OwnerUserName, string OwnerDisplayName, string? OwnerInitialPassword,
    string PlanCode, int? SeatLimitOverride, bool StartTrial = true);
