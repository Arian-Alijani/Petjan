using System;
using System.Linq;
using Animora.Domain.Identity;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The default role→permission matrix is internally consistent: every string resolves to a
/// declared code, Manager holds the whole catalog, and Critical authority is not handed to
/// clinical or front-desk roles.
/// </summary>
public sealed class RolePermissionMatrixTests
{
    [Fact]
    public void EveryRoleCode_IsADeclaredPermissionCode()
    {
        foreach (var (role, codes) in DefaultRolePermissions.All)
        {
            foreach (var code in codes)
            {
                Assert.True(PermissionCodes.Exists(code), $"{role} references undeclared code '{code}'.");
            }
        }
    }

    [Fact]
    public void Catalog_HasNoDuplicatesAndClassifiesEveryCode()
    {
        Assert.Equal(PermissionCodes.All.Count, PermissionCodes.All.Distinct(StringComparer.Ordinal).Count());

        var catalog = PermissionCodes.CreateCatalog();
        Assert.Equal(PermissionCodes.All.Count, catalog.Count);
        Assert.All(catalog, p => Assert.Equal(PermissionCodes.RiskOf(p.Code), p.Risk));
        Assert.Equal("sales", catalog.Single(p => p.Code == PermissionCodes.SalesPost).Category);
    }

    [Fact]
    public void RiskClassification_MatchesThePhaseTable()
    {
        string[] critical =
        [
            PermissionCodes.SalesVoid, PermissionCodes.SalesRefund, PermissionCodes.PaymentsReverse,
            PermissionCodes.FinancePeriodClose, PermissionCodes.UsersManage, PermissionCodes.RolesManage,
            PermissionCodes.DevicesManage,
        ];
        string[] high =
        [
            PermissionCodes.SalesPost, PermissionCodes.PaymentsRegister, PermissionCodes.CashierSessionClose,
            PermissionCodes.InventoryAdjust, PermissionCodes.FinancePost, PermissionCodes.SalesDiscountApprove,
        ];

        Assert.All(critical, c => Assert.Equal(RiskLevel.Critical, PermissionCodes.RiskOf(c)));
        Assert.All(high, c => Assert.Equal(RiskLevel.High, PermissionCodes.RiskOf(c)));
        Assert.All(
            PermissionCodes.All.Where(c => c.EndsWith(".read", StringComparison.Ordinal)),
            c => Assert.Equal(RiskLevel.Low, PermissionCodes.RiskOf(c)));
        Assert.Equal(RiskLevel.Normal, PermissionCodes.RiskOf(PermissionCodes.PatientsWrite));
    }

    [Fact]
    public void Manager_HoldsTheWholeCatalog()
    {
        var manager = DefaultRolePermissions.For(StaffRole.Manager);

        Assert.Equal(PermissionCodes.All.Count, manager.Count);
        Assert.All(PermissionCodes.All, code => Assert.Contains(code, manager));
    }

    [Theory]
    [InlineData(StaffRole.Doctor)]
    [InlineData(StaffRole.Receptionist)]
    public void ClinicalAndFrontDeskRoles_HoldNoCriticalCode(StaffRole role)
    {
        var codes = DefaultRolePermissions.For(role);

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.NotEqual(RiskLevel.Critical, PermissionCodes.RiskOf(code)));
    }

    [Fact]
    public void Cashier_HoldsNoCriticalCode_AndAccountantOnlyTheFinanceSubset()
    {
        Assert.All(
            DefaultRolePermissions.For(StaffRole.Cashier),
            code => Assert.NotEqual(RiskLevel.Critical, PermissionCodes.RiskOf(code)));

        var accountantCritical = DefaultRolePermissions.For(StaffRole.Accountant)
            .Where(c => PermissionCodes.RiskOf(c) == RiskLevel.Critical)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [PermissionCodes.FinancePeriodClose, PermissionCodes.PaymentsReverse],
            accountantCritical);
    }

    [Fact]
    public void SystemManagerDefinition_RejectsGrantAndRevoke()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var manager = RoleDefinition.CreateSystem("T1", StaffRole.Manager, clock);

        Assert.Equal("role.manager", manager.DisplayKey);
        Assert.Equal(PermissionCodes.All.Count, manager.PermissionCodes.Count);
        Assert.Throws<InvalidOperationException>(() => manager.Grant(PermissionCodes.AuditRead));
        Assert.Throws<InvalidOperationException>(() => manager.Revoke(PermissionCodes.AuditRead));
    }

    [Fact]
    public void NonManagerDefinition_GrantsAndRevokesKnownCodesOnly()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var doctor = RoleDefinition.CreateSystem("T1", StaffRole.Doctor, clock);

        Assert.False(doctor.Has(PermissionCodes.AppointmentsCancel));
        doctor.Grant(PermissionCodes.AppointmentsCancel);
        doctor.Grant(PermissionCodes.AppointmentsCancel);
        Assert.True(doctor.Has(PermissionCodes.AppointmentsCancel));

        doctor.Revoke(PermissionCodes.AppointmentsCancel);
        Assert.False(doctor.Has(PermissionCodes.AppointmentsCancel));

        Assert.Throws<ArgumentException>(() => doctor.Grant("does.not.exist"));
    }

    [Fact]
    public void Membership_IsTheOnlyUserTenantLink_AndDenyWinsOverExtra()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var membership = StaffMembership.Create("T1", "U1", StaffRole.Cashier, clock);

        Assert.True(membership.IsActive);
        Assert.Equal("T1", membership.TenantId);
        Assert.Equal("U1", membership.UserId);

        membership.GrantExtra(PermissionCodes.SalesRefund);
        membership.Deny(PermissionCodes.SalesRefund);

        Assert.DoesNotContain(PermissionCodes.SalesRefund, membership.ExtraPermissionCodes);
        Assert.Contains(PermissionCodes.SalesRefund, membership.DeniedPermissionCodes);

        membership.End(clock);
        var endedAt = membership.EndedUtc;
        membership.End(clock);

        Assert.False(membership.IsActive);
        Assert.Equal(endedAt, membership.EndedUtc);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
