using System.Collections.Frozen;
using Animora.Domain.Cashier;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Purchasing;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;

namespace Animora.Application.Security;

/// <summary>
/// Explicit metadata for a user/role-management operation, required by <see cref="AuthorizationPolicies.NotSelfDestructive"/>.
/// The caller (the handler that loaded the target and counted managers) supplies facts the policy cannot derive
/// from a plain <see cref="User"/>.
/// </summary>
/// <param name="TargetUserId">The user being changed.</param>
/// <param name="IsDestructive">Disable, delete, or demote (removing the Manager role).</param>
/// <param name="TargetIsManager">The target currently holds the Manager role.</param>
/// <param name="ActiveManagerCount">Active managers in the tenant INCLUDING the target, counted by the caller.</param>
public sealed record UserManagementOperation(string TargetUserId, bool IsDestructive, bool TargetIsManager,
    int ActiveManagerCount);

/// <summary>
/// The five named, reflection-free policy predicates (stage 2 of <see cref="PermissionEvaluator"/>) and their static
/// <c>(permission code, concrete resource type)</c> bindings. A policy is only ever evaluated for a bound pair — never
/// for a request without a resource, never for an unrelated resource type.
/// </summary>
public static class AuthorizationPolicies
{
    public const string OwnRecordOrManager = nameof(OwnRecordOrManager);
    public const string AssignedDoctorOnly = nameof(AssignedDoctorOnly);
    public const string OpenCashSessionOwner = nameof(OpenCashSessionOwner);
    public const string PeriodOpen = nameof(PeriodOpen);
    public const string NotSelfDestructive = nameof(NotSelfDestructive);

    /// <summary>Exactly the five predicates, keyed by ordinal name.</summary>
    public static FrozenDictionary<string, Func<AuthorizationSubject, object, bool>> Predicates { get; } =
        new Dictionary<string, Func<AuthorizationSubject, object, bool>>(StringComparer.Ordinal)
        {
            [OwnRecordOrManager] = IsOwnRecordOrManager,
            [AssignedDoctorOnly] = IsAssignedDoctor,
            [OpenCashSessionOwner] = IsCashSessionOwner,
            [PeriodOpen] = IsPeriodOpen,
            [NotSelfDestructive] = IsNotSelfDestructive,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly string[] None = [];
    private static readonly string[] OwnOnly = [OwnRecordOrManager];
    private static readonly string[] DoctorOnly = [AssignedDoctorOnly];
    private static readonly string[] CashOwnerOnly = [OpenCashSessionOwner];
    private static readonly string[] PeriodOnly = [PeriodOpen];
    private static readonly string[] SelfSafeOnly = [NotSelfDestructive];

    /// <summary>Static binding table: the policies that apply to <paramref name="permissionCode"/> on <paramref name="resource"/>.</summary>
    public static IReadOnlyList<string> PoliciesFor(string permissionCode, object? resource) => (permissionCode, resource) switch
    {
        (_, null) => None,
        (PermissionCodes.VisitsWrite, Visit) => DoctorOnly,
        (PermissionCodes.VisitsComplete, Visit) => DoctorOnly,
        (PermissionCodes.SalesDraft, Invoice) => OwnOnly,
        (PermissionCodes.PurchasingManage, PurchaseOrder) => OwnOnly,
        (PermissionCodes.CashierSessionClose, CashSession) => CashOwnerOnly,
        (PermissionCodes.FinancePost, AccountingPeriod) => PeriodOnly,
        (PermissionCodes.FinancePeriodClose, AccountingPeriod) => PeriodOnly,
        (PermissionCodes.UsersManage, User) => SelfSafeOnly,
        (PermissionCodes.UsersManage, UserManagementOperation) => SelfSafeOnly,
        (PermissionCodes.RolesManage, UserManagementOperation) => SelfSafeOnly,
        _ => None,
    };

    private static bool IsManager(AuthorizationSubject s) => s.Role == StaffRole.Manager;

    private static bool Same(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);

    private static bool IsOwnRecordOrManager(AuthorizationSubject s, object resource)
    {
        if (IsManager(s)) return true;
        var createdBy = resource switch
        {
            Invoice i => i.CreatedByUserId,
            PurchaseOrder p => p.CreatedByUserId,
            Expense e => e.CreatedByUserId,
            Appointment a => a.CreatedByUserId,
            _ => null,
        };
        return createdBy is not null && Same(createdBy, s.UserId);
    }

    private static bool IsAssignedDoctor(AuthorizationSubject s, object resource) =>
        resource is Visit v && (IsManager(s) || Same(v.DoctorUserId, s.UserId));

    private static bool IsCashSessionOwner(AuthorizationSubject s, object resource) =>
        resource is CashSession c && Same(c.CashierUserId, s.UserId);

    private static bool IsPeriodOpen(AuthorizationSubject s, object resource) =>
        resource is AccountingPeriod p && p.State == PeriodState.Open;

    /// <summary>
    /// Fail closed: a plain <see cref="User"/> carries no operation metadata (disable? delete? how many managers remain?),
    /// so it cannot prove non-destructiveness and is denied. Only <see cref="UserManagementOperation"/> can pass: a
    /// destructive change may target neither the actor nor the last active manager.
    /// </summary>
    private static bool IsNotSelfDestructive(AuthorizationSubject s, object resource)
    {
        if (resource is not UserManagementOperation op || op.TargetUserId.Length == 0) return false;
        if (!op.IsDestructive) return true;
        if (Same(op.TargetUserId, s.UserId)) return false;
        return !op.TargetIsManager || op.ActiveManagerCount > 1;
    }
}
