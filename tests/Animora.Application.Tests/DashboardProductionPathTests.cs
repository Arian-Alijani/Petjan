using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Clinical;
using Animora.Application.UseCases.Dashboard;
using Animora.Domain.Patients;
using Animora.Domain.ValueObjects;
using Animora.Domain.Identity;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// D34: the gaps the persistence-only tests could not see. Everything below runs the production path - real local
/// session and membership, real dispatcher/authorization, the real service and SQLite - with no hand-built access
/// object, no cache double and no seed that pre-answers the actor question.
/// </summary>
public sealed class DashboardProductionPathTests
{
    private static async Task<(DashboardDayWindow Window, string UserId)> TodayAsync(LocalIdentityHost host)
    {
        using var scope = host.Services.CreateScope();
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        var window = DashboardDayWindow.For(ClinicCalendar.LocalDay(host.Clock.UtcNow.UtcTicks, zone), zone);
        await Task.CompletedTask;
        return (window, host.Cache.Current!.UserId!);
    }

    [Fact]
    public async Task DoctorSession_CountsAssignedAndAdministeredWork_NotTheAuditActor_AndSeesNoMoney()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Doctor);
        var (window, me) = await TodayAsync(host);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var clock = new Animora.Persistence.Tests.FakeClock(window.StartUtc.AddHours(10));
            var owner = Owner.Register(Tenant.LocalDefaultId, "مالک", "09120000001", clock);
            var pet = Pet.Register(Tenant.LocalDefaultId, owner.Id, "بیمار", PetSpecies.Dog, clock);
            db.AddRange(owner, pet);
            Visit Done(string doctor)
            {
                var v = Visit.Open(Tenant.LocalDefaultId, pet.Id, owner.Id, doctor, VisitReason.Checkup, clock);
                Assert.True(v.Start().IsSuccess);
                Assert.True(v.SetAssessment("ok").IsSuccess);
                Assert.True(v.Complete(clock).IsSuccess);
                return v;
            }
            // Assigned to the signed-in doctor although a receptionist authored the audit rows; another doctor's visit is excluded.
            db.AddRange(Done(me), Done("someone-else"));
            db.Add(VaccinationRecord.Administer(Tenant.LocalDefaultId, pet.Id, "DHPP", "A", 1,
                window.StartUtc.AddHours(11), me, clock).Value!);
            db.Add(VaccinationRecord.Administer(Tenant.LocalDefaultId, pet.Id, "RABIES", "B", 1,
                window.StartUtc.AddHours(11), "someone-else", clock).Value!);
            await db.SaveChangesAsync();
        }

        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var snapshot = await service.RefreshSnapshotAsync(UserRole.Manager); // requested layout is not authority
        Assert.Equal(UserRole.Doctor, snapshot.Role);
        Assert.Equal("1", snapshot.Stats.Single(x => x.Label == "ویزیت‌های من").Value);
        Assert.Equal("1", snapshot.Stats.Single(x => x.Label == "واکسن‌ها").Value);
        Assert.DoesNotContain(snapshot.Stats, x => x.IconKey is "IconArrowUpRight" or "IconCart" or "IconBox");
        Assert.Null(snapshot.RevenuePulse);
        Assert.Null(snapshot.DoctorLoads);
    }

    [Fact]
    public async Task CashierSession_ShowsSignedNonCreditSettlement_AndRefundReducesIt_ThroughTheService()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Cashier);
        var (window, _) = await TodayAsync(host);
        var at = window.StartUtc.AddHours(9);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            foreach (var (method, amount) in new[]
                     { (PaymentMethod.Card, 100L), (PaymentMethod.Card, -30L), (PaymentMethod.Credit, 500L) })
                db.Add(Payment.Create(Tenant.LocalDefaultId, "invoice", method, Money.Irt(amount), at, "cashier").Value!);
            await db.SaveChangesAsync();
        }

        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var snapshot = await service.RefreshSnapshotAsync(UserRole.Manager);
        Assert.Equal(UserRole.Cashier, snapshot.Role);
        var settlement = snapshot.Stats[0];
        Assert.Contains("غیرنسیه", settlement.Label); // credit (500) is not money collected
        Assert.Equal("70", settlement.Value);         // 100 receipt - 30 refund, signed
        Assert.Contains("بازپرداخت: 30", settlement.Foot);
        Assert.Null(snapshot.RevenuePulse);
    }

    [Fact]
    public async Task RevokedDoctor_CannotReadAfterRoleDowngrade_EvenWithWarmCache_AndEmptyStoreIsARealZero()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Doctor);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var warm = await service.GetSnapshotAsync(UserRole.Doctor);
        Assert.Equal("0", warm.Stats.Single(x => x.Label == "ویزیت‌های من").Value); // successful empty read: a real zero
        await DashboardAuthorizationTests.ChangeMemberAsync(host, m => m.Suspend());
        await Assert.ThrowsAsync<Animora.Contracts.Dashboard.DashboardQueryException>(() => service.GetSnapshotAsync(UserRole.Doctor));
    }
}
